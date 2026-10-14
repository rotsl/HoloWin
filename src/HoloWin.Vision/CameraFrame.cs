using HoloWin.Gestures;

namespace HoloWin.Vision;

/// <summary>
/// Vision-specific extensions over <see cref="Frame"/>.
/// HoloWin reuses the <see cref="Frame"/> contract directly (rather than a
/// vision-specific subclass) so that downstream gesture/fusion modules can
/// treat camera frames and synthetic frames uniformly. This static class
/// provides the small set of helpers commonly needed by camera pipelines:
/// deep-clone semantics for retention, BGR reordering for OpenCV interop,
/// and zero-copy row spans for performance-critical preprocessors.
/// </summary>
public static class CameraFrame
{
    /// <summary>
    /// Creates a deep copy of the frame's RGB pixel buffer. Use this when a
    /// consumer needs to retain frame data beyond the next capture tick —
    /// <see cref="OpenCvCameraCapture"/> reuses an internal buffer and will
    /// overwrite any retained reference on the next capture cycle.
    /// </summary>
    public static Frame CloneFrame(this Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var copy = new byte[frame.RgbPixels.Length];
        Buffer.BlockCopy(frame.RgbPixels, 0, copy, 0, frame.RgbPixels.Length);
        return new Frame
        {
            Width = frame.Width,
            Height = frame.Height,
            Stride = frame.Stride,
            RgbPixels = copy,
            Timestamp = frame.Timestamp,
            SourceId = frame.SourceId,
        };
    }

    /// <summary>
    /// Returns a new byte array containing the frame's pixels reordered from
    /// RGB to BGR. Useful for interop with OpenCV-style APIs that expect BGR
    /// ordering. The original <paramref name="frame"/> is left unmodified.
    /// </summary>
    public static byte[] ToBgrPixels(this Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var src = frame.RgbPixels;
        var dst = new byte[frame.RgbPixels.Length];
        int srcStride = frame.Stride;
        int dstStride = frame.Width * 3;
        for (int y = 0; y < frame.Height; y++)
        {
            int srcRow = y * srcStride;
            int dstRow = y * dstStride;
            for (int x = 0; x < frame.Width; x++)
            {
                int si = srcRow + x * 3;
                int di = dstRow + x * 3;
                dst[di + 0] = src[si + 2]; // B
                dst[di + 1] = src[si + 1]; // G
                dst[di + 2] = src[si + 0]; // R
            }
        }
        return dst;
    }

    /// <summary>
    /// Returns a span over the RGB pixel buffer for the given row. The span
    /// is only valid for the lifetime of the underlying <paramref name="frame"/>;
    /// callers must not retain it past the next capture cycle.
    /// </summary>
    public static ReadOnlySpan<byte> RowPixels(this Frame frame, int y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if ((uint)y >= (uint)frame.Height)
            throw new ArgumentOutOfRangeException(nameof(y), y, "Row index out of range.");
        return frame.RgbPixels.AsSpan(y * frame.Stride, frame.Width * 3);
    }
}
