using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GameLauncher.Service;

internal static class RemoteInput
{
    private const uint InputMouse=0,InputKeyboard=1;
    private const uint MouseLeftDown=0x0002,MouseLeftUp=0x0004,MouseRightDown=0x0008,MouseRightUp=0x0010,MouseWheel=0x0800;
    private const uint KeyUp=0x0002,KeyUnicode=0x0004;

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public nint extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort virtualKey,scan; public uint flags,time; public nint extra; }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }

    public static void MoveTo(double x,double y)
    {
        var bounds=Screen.PrimaryScreen?.Bounds??new System.Drawing.Rectangle(0,0,1920,1080);
        SetCursorPos(bounds.Left+(int)(Math.Clamp(x,0,1)*(bounds.Width-1)),bounds.Top+(int)(Math.Clamp(y,0,1)*(bounds.Height-1)));
    }
    public static void MoveBy(double dx,double dy)
    {
        if(GetCursorPos(out var point))SetCursorPos(point.X+(int)Math.Clamp(dx,-300,300),point.Y+(int)Math.Clamp(dy,-300,300));
    }
    public static void Click(string button)
    {
        uint down=button.Equals("right",StringComparison.OrdinalIgnoreCase)?MouseRightDown:MouseLeftDown;
        uint up=button.Equals("right",StringComparison.OrdinalIgnoreCase)?MouseRightUp:MouseLeftUp;
        SendMouse(down);SendMouse(up);
    }
    public static void Button(string button,bool down)=>SendMouse(button.Equals("right",StringComparison.OrdinalIgnoreCase)?(down?MouseRightDown:MouseRightUp):(down?MouseLeftDown:MouseLeftUp));
    public static void Scroll(double amount)=>SendMouse(MouseWheel,unchecked((uint)(int)Math.Clamp(amount,-1200,1200)));
    private static void SendMouse(uint flags,uint data=0)=>SendInput(1,new[]{new INPUT{type=InputMouse,data=new InputUnion{mi=new MOUSEINPUT{dwFlags=flags,mouseData=data}}}},Marshal.SizeOf<INPUT>());

    public static void Text(string text)
    {
        foreach(char ch in text.Take(500))
        {
            var down=new INPUT{type=InputKeyboard,data=new InputUnion{ki=new KEYBDINPUT{scan=ch,flags=KeyUnicode}}};
            var up=down;up.data.ki.flags=KeyUnicode|KeyUp;
            SendInput(2,new[]{down,up},Marshal.SizeOf<INPUT>());
        }
    }
    public static void Key(string key)
    {
        ushort vk=key.ToLowerInvariant() switch
        {
            "enter"=>0x0D,"escape"=>0x1B,"space"=>0x20,"backspace"=>0x08,"tab"=>0x09,
            "left"=>0x25,"up"=>0x26,"right"=>0x27,"down"=>0x28,"f11"=>0x7A,
            _=>0
        };
        if(vk==0)return;
        var down=new INPUT{type=InputKeyboard,data=new InputUnion{ki=new KEYBDINPUT{virtualKey=vk}}};
        var up=down;up.data.ki.flags=KeyUp;
        SendInput(2,new[]{down,up},Marshal.SizeOf<INPUT>());
    }
}
