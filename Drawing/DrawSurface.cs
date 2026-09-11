using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System.IO;

namespace PenDynamicsLab.Drawing;

/// <summary>
/// Bundles an SKBitmap, SKCanvas, and Avalonia WriteableBitmap that mirrors it.
/// Multiple Image hosts can share the same bitmap — useful when the same canvas
/// content needs to appear in more than one place (e.g., across tabs).
/// </summary>
/// <remarks>
/// <para>
/// The backing store is allocated in <em>physical pixels</em> while callers draw in
/// <em>device-independent units</em> (DIPs). <see cref="EnsureSize"/> takes the host's
/// DIP bounds plus the render scaling and does three things to bridge that gap:
/// </para>
/// <list type="number">
///   <item>allocates the SKBitmap at <c>dip * scale</c> pixels,</item>
///   <item>assigns each host Image an explicit <see cref="DipWidth"/> / <see cref="DipHeight"/>,
///   which together with <c>Stretch="Fill"</c> maps the backing store onto the screen 1:1,</item>
///   <item>applies a <c>scale</c> transform to the SKCanvas so drawing code stays in
///   DIP space.</item>
/// </list>
/// <para>
/// The explicit DIP size on the host is what makes the mapping 1:1, and the bitmap stays
/// tagged at 96 DPI so that <c>Bitmap.Size</c> keeps reporting pixels. Tagging the real
/// density instead makes Avalonia sample only the top-left <c>pixels / scale</c> corner
/// and stretch it over the whole host, which puts strokes <c>scale</c> times too far from
/// the origin — the symptom that motivated all of this.
/// </para>
/// <para>
/// Without this the bitmap would hold one pixel per DIP and the compositor would
/// magnify it by the scaling factor — visibly blocky strokes on a HiDPI display, and
/// sub-pixel pen precision quantized away to whole DIPs.
/// </para>
/// </remarks>
public sealed class DrawSurface : IDisposable
{
    private static readonly SKColor ClearColor = new(0xF7, 0xF7, 0xF4);

    private readonly List<Image> _hosts = new();
    private SKBitmap? _skBitmap;
    private SKCanvas? _skCanvas;
    private WriteableBitmap? _avBitmap;

    /// <summary>Backing store width in physical pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Backing store height in physical pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Physical pixels per DIP. The canvas carries a matching scale transform.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Backing store width in DIPs (<see cref="Width"/> / <see cref="Scale"/>).</summary>
    public double DipWidth { get; private set; }

    /// <summary>Backing store height in DIPs (<see cref="Height"/> / <see cref="Scale"/>).</summary>
    public double DipHeight { get; private set; }

    /// <summary>Drawing surface. Its transform maps DIP coordinates onto physical pixels.</summary>
    public SKCanvas? Canvas => _skCanvas;

    public DrawSurface(Image host)
    {
        AddHost(host);
    }

    public DrawSurface() { }

    /// <summary>Register an Image control that should display this surface's bitmap.</summary>
    public void AddHost(Image host)
    {
        if (_hosts.Contains(host)) return;
        _hosts.Add(host);
        if (_avBitmap != null) ApplyToHost(host);
    }

    /// <summary>
    /// Point a host at the current bitmap and size it in DIPs. The explicit size is what
    /// makes Stretch="Fill" resolve to a 1:1 pixel mapping instead of a magnification.
    /// </summary>
    private void ApplyToHost(Image host)
    {
        host.Source = _avBitmap;
        host.Width = DipWidth;
        host.Height = DipHeight;
    }

    /// <summary>
    /// Recreate the bitmap if the requested DIP size or render scaling changed;
    /// preserve existing pixels.
    /// </summary>
    /// <param name="dipWidth">Host width in device-independent units.</param>
    /// <param name="dipHeight">Host height in device-independent units.</param>
    /// <param name="scale">Physical pixels per DIP (the TopLevel's RenderScaling).</param>
    public void EnsureSize(double dipWidth, double dipHeight, double scale)
    {
        if (dipWidth <= 0 || dipHeight <= 0) return;
        if (scale <= 0 || double.IsNaN(scale)) scale = 1;

        int w = (int)Math.Round(dipWidth * scale);
        int h = (int)Math.Round(dipHeight * scale);
        if (w <= 0 || h <= 0) return;

        // Never shrink. The preserve-pixels blit below copies the old bitmap in at the origin,
        // so allocating smaller discards whatever falls outside — permanently, since growing
        // back cannot recover it. Shrinking the window and restoring it used to truncate the
        // mark at the smaller height, and a resize drag does that on every step.
        //
        // Growing instead of shrinking also makes the shared processed surface safe: the Stroke
        // tab's canvas and the Compare tab's are different sizes, and the hosts already clip an
        // oversized bitmap (see StrokeCanvasView — the Image is pinned at 0,0 on a Canvas inside
        // a ClipToBounds border), so a bitmap larger than its host displays correctly.
        //
        // Growth is monotonic within a session: maximise then restore and the bitmap stays at the
        // maximum. Bounded by screen size, so this is a stated consequence rather than a leak.
        //
        // Only when the scale is unchanged. A DPI change has to reallocate either way, and its
        // resample branch scales the old content by the ratio so it keeps its apparent size —
        // a smaller pixel count there is correct, not a loss.
        if (_skBitmap != null && Scale == scale)
        {
            w = Math.Max(w, Width);
            h = Math.Max(h, Height);
        }

        if (_skBitmap != null && Width == w && Height == h && Scale == scale) return;

        var oldBitmap = _skBitmap;
        var oldCanvas = _skCanvas;
        double oldScale = Scale;

        _skBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        _skCanvas = new SKCanvas(_skBitmap);
        Width = w;
        Height = h;
        Scale = scale;
        // Derive the DIP size from the rounded pixel count rather than the requested
        // bounds, so DipWidth * scale is exactly Width and the mapping stays 1:1.
        DipWidth = w / scale;
        DipHeight = h / scale;

        _skCanvas.Clear(ClearColor);

        // Preserve existing pixels. This blit runs before the DIP transform is applied
        // below, so it works in device space. If the scaling itself changed (the window
        // moved to a monitor with different DPI) the old content is resampled by the
        // ratio so it keeps its apparent size rather than jumping.
        if (oldBitmap != null)
        {
            if (scale != oldScale && oldScale > 0)
            {
                _skCanvas.Save();
                _skCanvas.Scale((float)(scale / oldScale));
                _skCanvas.DrawBitmap(oldBitmap, 0, 0);
                _skCanvas.Restore();
            }
            else
            {
                _skCanvas.DrawBitmap(oldBitmap, 0, 0);
            }
            oldCanvas?.Dispose();
            oldBitmap.Dispose();
        }

        // From here on callers draw in DIPs; this maps them onto the physical pixels.
        _skCanvas.Scale((float)scale);

        // 96 DPI is deliberate, NOT an oversight. Tagging the true density (96 * scale)
        // makes Bitmap.Size report DIPs instead of pixels, and Avalonia derives the
        // source rectangle from Bitmap.Size while treating it as pixels — so only the
        // top-left (pixels / scale) corner of the bitmap gets sampled and stretched over
        // the whole host. Leaving it at 96 keeps Bitmap.Size == PixelSize, so the full
        // bitmap is the source; the DIP size is carried by the hosts' explicit
        // Width/Height instead. See ApplyToHost.
        // Dispose the outgoing bitmap rather than letting the GC find it. The managed
        // wrapper is tiny and the backing store is unmanaged, so the collector sees
        // almost no pressure while the real footprint is whatever w * h * 4 was — and
        // a resize drag reallocates on every mouse move.
        var oldAvBitmap = _avBitmap;

        _avBitmap = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888,
            global::Avalonia.Platform.AlphaFormat.Premul);

