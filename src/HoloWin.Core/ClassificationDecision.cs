namespace HoloWin.Core;

public sealed record ClassificationDecision
{
    public DeskZone? Zone { get; init; }
    public double Confidence { get; init; }
    public double SignalStrength { get; init; }
    public IReadOnlyList<double> ZoneDistances { get; init; } = Array.Empty<double>();
    public RejectionReason? RejectionReason { get; init; }
    public double ProcessingLatencyMilliseconds { get; init; }
    public MonotonicTimestamp? Timestamp { get; init; }

    public bool WasAccepted => Zone is not null && RejectionReason is null;
}

public static class ClassifierDefaults
{
    public const double MinimumConfidence = 0.36;
    public const double MinimumRelativeSeparation = 0.035;
    public const double MinimumLinearScoreMargin = 0.075;
}

public sealed class ClassifierTrainingException : Exception
{
    public ClassifierTrainingException(string message) : base(message) { }
}
