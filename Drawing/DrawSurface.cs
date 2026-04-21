using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace PenDynamicsLab.Drawing;

/// <summary>
/// Bundles an SKBitmap, SKCanvas, and Avalonia WriteableBitmap that mirrors it,
/// hosted on a single Image control. Resizes follow the host control's bounds.
/// </summary>
public sealed class DrawSurface : IDisposable
{
    private static readonly SKColor ClearColor = new(0xF5, 0xF5, 0xF0);

    private readonly Image _host;
    private SKBitmap? _skBitmap;
    private SKCanvas? _skCanvas;
    private WriteableBitmap? _avBitmap;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public SKCanvas? Canvas => _skCanvas;

    public DrawSurface(Image host)
    {
        _host = host;
    }

    /// <summary>Recreate the bitmap if the host's bounds changed; preserve existing pixels on grow/shrink.</summary>
    public void EnsureSize(int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        if (_skBitmap != null && Width == w && Height == h) return;

        var oldBitmap = _skBitmap;
        var oldCanvas = _skCanvas;

        _skBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        _skCanvas = new SKCanvas(_skBitmap);
        Width = w;
        Height = h;

        _skCanvas.Clear(ClearColor);

        if (oldBitmap != null)
        {
            _skCanvas.DrawBitmap(oldBitmap, 0, 0);
            oldCanvas?.Dispose();
            oldBitmap.Dispose();
        }

        _avBitmap = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888,
            global::Avalonia.Platform.AlphaFormat.Premul);

        CopyToAvBitmap();
        _host.Source = _avBitmap;
    }

    public void Clear()
    {
        _skCanvas?.Clear(ClearColor);
        Present();
    }

    /// <summary>Push the SKBitmap pixels into the Avalonia bitmap and invalidate the host.</summary>
    public void Present()
    {
        CopyToAvBitmap();
        _host.InvalidateVisual();
    }

    private void CopyToAvBitmap()
    {
        if (_skBitmap == null || _avBitmap == null) return;
        using var fb = _avBitmap.Lock();
        unsafe
        {
            var src = _skBitmap.GetPixels();
            var dst = fb.Address;
            int bytes = Width * Height * 4;
            Buffer.MemoryCopy((void*)src, (void*)dst, bytes, bytes);
        }
    }

    public void Dispose()
    {
        _skCanvas?.Dispose();
        _skBitmap?.Dispose();
        _skCanvas = null;
        _skBitmap = null;
    }
}
