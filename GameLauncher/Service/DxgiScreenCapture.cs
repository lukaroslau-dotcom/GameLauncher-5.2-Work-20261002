using SharpGen.Runtime;
using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace GameLauncher.Service;

/// <summary>
/// GPU-backed primary-monitor capture. Desktop Duplication keeps working for
/// DirectX/OpenGL full-screen surfaces where GDI CopyFromScreen can be black.
/// </summary>
internal sealed class DxgiScreenCapture : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1, FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1, FeatureLevel.Level_10_0
    ];

    private IDXGIFactory1? factory;
    private IDXGIAdapter1? adapter;
    private IDXGIOutput? output;
    private IDXGIOutput1? output1;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private ID3D11Texture2D? staging;
    private Bitmap? desktop;
    private bool hasFrame;

    public Rectangle Bounds { get; private set; }

    public DxgiScreenCapture() => Initialize();

    private void Initialize()
    {
        DisposeResources();
        factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out adapter).CheckError();
        if (adapter == null) throw new InvalidOperationException("Kein Grafikadapter gefunden.");
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            FeatureLevels, out device).CheckError();
        context = device!.ImmediateContext;
        adapter.EnumOutputs(0, out output).CheckError();
        if (output == null) throw new InvalidOperationException("Kein Bildschirm gefunden.");
        output1 = output.QueryInterface<IDXGIOutput1>();
        duplication = output1.DuplicateOutput(device);

        var rect = output.Description.DesktopCoordinates;
        Bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            Bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        staging = device.CreateTexture2D(new Texture2DDescription
        {
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = (uint)Bounds.Width,
            Height = (uint)Bounds.Height,
            MiscFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        });
        desktop = new Bitmap(Bounds.Width, Bounds.Height, PixelFormat.Format32bppArgb);
        hasFrame = false;
    }

    public Bitmap Capture(int timeoutMilliseconds)
    {
        if (duplication == null || context == null || staging == null || desktop == null)
            Initialize();

        IDXGIResource? resource = null;
        bool acquired = false;
        try
        {
            uint timeout=(uint)(hasFrame?Math.Clamp(timeoutMilliseconds,0,1000):250);
            Result result = duplication!.AcquireNextFrame(timeout,
                out OutduplFrameInfo frameInfo, out resource);
            if (result.Failure)
            {
                if (result.Code == unchecked((int)0x887A0027) && hasFrame) return desktop!; // DXGI_ERROR_WAIT_TIMEOUT
                result.CheckError();
            }

            acquired = true;
            if (resource != null && (frameInfo.LastPresentTime != 0 || !hasFrame))
            {
                using ID3D11Texture2D source = resource.QueryInterface<ID3D11Texture2D>();
                context!.CopyResource(staging!, source);
                MappedSubresource mapped = context.Map(staging!, 0, MapMode.Read, MapFlags.None);
                try { CopyToBitmap(mapped, desktop!); }
                finally { context.Unmap(staging!, 0); }
                hasFrame = true;
            }
        }
        finally
        {
            resource?.Dispose();
            if (acquired) try { duplication?.ReleaseFrame(); } catch { }
        }

        if (!hasFrame)
            throw new InvalidOperationException("DXGI hat noch kein Bildschirmbild geliefert.");
        return desktop!;
    }

    private static unsafe void CopyToBitmap(MappedSubresource source, Bitmap destination)
    {
        var area = new Rectangle(0, 0, destination.Width, destination.Height);
        BitmapData target = destination.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = destination.Width * 4;
            for (int y = 0; y < destination.Height; y++)
            {
                byte* from = (byte*)source.DataPointer + y * source.RowPitch;
                byte* to = (byte*)target.Scan0 + y * target.Stride;
                Buffer.MemoryCopy(from, to, Math.Abs(target.Stride), rowBytes);
            }
        }
        finally { destination.UnlockBits(target); }
    }

    public void Reset() => Initialize();

    private void DisposeResources()
    {
        desktop?.Dispose();
        staging?.Dispose();
        duplication?.Dispose();
        output1?.Dispose();
        output?.Dispose();
        context?.Dispose();
        device?.Dispose();
        adapter?.Dispose();
        factory?.Dispose();
        desktop = null; staging = null; duplication = null; output1 = null;
        output = null; context = null; device = null; adapter = null; factory = null;
    }

    public void Dispose() => DisposeResources();
}
