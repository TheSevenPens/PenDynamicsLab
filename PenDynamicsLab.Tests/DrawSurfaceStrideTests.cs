using PenDynamicsLab.Drawing;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins <see cref="DrawSurface.CopyRows"/>, the blit from the Skia bitmap into the Avalonia
/// framebuffer.
/// </summary>
/// <remarks>
/// The bug these pin: the original copied <c>Width * Height * 4</c> bytes as one block, which
/// is only correct when both sides are tightly packed. <c>ILockedFramebuffer.RowBytes</c> may
/// pad each row to an alignment, and when it does, a contiguous copy puts row N at
/// <c>N * width * 4</c> instead of <c>N * stride</c> — every row a little further left than the
/// one above, shearing the image into a parallelogram.
///
/// No platform this has run on produces a padded stride, so the padded branch cannot be
/// reached through a real <c>WriteableBitmap</c>. Passing strides explicitly is the only way
/// to exercise it, which is why the copy is a separate method rather than inline.
/// </remarks>
public class DrawSurfaceStrideTests
{
    /// <summary>Fill a buffer with a recognisable value per pixel: row * 100 + column.</summary>
    private static byte[] MakeSource(int width, int height, int stride)
    {
        var src = new byte[stride * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                for (int b = 0; b < 4; b++)
                    src[y * stride + x * 4 + b] = (byte)(y * 100 + x);
        return src;
    }

    private static void AssertPixelsLandedCorrectly(byte[] dst, int width, int height, int dstStride)
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                Assert.Equal((byte)(y * 100 + x), dst[y * dstStride + x * 4]);
    }

    [Theory]
    // Both packed — the fast path, and the only shape seen in practice.
    [InlineData(4, 3, 16, 16)]
    // Destination padded: the real-world failure, an aligned framebuffer.
    [InlineData(4, 3, 16, 32)]
    // Source padded: SKBitmap.RowBytes is not contractually width * 4 either.
    [InlineData(4, 3, 24, 16)]
    // Both padded, by different amounts.
    [InlineData(4, 3, 24, 32)]
    // A single row cannot shear, but it should still respect the strides.
    [InlineData(4, 1, 32, 48)]
    public void EveryPixelLandsAtItsOwnCoordinate(int width, int height, int srcStride, int dstStride)
    {
        int rowBytes = width * 4;
        var src = MakeSource(width, height, srcStride);
        var dst = new byte[dstStride * height];

        DrawSurface.CopyRows(src, srcStride, dst, dstStride, rowBytes, height);

        AssertPixelsLandedCorrectly(dst, width, height, dstStride);
    }

    [Fact]
    public void PaddingBytesAreLeftAlone()
    {
        // Anything past the visible row belongs to the framebuffer, not to us. Writing into
        // it would be the overrun the one-block copy was wrongly suspected of.
        const int width = 4, height = 3, srcStride = 16, dstStride = 32;
        int rowBytes = width * 4;

        var src = MakeSource(width, height, srcStride);
        var dst = new byte[dstStride * height];
        for (int i = 0; i < dst.Length; i++) dst[i] = 0xEE;

        DrawSurface.CopyRows(src, srcStride, dst, dstStride, rowBytes, height);

        for (int y = 0; y < height; y++)
            for (int i = rowBytes; i < dstStride; i++)
                Assert.Equal(0xEE, dst[y * dstStride + i]);
    }

    [Fact]
    public void PackedAndStridedCopiesAgree()
    {
        // The fast path is an optimisation, not a different behaviour. Same pixels in,
        // same pixels out — otherwise the branch itself becomes a source of bugs.
        const int width = 5, height = 4;
        int rowBytes = width * 4;

        var packedSrc = MakeSource(width, height, rowBytes);
        var packedDst = new byte[rowBytes * height];
        DrawSurface.CopyRows(packedSrc, rowBytes, packedDst, rowBytes, rowBytes, height);

        const int paddedStride = 28;
        var paddedSrc = MakeSource(width, height, paddedStride);
        var paddedDst = new byte[paddedStride * height];
        DrawSurface.CopyRows(paddedSrc, paddedStride, paddedDst, paddedStride, rowBytes, height);

        for (int y = 0; y < height; y++)
        {
            Assert.Equal(
                packedDst.AsSpan(y * rowBytes, rowBytes).ToArray(),
                paddedDst.AsSpan(y * paddedStride, rowBytes).ToArray());
        }
    }
}
