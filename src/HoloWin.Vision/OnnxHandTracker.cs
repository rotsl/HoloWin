using HoloWin.Gestures;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HoloWin.Vision;

/// <summary>OpenCV Zoo MediaPipe palm detector followed by an oriented hand crop.</summary>
public sealed class OnnxHandTracker : IHandTracker, IDisposable
{
    private readonly InferenceSession _palm;
    private readonly InferenceSession _hand;
    public const string PalmFile = "palm_detection_mediapipe_2023feb.onnx";
    public const string HandFile = "handpose_estimation_mediapipe_2023feb.onnx";
    public OnnxHandTracker(string? modelDirectory = null)
    {
        string root = modelDirectory ?? Path.Combine(AppContext.BaseDirectory, "models");
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        _palm = new InferenceSession(Path.Combine(root, PalmFile), options);
        try
        {
            _hand = new InferenceSession(Path.Combine(root, HandFile), options);
            ValidateInput(_palm, 192); ValidateInput(_hand, 224);
            foreach (string output in new[] { "Identity", "Identity_1", "Identity_2" })
                if (!_hand.OutputMetadata.ContainsKey(output)) throw new InvalidDataException("Unexpected hand model outputs. Use the bundled OpenCV Zoo models.");
        }
        catch { _palm.Dispose(); _hand?.Dispose(); throw; }
    }
    private static void ValidateInput(InferenceSession session, int size)
    {
        var input = session.InputMetadata.Single().Value;
        if (input.ElementType != typeof(float) || !input.Dimensions.SequenceEqual(new[] { 1, size, size, 3 }))
            throw new InvalidDataException("Unexpected camera model input. Use the bundled OpenCV Zoo models.");
    }
    public Task<IReadOnlyList<HandTrackingResult>> TrackAsync(Frame frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Stride < frame.Width * 3 || frame.RgbPixels.Length < frame.Stride * frame.Height)
            throw new ArgumentException("Invalid RGB camera frame.");
        double side = Math.Max(frame.Width, frame.Height);
        var tensor = Sample(frame, 192, frame.Width / 2.0, frame.Height / 2.0, side, 0);
        using var outputs = _palm.Run(new[] { NamedOnnxValue.CreateFromTensor(_palm.InputMetadata.Keys.Single(), tensor) });
        var boxes = outputs.Single(o => o.AsTensor<float>().Dimensions.ToArray().Last() == 18).AsTensor<float>().ToArray();
        var scores = outputs.Single(o => o.AsTensor<float>().Dimensions.ToArray().Last() == 1).AsTensor<float>().ToArray();
        var anchors = Anchors();
        if (scores.Length != anchors.Length || boxes.Length != anchors.Length * 18) throw new InvalidDataException("Unexpected palm model output dimensions.");
        var candidates = new List<Palm>();
        double padX = (side - frame.Width) / 2, padY = (side - frame.Height) / 2;
        for (int i = 0; i < anchors.Length; i++)
        {
            double confidence = 1 / (1 + Math.Exp(-Math.Clamp(scores[i], -100, 100)));
            if (confidence < 0.65) continue;
            int offset = i * 18;
            double cx = (anchors[i].X + boxes[offset] / 192) * side - padX;
            double cy = (anchors[i].Y + boxes[offset + 1] / 192) * side - padY;
            double width = boxes[offset + 2] / 192 * side, height = boxes[offset + 3] / 192 * side;
            if (width <= 0 || height <= 0) continue;
            var points = new (double X, double Y)[7];
            for (int j = 0; j < 7; j++) points[j] = ((anchors[i].X + boxes[offset + 4 + j * 2] / 192) * side - padX,
                (anchors[i].Y + boxes[offset + 5 + j * 2] / 192) * side - padY);
            candidates.Add(new Palm(cx, cy, width, height, confidence, points));
        }
        var selected = new List<Palm>();
        foreach (var candidate in candidates.OrderByDescending(p => p.Confidence))
        {
            if (selected.Any(p => Overlap(candidate, p) > 0.3)) continue;
            selected.Add(candidate); if (selected.Count == 2) break;
        }
        var result = new List<HandTrackingResult>();
        foreach (var palm in selected)
        {
            ct.ThrowIfCancellationRequested();
            double angle = Math.Atan2(palm.Points[2].Y - palm.Points[0].Y, palm.Points[2].X - palm.Points[0].X) + Math.PI / 2;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            var rotated = palm.Points.Select(p => (X: cos * p.X + sin * p.Y, Y: -sin * p.X + cos * p.Y)).ToArray();
            double minX = rotated.Min(p => p.X), maxX = rotated.Max(p => p.X), minY = rotated.Min(p => p.Y), maxY = rotated.Max(p => p.Y);
            double centerX = (minX + maxX) / 2, centerY = (minY + maxY) / 2 - (maxY - minY) * 0.4;
            double cropSide = Math.Max(maxX - minX, maxY - minY) * 3;
            if (cropSide < 8) continue;
            double originalX = cos * centerX - sin * centerY, originalY = sin * centerX + cos * centerY;
            var crop = Sample(frame, 224, originalX, originalY, cropSide, angle);
            using var handOutput = _hand.Run(new[] { NamedOnnxValue.CreateFromTensor(_hand.InputMetadata.Keys.Single(), crop) });
            float[] landmarks = handOutput.Single(o => o.Name == "Identity").AsTensor<float>().ToArray();
            double presence = handOutput.Single(o => o.Name == "Identity_1").AsTensor<float>().First();
            double handedness = handOutput.Single(o => o.Name == "Identity_2").AsTensor<float>().First();
            if (!double.IsFinite(presence) || presence < 0.8 || landmarks.Length != 63) continue;
            var points = new Landmark[21];
            for (int j = 0; j < 21; j++)
            {
                double x = (landmarks[j * 3] / 224 - 0.5) * cropSide;
                double y = (landmarks[j * 3 + 1] / 224 - 0.5) * cropSide;
                points[j] = new Landmark((cos * x - sin * y + originalX) / frame.Width,
                    (sin * x + cos * y + originalY) / frame.Height, landmarks[j * 3 + 2] / 224 * cropSide / frame.Width);
            }
            if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z))) continue;
            result.Add(new HandTrackingResult(handedness >= 0.5 ? "Right" : "Left", Math.Min(presence, palm.Confidence), points, frame.Timestamp.WallClockUtc.UtcDateTime));
        }
        return Task.FromResult<IReadOnlyList<HandTrackingResult>>(result);
    }
    private static DenseTensor<float> Sample(Frame frame, int size, double cx, double cy, double side, double angle)
    {
        var tensor = new DenseTensor<float>(new[] { 1, size, size, 3 });
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double dx = ((x + 0.5) / size - 0.5) * side, dy = ((y + 0.5) / size - 0.5) * side;
            double px = cos * dx - sin * dy + cx - 0.5, py = sin * dx + cos * dy + cy - 0.5;
            int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
            if (x0 < 0 || y0 < 0 || x0 + 1 >= frame.Width || y0 + 1 >= frame.Height) continue;
            double fx = px - x0, fy = py - y0;
            for (int c = 0; c < 3; c++)
            {
                double top = frame.RgbPixels[y0 * frame.Stride + x0 * 3 + c] * (1 - fx) + frame.RgbPixels[y0 * frame.Stride + (x0 + 1) * 3 + c] * fx;
                double bottom = frame.RgbPixels[(y0 + 1) * frame.Stride + x0 * 3 + c] * (1 - fx) + frame.RgbPixels[(y0 + 1) * frame.Stride + (x0 + 1) * 3 + c] * fx;
                tensor[0, y, x, c] = (float)((top * (1 - fy) + bottom * fy) / 255);
            }
        }
        return tensor;
    }
    private static (double X, double Y)[] Anchors()
    {
        var result = new List<(double, double)>();
        foreach (var (size, repeats) in new[] { (24, 2), (12, 6) })
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) for (int k = 0; k < repeats; k++)
                result.Add(((x + 0.5) / size, (y + 0.5) / size));
        return result.ToArray();
    }
    private static double Overlap(Palm a, Palm b)
    {
        double w = Math.Max(0, Math.Min(a.X + a.Width / 2, b.X + b.Width / 2) - Math.Max(a.X - a.Width / 2, b.X - b.Width / 2));
        double h = Math.Max(0, Math.Min(a.Y + a.Height / 2, b.Y + b.Height / 2) - Math.Max(a.Y - a.Height / 2, b.Y - b.Height / 2));
        double intersection = w * h;
        return intersection / (a.Width * a.Height + b.Width * b.Height - intersection);
    }
    private sealed record Palm(double X, double Y, double Width, double Height, double Confidence, (double X, double Y)[] Points);
    public void Dispose() { _palm.Dispose(); _hand.Dispose(); }
}
