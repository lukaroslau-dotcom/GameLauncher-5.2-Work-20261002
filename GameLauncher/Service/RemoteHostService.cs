using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameLauncher.Service;

public sealed class RemoteHostService : IDisposable
{
    private TcpListener? listener;
    private CancellationTokenSource? cancellation;
    private sealed class ClientSession(WebSocket socket) : IDisposable
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim FrameAvailable { get; } = new(0, 1);
        public SemaphoreSlim ClientReady { get; } = new(1, 1);
        public void SignalFrame() { if (FrameAvailable.CurrentCount == 0) FrameAvailable.Release(); }
        public void Acknowledge() { if (ClientReady.CurrentCount == 0) ClientReady.Release(); }
        public void Dispose() { FrameAvailable.Dispose(); ClientReady.Dispose(); }
    }

    private readonly ConcurrentDictionary<Guid,ClientSession> clients=new();
    private readonly ConcurrentDictionary<string,(int Count,DateTime Since)> failedPins=new();
    private readonly object captureGate = new();
    private int fps=60,quality=75,maxWidth=1280;
    private byte[]? latestFrame;
    private long frameSequence;
    private Task? captureTask;
    private DxgiScreenCapture? dxgiCapture;
    private Bitmap? gdiCapture;
    private Bitmap? scaledCapture;
    public bool IsRunning=>listener!=null;
    public int Port {get;private set;}=17840;
    public string Pin {get;private set;}="------";
    public IReadOnlyList<string> Urls {get;private set;}=Array.Empty<string>();
    public int ClientCount=>clients.Count;
    public string LastError {get;private set;}="";
    public string CaptureTechnology {get;private set;}="DXGI wird vorbereitet";
    public int ActualFps {get;private set;}
    public event Action? StatusChanged;

    public void Start(int requestedPort,int requestedFps,int requestedQuality,int requestedWidth)
    {
        if(IsRunning)return;
        Port=Math.Clamp(requestedPort,1025,65535);fps=Math.Clamp(requestedFps,15,60);quality=Math.Clamp(requestedQuality,35,92);maxWidth=Math.Clamp(requestedWidth,1280,1920);
        latestFrame=null;frameSequence=0;LastError="";ActualFps=0;CaptureTechnology="DXGI wird vorbereitet";
        Pin=RandomNumberGenerator.GetInt32(100000,1000000).ToString();
        cancellation=new CancellationTokenSource();
        listener=new TcpListener(IPAddress.Any,Port);listener.Start(8);
        Urls=FindLanAddresses().Select(ip=>$"http://{ip}:{Port}").ToList();
        captureTask=Task.Run(()=>CaptureLoop(cancellation.Token),cancellation.Token);
        _=AcceptLoop(cancellation.Token);StatusChanged?.Invoke();
    }
    public void Stop()
    {
        var current=listener;if(current==null)return;listener=null;
        cancellation?.Cancel();try{current.Stop();}catch{}
        foreach(var session in clients.Values)try{session.Socket.Abort();}catch{}
        clients.Clear();
        Task? producer=captureTask;captureTask=null;
        if(producer!=null)try{producer.Wait(TimeSpan.FromSeconds(1));}catch{}
        latestFrame=null;Urls=Array.Empty<string>();Pin="------";ActualFps=0;StatusChanged?.Invoke();
    }
    private async Task AcceptLoop(CancellationToken token)
    {
        while(!token.IsCancellationRequested&&listener!=null)
        {
            try{var client=await listener.AcceptTcpClientAsync(token);_ = HandleClient(client,token);}
            catch(OperationCanceledException){break;}catch(ObjectDisposedException){break;}catch{await Task.Delay(250,token).ConfigureAwait(false);}
        }
    }
    private async Task HandleClient(TcpClient client,CancellationToken hostToken)
    {
        using(client)
        {
            client.NoDelay=true;string remoteIp=(client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString()??"unknown";
            if(client.Client.RemoteEndPoint is IPEndPoint endpoint&&!IsPrivate(endpoint.Address))return;
            using var stream=client.GetStream();
            string? request=await ReadHeaders(stream,hostToken);if(request==null)return;
            var lines=request.Split("\r\n",StringSplitOptions.None);var first=lines[0].Split(' ');if(first.Length<2)return;
            string target=first[1];var headers=lines.Skip(1).Select(line=>line.Split(':',2)).Where(p=>p.Length==2).ToDictionary(p=>p[0].Trim(),p=>p[1].Trim(),StringComparer.OrdinalIgnoreCase);
            if(headers.TryGetValue("Upgrade",out var upgrade)&&upgrade.Equals("websocket",StringComparison.OrdinalIgnoreCase))
            {await UpgradeWebSocket(stream,target,headers,remoteIp,hostToken);return;}
            if(target.StartsWith("/manifest",StringComparison.OrdinalIgnoreCase)){await WriteResponse(stream,"application/manifest+json",Encoding.UTF8.GetBytes(Manifest),hostToken);return;}
            if(target.StartsWith("/icon",StringComparison.OrdinalIgnoreCase)){await WriteResponse(stream,"image/png",CreateIcon(),hostToken);return;}
            await WriteResponse(stream,"text/html; charset=utf-8",Encoding.UTF8.GetBytes(Page),hostToken);
        }
    }
    private async Task UpgradeWebSocket(NetworkStream stream,string target,Dictionary<string,string> headers,string remoteIp,CancellationToken hostToken)
    {
        string supplied=QueryValue(target,"pin");
        if(IsRateLimited(remoteIp)||supplied!=Pin||clients.Count>=2||!headers.TryGetValue("Sec-WebSocket-Key",out string? key))
        {
            if(supplied!=Pin)RegisterFailedPin(remoteIp);
            await WriteStatus(stream,"403 Forbidden",hostToken);return;
        }
        failedPins.TryRemove(remoteIp,out _);
        string accept=Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "+accept+"\r\n\r\n");
        await stream.WriteAsync(response,hostToken);
        using var socket=WebSocket.CreateFromStream(stream,true,null,TimeSpan.FromSeconds(20));
        using var session=new ClientSession(socket);var id=Guid.NewGuid();clients[id]=session;StatusChanged?.Invoke();
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        try{var completed=await Task.WhenAny(SendFrames(session,linked.Token),ReceiveInputs(session,linked.Token));await completed;}
        catch(Exception ex){LastError=ex.ToString();Debug.WriteLine(ex);}finally{linked.Cancel();clients.TryRemove(id,out _);try{socket.Abort();}catch{}StatusChanged?.Invoke();}
    }
    private async Task SendFrames(ClientSession session,CancellationToken token)
    {
        long sentSequence=-1;
        while(!token.IsCancellationRequested&&session.Socket.State==WebSocketState.Open)
        {
            // Safari confirms after drawing. That keeps TCP from queuing old frames and
            // always sends the newest available image instead of increasing latency.
            await session.ClientReady.WaitAsync(token);
            while(Volatile.Read(ref frameSequence)==sentSequence)
                await session.FrameAvailable.WaitAsync(token);
            byte[]? frame=Volatile.Read(ref latestFrame);if(frame==null)continue;
            sentSequence=Volatile.Read(ref frameSequence);
            await session.Socket.SendAsync(frame,WebSocketMessageType.Binary,true,token);
        }
    }
    private async Task ReceiveInputs(ClientSession session,CancellationToken token)
    {
        byte[] buffer=new byte[8192];
        while(!token.IsCancellationRequested&&session.Socket.State==WebSocketState.Open)
        {
            using var memory=new MemoryStream();WebSocketReceiveResult result;
            do{result=await session.Socket.ReceiveAsync(buffer,token);if(result.MessageType==WebSocketMessageType.Close)return;memory.Write(buffer,0,result.Count);}while(!result.EndOfMessage&&memory.Length<65536);
            if(result.MessageType!=WebSocketMessageType.Text)continue;
            try{using var json=JsonDocument.Parse(memory.ToArray());HandleInput(json.RootElement,session);}catch{}
        }
    }
    private static void HandleInput(JsonElement root,ClientSession session)
    {
        string type=root.TryGetProperty("type",out var t)?t.GetString()??"":"";
        double Number(string name)=>root.TryGetProperty(name,out var value)&&value.TryGetDouble(out var n)?n:0;
        string Text(string name)=>root.TryGetProperty(name,out var value)?value.GetString()??"":"";
        switch(type)
        {
            case "frameAck":session.Acknowledge();break;
            case "move":RemoteInput.MoveTo(Number("x"),Number("y"));break;
            case "delta":RemoteInput.MoveBy(Number("dx"),Number("dy"));break;
            case "click":RemoteInput.Click(Text("button"));break;
            case "down":RemoteInput.Button(Text("button"),true);break;
            case "up":RemoteInput.Button(Text("button"),false);break;
            case "scroll":RemoteInput.Scroll(Number("amount"));break;
            case "key":RemoteInput.Key(Text("key"));break;
            case "text":RemoteInput.Text(Text("text"));break;
        }
    }

    private async Task CaptureLoop(CancellationToken token)
    {
        var interval=TimeSpan.FromSeconds(1d/fps);int frames=0;long measuredAt=Stopwatch.GetTimestamp();
        try
        {
            while(!token.IsCancellationRequested)
            {
                if(clients.IsEmpty){await Task.Delay(80,token);measuredAt=Stopwatch.GetTimestamp();frames=0;continue;}
                long started=Stopwatch.GetTimestamp();
                try
                {
                    byte[] frame=CaptureJpeg();Volatile.Write(ref latestFrame,frame);Interlocked.Increment(ref frameSequence);
                    foreach(var client in clients.Values)client.SignalFrame();frames++;
                }
                catch(Exception ex)
                {
                    LastError=ex.Message;Debug.WriteLine(ex);CaptureTechnology="GDI-Fallback";
                    lock(captureGate){dxgiCapture?.Dispose();dxgiCapture=null;}
                    await Task.Delay(50,token);continue;
                }
                double seconds=Stopwatch.GetElapsedTime(measuredAt).TotalSeconds;
                if(seconds>=1){ActualFps=(int)Math.Round(frames/seconds);frames=0;measuredAt=Stopwatch.GetTimestamp();StatusChanged?.Invoke();}
                TimeSpan elapsed=Stopwatch.GetElapsedTime(started);TimeSpan wait=interval-elapsed;
                if(wait>TimeSpan.Zero)await Task.Delay(wait,token);
                else await Task.Yield();
            }
        }
        catch(OperationCanceledException){}
        finally
        {
            lock(captureGate){dxgiCapture?.Dispose();dxgiCapture=null;gdiCapture?.Dispose();gdiCapture=null;scaledCapture?.Dispose();scaledCapture=null;}
        }
    }

    private byte[] CaptureJpeg()
    {
        lock(captureGate)
        {
            Bitmap source;Rectangle bounds;
            try
            {
                dxgiCapture??=new DxgiScreenCapture();source=dxgiCapture.Capture(Math.Max(1,1000/fps));bounds=dxgiCapture.Bounds;CaptureTechnology="DXGI-Vollbild";
            }
            catch(Exception ex)
            {
                Debug.WriteLine("DXGI capture: "+ex.Message);dxgiCapture?.Dispose();dxgiCapture=null;
                bounds=System.Windows.Forms.Screen.PrimaryScreen?.Bounds??new Rectangle(0,0,1920,1080);
                if(gdiCapture==null||gdiCapture.Width!=bounds.Width||gdiCapture.Height!=bounds.Height){gdiCapture?.Dispose();gdiCapture=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);}
                using(Graphics graphics=Graphics.FromImage(gdiCapture))graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
                source=gdiCapture;CaptureTechnology="GDI-Fallback";
            }

            int width=Math.Min(maxWidth,bounds.Width),height=Math.Max(1,(int)Math.Round(bounds.Height*(double)width/bounds.Width));
            if(scaledCapture==null||scaledCapture.Width!=width||scaledCapture.Height!=height){scaledCapture?.Dispose();scaledCapture=new Bitmap(width,height,PixelFormat.Format24bppRgb);}
            using(Graphics graphics=Graphics.FromImage(scaledCapture))
            {
                graphics.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality=System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                graphics.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                graphics.DrawImage(source,0,0,width,height);
                DrawCursor(graphics,bounds,width,height);
            }
            using var memory=new MemoryStream(256*1024);var encoder=ImageCodecInfo.GetImageEncoders().First(e=>e.FormatID==ImageFormat.Jpeg.Guid);using var parameters=new EncoderParameters(1);parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)quality);scaledCapture.Save(memory,encoder,parameters);return memory.ToArray();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size,Flags;public IntPtr Cursor;public NativePoint Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)]public bool IsIcon;public int HotspotX,HotspotY;public IntPtr Mask,Color; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int width,int height,uint step,IntPtr brush,uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    private static void DrawCursor(Graphics graphics,Rectangle bounds,int width,int height)
    {
        var cursor=new CursorInfo{Size=Marshal.SizeOf<CursorInfo>()};
        if(!GetCursorInfo(ref cursor)||cursor.Flags!=1||cursor.Cursor==IntPtr.Zero)return;
        int hotX=0,hotY=0;
        if(GetIconInfo(cursor.Cursor,out IconInfo icon))
        {
            hotX=icon.HotspotX;hotY=icon.HotspotY;
            if(icon.Mask!=IntPtr.Zero)DeleteObject(icon.Mask);if(icon.Color!=IntPtr.Zero)DeleteObject(icon.Color);
        }
        double scaleX=width/(double)bounds.Width,scaleY=height/(double)bounds.Height;
        int x=(int)Math.Round((cursor.Position.X-bounds.Left-hotX)*scaleX);
        int y=(int)Math.Round((cursor.Position.Y-bounds.Top-hotY)*scaleY);
        int cursorSize=Math.Clamp((int)Math.Round(32*Math.Min(scaleX,scaleY)),20,48);
        IntPtr dc=graphics.GetHdc();try{DrawIconEx(dc,x,y,cursor.Cursor,cursorSize,cursorSize,0,IntPtr.Zero,3);}finally{graphics.ReleaseHdc(dc);}
    }
    private static async Task<string?> ReadHeaders(NetworkStream stream,CancellationToken token)
    {
        byte[] one=new byte[1];using var memory=new MemoryStream();int matched=0;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while(memory.Length<16384)
        {if(await stream.ReadAsync(one,timeout.Token)==0)return null;memory.WriteByte(one[0]);matched=one[0]=="\r\n\r\n"[matched]?matched+1:(one[0]=='\r'?1:0);if(matched==4)return Encoding.ASCII.GetString(memory.ToArray());}
        return null;
    }
    private static async Task WriteResponse(NetworkStream stream,string type,byte[] body,CancellationToken token)
    {byte[] head=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");await stream.WriteAsync(head,token);await stream.WriteAsync(body,token);}
    private static Task WriteStatus(NetworkStream stream,string status,CancellationToken token)=>stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),token).AsTask();
    private static string QueryValue(string target,string name)
    {int query=target.IndexOf('?');if(query<0)return "";foreach(var pair in target[(query+1)..].Split('&')){var bits=pair.Split('=',2);if(bits.Length==2&&bits[0]==name)return Uri.UnescapeDataString(bits[1]);}return "";}
    private static IEnumerable<string> FindLanAddresses()=>NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a.Address)&&!a.Address.ToString().StartsWith("169.254.")).Select(a=>a.Address.ToString()).Distinct();
    private bool IsRateLimited(string ip)=>failedPins.TryGetValue(ip,out var entry)&&DateTime.UtcNow-entry.Since<TimeSpan.FromMinutes(1)&&entry.Count>=6;
    private void RegisterFailedPin(string ip)=>failedPins.AddOrUpdate(ip,_=>(1,DateTime.UtcNow),(_,entry)=>DateTime.UtcNow-entry.Since>=TimeSpan.FromMinutes(1)?(1,DateTime.UtcNow):(entry.Count+1,entry.Since));
    private static bool IsPrivate(IPAddress address)
    {
        if(IPAddress.IsLoopback(address))return true;byte[] b=address.GetAddressBytes();
        return b.Length==4&&(b[0]==10||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168)||(b[0]==169&&b[1]==254));
    }
    private static byte[] CreateIcon(){using var image=new Bitmap(180,180);using var g=Graphics.FromImage(image);g.Clear(Color.FromArgb(10,22,40));using var brush=new SolidBrush(Color.FromArgb(88,139,255));g.FillEllipse(brush,25,48,130,84);using var white=new Pen(Color.White,10);g.DrawLine(white,58,90,88,90);g.DrawLine(white,73,75,73,105);g.DrawEllipse(white,112,77,8,8);g.DrawEllipse(white,128,96,8,8);using var ms=new MemoryStream();image.Save(ms,ImageFormat.Png);return ms.ToArray();}
    public void Dispose()=>Stop();

    private const string Manifest="""{"name":"GameLauncher Remote","short_name":"Game Remote","display":"standalone","orientation":"any","start_url":"/","background_color":"#071426","theme_color":"#0a1628","icons":[{"src":"/icon.png","sizes":"180x180","type":"image/png"}]}""";
    private const string Page="""
<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover"><meta name="apple-mobile-web-app-capable" content="yes"><meta name="apple-mobile-web-app-status-bar-style" content="black-translucent"><meta name="theme-color" content="#030914"><link rel="manifest" href="/manifest.webmanifest"><link rel="apple-touch-icon" href="/icon.png"><title>Game Remote</title><style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#02060d;color:#edf4ff;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}#screen{position:fixed;inset:0;width:100%;height:100%;object-fit:contain;touch-action:none;background:#02060d}.glass{background:#101e34e8;border:1px solid #385172;border-radius:18px;box-shadow:0 15px 50px #0008;-webkit-backdrop-filter:blur(18px);backdrop-filter:blur(18px)}#login{position:fixed;inset:0;display:grid;place-items:center;padding:25px;z-index:8;background:radial-gradient(circle at 50% 30%,#18365b,#050b15 70%)}#login>div{width:min(420px,100%);padding:26px}h1{font-size:26px;margin:0 0 8px}p{color:#aabbd2;line-height:1.45}input,textarea,button{font:inherit;color:inherit}input,textarea{width:100%;background:#081324;border:1px solid #405a7d;border-radius:13px;padding:14px;margin:8px 0}button{border:0;border-radius:13px;padding:12px 15px;background:#243a58;font-weight:650;white-space:nowrap}button.primary{width:100%;background:#578cff;margin-top:8px}#bar{position:fixed;top:max(8px,env(safe-area-inset-top));right:max(8px,env(safe-area-inset-right));z-index:5;display:flex;gap:7px;padding:7px;transition:.18s ease}#bar button{padding:10px 12px}#bar.collapsed{left:0;right:auto;top:max(5px,env(safe-area-inset-top));padding:0;background:transparent;border:0;border-radius:0;box-shadow:none;-webkit-backdrop-filter:none;backdrop-filter:none}#bar.collapsed .tool{display:none}#bar.collapsed #collapse{width:28px;min-width:28px;height:36px;padding:0;border-radius:0 12px 12px 0;background:#162a46cc;font-size:22px}#collapse{background:#578cff;min-width:44px}#status{position:fixed;left:max(8px,env(safe-area-inset-left));top:max(8px,env(safe-area-inset-top));z-index:4;padding:9px 13px;font-size:12px}#status.quiet{display:none}#keys{position:fixed;left:max(8px,env(safe-area-inset-left));right:max(8px,env(safe-area-inset-right));bottom:max(8px,env(safe-area-inset-bottom));z-index:7;padding:12px;display:none}.row{display:flex;gap:7px;overflow:auto}.row button{flex:1;min-width:53px}textarea{height:64px;resize:none}.bad{color:#ff9b9b}.ok{color:#9ff0c2}@media(max-width:600px){#bar .label{display:none}#status{max-width:48vw;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}}
</style></head><body><canvas id="screen" width="1280" height="720" aria-label="PC-Bildschirm"></canvas><div id="status" class="glass">Nicht verbunden</div><div id="bar" class="glass"><button id="collapse" title="Menü ein-/ausklappen">☰</button><button class="tool" id="mode"><span class="label">Direkt</span> ◉</button><button class="tool" id="drag"><span class="label">Ziehen</span> ↔</button><button class="tool" id="keyboard">⌨︎</button><button class="tool" id="full">⛶</button></div><div id="keys" class="glass"><div class="row"><button data-key="escape">Esc</button><button data-key="space">Leertaste</button><button data-key="enter">Enter</button><button data-key="left">←</button><button data-key="up">↑</button><button data-key="down">↓</button><button data-key="right">→</button><button data-key="f11">F11</button></div><textarea id="text" placeholder="Text hier eingeben …"></textarea><div class="row"><button id="send">Text senden</button><button id="closeKeys">Schließen</button></div></div><div id="login"><div class="glass"><h1>Game Remote</h1><p>Gib die sechsstellige PIN ein, die im GameLauncher auf deinem PC angezeigt wird.</p><input id="pin" inputmode="numeric" pattern="[0-9]*" maxlength="6" placeholder="000000"><button id="connect" class="primary">Verbinden</button><p id="error"></p><p>Nur im gleichen WLAN verwenden. In Safari kannst du diese Seite über „Zum Home-Bildschirm“ wie eine App installieren.</p></div></div><script>
const screen=document.querySelector('#screen'),ctx=screen.getContext('2d',{alpha:false}),login=document.querySelector('#login'),statusEl=document.querySelector('#status'),pin=document.querySelector('#pin'),bar=document.querySelector('#bar');let ws,mode='direct',drag=false,down=null,last=null,retry=0,rendering=false,pending=null,frames=0,fpsAt=performance.now();const send=o=>ws?.readyState===1&&ws.send(JSON.stringify(o));
async function drawFrame(blob){let bitmap;try{bitmap=await createImageBitmap(blob);if(screen.width!==bitmap.width||screen.height!==bitmap.height){screen.width=bitmap.width;screen.height=bitmap.height}ctx.drawImage(bitmap,0,0);bitmap.close()}catch{await new Promise(resolve=>{const image=new Image(),url=URL.createObjectURL(blob);image.onload=()=>{if(screen.width!==image.naturalWidth||screen.height!==image.naturalHeight){screen.width=image.naturalWidth;screen.height=image.naturalHeight}ctx.drawImage(image,0,0);URL.revokeObjectURL(url);resolve()};image.onerror=()=>{URL.revokeObjectURL(url);resolve()};image.src=url})}}
async function renderLatest(){if(rendering)return;rendering=true;while(pending){const blob=pending;pending=null;await drawFrame(blob);send({type:'frameAck'});frames++;const now=performance.now();if(now-fpsAt>=1000){const actual=Math.round(frames*1000/(now-fpsAt));statusEl.textContent=`Verbunden · ${actual} FPS · ${screen.width}×${screen.height}`;frames=0;fpsAt=now}}rendering=false}
function connect(){if(pin.value.length!==6)return;localStorage.pin=pin.value;statusEl.textContent='Verbinden …';ws=new WebSocket(`${location.protocol==='https:'?'wss':'ws'}://${location.host}/control?pin=${encodeURIComponent(pin.value)}`);ws.binaryType='blob';ws.onopen=()=>{login.style.display='none';statusEl.textContent='Verbunden · Stream startet';statusEl.className='glass ok';retry=0;fpsAt=performance.now();frames=0};ws.onmessage=e=>{if(typeof e.data==='string')return;pending=e.data;renderLatest()};ws.onclose=()=>{statusEl.textContent='Verbindung getrennt';statusEl.className='glass bad';pending=null;if(retry++<8)setTimeout(connect,Math.min(8000,700*retry))};ws.onerror=()=>document.querySelector('#error').textContent='PIN falsch, Host aus oder nicht im gleichen WLAN.'}
document.querySelector('#connect').onclick=connect;if(localStorage.pin){pin.value=localStorage.pin;connect()}const collapseButton=document.querySelector('#collapse'),keys=document.querySelector('#keys'),savedCompact=localStorage.remoteCompact==='1';bar.classList.toggle('collapsed',savedCompact);statusEl.classList.toggle('quiet',savedCompact);collapseButton.textContent=savedCompact?'›':'×';collapseButton.onclick=e=>{const compact=bar.classList.toggle('collapsed');e.currentTarget.textContent=compact?'›':'×';localStorage.remoteCompact=compact?'1':'0';statusEl.classList.toggle('quiet',compact);if(compact)keys.style.display='none'};document.querySelector('#mode').onclick=e=>{mode=mode==='direct'?'trackpad':'direct';e.currentTarget.querySelector('.label').textContent=mode==='direct'?'Direkt':'Trackpad'};document.querySelector('#drag').onclick=e=>{drag=!drag;e.currentTarget.style.background=drag?'#578cff':'#243a58';if(!drag)send({type:'up',button:'left'})};document.querySelector('#keyboard').onclick=()=>{keys.style.display='block';document.querySelector('#text').focus()};document.querySelector('#closeKeys').onclick=()=>keys.style.display='none';document.querySelector('#send').onclick=()=>{const t=document.querySelector('#text');send({type:'text',text:t.value});t.value=''};document.querySelectorAll('[data-key]').forEach(b=>b.onclick=()=>send({type:'key',key:b.dataset.key}));document.querySelector('#full').onclick=()=>document.documentElement.requestFullscreen?.();
function pos(e){const r=screen.getBoundingClientRect(),iw=screen.width||r.width,ih=screen.height||r.height,s=Math.min(r.width/iw,r.height/ih),w=iw*s,h=ih*s,left=r.left+(r.width-w)/2,top=r.top+(r.height-h)/2;return{x:Math.max(0,Math.min(1,(e.clientX-left)/w)),y:Math.max(0,Math.min(1,(e.clientY-top)/h))}}screen.oncontextmenu=e=>e.preventDefault();screen.onpointerdown=e=>{screen.setPointerCapture(e.pointerId);const p=pos(e);down={...p,x0:e.clientX,y0:e.clientY,time:Date.now()};last={x:e.clientX,y:e.clientY};if(drag){send({type:'move',x:p.x,y:p.y});send({type:'down',button:'left'})}};screen.onpointermove=e=>{if(!down)return;const p=pos(e);if(drag)send({type:'move',x:p.x,y:p.y});else if(mode==='trackpad'){send({type:'delta',dx:(e.clientX-last.x)*1.45,dy:(e.clientY-last.y)*1.45});last={x:e.clientX,y:e.clientY}}};screen.onpointerup=e=>{if(!down)return;const p=pos(e),dx=e.clientX-down.x0,dy=e.clientY-down.y0,dist=Math.hypot(dx,dy),held=Date.now()-down.time;if(drag)send({type:'up',button:'left'});else if(held>650&&dist<18){send({type:'move',x:p.x,y:p.y});send({type:'click',button:'right'});navigator.vibrate?.(25)}else if(dist<14){if(mode==='direct')send({type:'move',x:p.x,y:p.y});send({type:'click',button:'left'});navigator.vibrate?.(12)}else if(mode==='direct')send({type:'scroll',amount:-dy*3});down=null};screen.onpointercancel=()=>{if(drag)send({type:'up',button:'left'});down=null};screen.onwheel=e=>{e.preventDefault();send({type:'scroll',amount:-e.deltaY})};
</script></body></html>
""";
}
