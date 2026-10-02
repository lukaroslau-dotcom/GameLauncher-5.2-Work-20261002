using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Drawing.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImage = System.Drawing.Image;
namespace GameLauncher.Service;
public sealed record AnimatedImage(IReadOnlyList<BitmapSource> Frames,IReadOnlyList<TimeSpan> Delays);
/// <summary>Bounded decoded-image cache. Decoding never runs on the UI thread.</summary>
public static class ImageCache
{
    private static readonly ConcurrentDictionary<string,Task<BitmapSource?>> cache = new();
    private static readonly ConcurrentDictionary<string,Task<AnimatedImage?>> animations = new();
    private static readonly SemaphoreSlim workers = new(2);
    public static Task<BitmapSource?> Load(string? path,int width=640)
    {
        if(string.IsNullOrWhiteSpace(path)) return Task.FromResult<BitmapSource?>(null);
        return cache.GetOrAdd(path+"|"+width,keyName=>Task.Run(async()=>
        {
            await workers.WaitAsync();
            try
            {
                if(!File.Exists(path)) return null;
                using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.StreamSource=file;bitmap.DecodePixelWidth=width;bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();return (BitmapSource)bitmap;
            }
            catch {return null;}
            finally { workers.Release(); if(cache.Count>80) foreach(var key in cache.Where(p=>p.Value.IsCompleted).Take(cache.Count-64).Select(p=>p.Key)) cache.TryRemove(key,out _); }
        }));
    }
    public static void Invalidate(string path)
    {
        foreach(var key in cache.Keys.Where(k=>k.StartsWith(path+"|",StringComparison.OrdinalIgnoreCase)))cache.TryRemove(key,out _);
        foreach(var key in animations.Keys.Where(k=>k.StartsWith(path+"|",StringComparison.OrdinalIgnoreCase)))animations.TryRemove(key,out _);
    }

    public static Task<AnimatedImage?> LoadGif(string path,int requestedWidth)
    {
        int width=Math.Clamp(requestedWidth,120,420);
        return animations.GetOrAdd(path+"|"+width,cacheKey=>StartGifDecode(path,width));
    }

    public static bool IsGif(string path)
    {
        if(Path.GetExtension(path).Equals(".gif",StringComparison.OrdinalIgnoreCase))return true;
        try
        {
            Span<byte> header=stackalloc byte[6];
            using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            return file.Read(header)==6&&(header.SequenceEqual("GIF87a"u8)||header.SequenceEqual("GIF89a"u8));
        }
        catch{return false;}
    }

    private static Task<AnimatedImage?> StartGifDecode(string path,int width)
    {
        var completion=new TaskCompletionSource<AnimatedImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            workers.Wait();
            try
            {
                if(!File.Exists(path)||new FileInfo(path).Length>10*1024*1024){completion.TrySetResult(null);return;}
                using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                using var gif=DrawingImage.FromStream(file,true,true);
                var dimension=new FrameDimension(gif.FrameDimensionsList[0]);
                int frameCount=gif.GetFrameCount(dimension);
                if(frameCount is <2 or >300){completion.TrySetResult(null);return;}
                double scale=Math.Min(1d,(double)width/gif.Width);
                int targetWidth=Math.Max(1,(int)Math.Round(gif.Width*scale));
                int targetHeight=Math.Max(1,(int)Math.Round(gif.Height*scale));
                int step=Math.Max(1,(int)Math.Ceiling(frameCount/120d));
                int outputCount=(int)Math.Ceiling(frameCount/(double)step);
                if((long)targetWidth*targetHeight*outputCount>35_000_000){completion.TrySetResult(null);return;}
                byte[]? rawDelays=null;
                try{rawDelays=gif.GetPropertyItem(0x5100)?.Value;}catch{}
                var frames=new List<BitmapSource>(outputCount);
                var delays=new List<TimeSpan>(outputCount);
                for(int index=0;index<frameCount;index+=step)
                {
                    gif.SelectActiveFrame(dimension,index);
                    using var flattened=new DrawingBitmap(targetWidth,targetHeight,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using(var graphics=System.Drawing.Graphics.FromImage(flattened))
                    {
                        graphics.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.CompositingQuality=System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                        graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        graphics.DrawImage(gif,0,0,targetWidth,targetHeight);
                    }
                    var area=new System.Drawing.Rectangle(0,0,targetWidth,targetHeight);
                    var data=flattened.LockBits(area,ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    try
                    {
                        int stride=Math.Abs(data.Stride);
                        var frame=BitmapSource.Create(targetWidth,targetHeight,96,96,PixelFormats.Pbgra32,null,data.Scan0,stride*targetHeight,stride);
                        frame.Freeze();frames.Add(frame);
                    }
                    finally{flattened.UnlockBits(data);}
                    int delay=0;
                    for(int offset=0;offset<step&&index+offset<frameCount;offset++)
                    {
                        int position=(index+offset)*4;
                        delay+=rawDelays!=null&&position+4<=rawDelays.Length?Math.Max(2,BitConverter.ToInt32(rawDelays,position)):10;
                    }
                    delays.Add(TimeSpan.FromMilliseconds(Math.Clamp(delay*10,30,1000)));
                }
                completion.TrySetResult(new AnimatedImage(frames,delays));
            }
            catch(Exception ex){System.Diagnostics.Debug.WriteLine("GIF decode failed: "+ex);completion.TrySetResult(null);}
            finally
            {
                workers.Release();
                if(animations.Count>8)foreach(var key in animations.Where(item=>item.Value.IsCompleted).Take(animations.Count-6).Select(item=>item.Key))animations.TryRemove(key,out _);
            }
        }){IsBackground=true,Name="GameLauncher GIF decoder"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        return completion.Task;
    }
}