        // Hosts still point at the old bitmap until ApplyToHost runs below, so the
        // dispose has to wait until after they have been repointed.
        CopyToAvBitmap();
        foreach (var host in _hosts) ApplyToHost(host);
        oldAvBitmap?.Dispose();
    }

    public void Clear()
    {
        _skCanvas?.Clear(ClearColor);
        Present();
    }

    /// <summary>Push the SKBitmap pixels into the Avalonia bitmap and invalidate every host.</summary>
    public void Present()
    {
        CopyToAvBitmap();
        foreach (var host in _hosts) host.InvalidateVisual();
    }

    /// <summary>
    /// Push the SKBitmap pixels into the Avalonia bitmap, row by row.
    /// </summary>
    /// <remarks>
    /// Neither side promises a tightly packed buffer: <see cref="SKBitmap.RowBytes"/> and
    /// <c>ILockedFramebuffer.RowBytes</c> may both pad each row out to an alignment. Copying
    /// the whole image as one block assumes <c>stride == width * 4</c> on both sides, and when
    /// that is wrong the result is not noise but a shear — row <c>N</c> lands at
    /// <c>N * width * 4</c> instead of <c>N * stride</c>, so each row sits a little further
    /// left than the one above and the image wraps into a parallelogram.
    /// </remarks>
    private void CopyToAvBitmap()
    {
        if (_skBitmap == null || _avBitmap == null) return;
        using var fb = _avBitmap.Lock();

        int srcStride = _skBitmap.RowBytes;
        int dstStride = fb.RowBytes;
        int rowBytes = Width * 4;

        unsafe
        {
            byte* src = (byte*)_skBitmap.GetPixels();
            byte* dst = (byte*)fb.Address;
            if (src == null || dst == null) return;

            CopyRows(
                new ReadOnlySpan<byte>(src, srcStride * Height), srcStride,
                new Span<byte>(dst, dstStride * Height), dstStride,
                rowBytes, Height);
        }
    }

    /// <summary>
    /// Copy <paramref name="height"/> rows of <paramref name="rowBytes"/> bytes, honouring
    /// each side's stride. Pulled out of <see cref="CopyToAvBitmap"/> so the padded case can
    /// be tested without a real framebuffer — no platform seen here produces a padded stride,
    /// so left inline it would be unreachable in tests and unverifiable.
    /// </summary>
    internal static void CopyRows(
        ReadOnlySpan<byte> src, int srcStride,
        Span<byte> dst, int dstStride,
        int rowBytes, int height)
    {
        // Both sides packed: the rows are already contiguous, so copy the lot in one go.
        if (srcStride == rowBytes && dstStride == rowBytes)
        {
            src[..(rowBytes * height)].CopyTo(dst);
            return;
        }

        for (int y = 0; y < height; y++)
        {
            src.Slice(y * srcStride, rowBytes)
               .CopyTo(dst.Slice(y * dstStride, rowBytes));
        }
    }

    /// <summary>Encode the current bitmap to PNG and write to <paramref name="stream"/>.</summary>
    public void SavePng(Stream stream)
    {
        if (_skBitmap == null) return;
        using var image = SKImage.FromBitmap(_skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }

    public void Dispose()
    {
        // Repoint the hosts before disposing what they are showing, so nothing can
        // render from a freed buffer if a frame is still in flight.
        foreach (var host in _hosts) host.Source = null;

        _skCanvas?.Dispose();
        _skBitmap?.Dispose();
        _avBitmap?.Dispose();
        _skCanvas = null;
        _skBitmap = null;
        _avBitmap = null;
    }
}
