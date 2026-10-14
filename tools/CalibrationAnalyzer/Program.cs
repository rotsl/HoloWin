using HoloWin.Core;
using HoloWin.Persistence;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: CalibrationAnalyzer <profile-id> [eval-id]");
            return 1;
        }
        var profileId = Guid.Parse(args[0]);
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "HoloWin");
        var repo = new JsonProfileRepository(dir);
        var evalRepo = new JsonEvaluationReportRepository(dir);

        var profile = await repo.LoadAsync(profileId, default);
        if (profile is null)
        {
            Console.Error.WriteLine($"Profile {profileId} not found under {dir}");
            return 1;
        }

        Console.WriteLine($"Profile: {profile.Name}");
        Console.WriteLine($"  Schema:      {profile.SchemaVersion}");
        Console.WriteLine($"  Strategy:    {profile.Strategy}");
        Console.WriteLine($"  Positives:   {profile.PositiveExamples.Count}");
        Console.WriteLine($"  Negatives:   {profile.NegativeExamples.Count}");
        Console.WriteLine($"  Calibrated:  {profile.CalibrationIsComplete}");
        var perZone = profile.PositiveExamples.GroupBy(p => p.Zone);
        foreach (var g in perZone)
        {
            Console.WriteLine($"    {g.Key}: {g.Count()} samples");
        }

        if (profile.CalibrationIsComplete)
        {
            foreach (bool includeNegatives in new[] { true, false })
            {
                int correct = 0;
                var failures = new Dictionary<string, int>();
                for (int i = 0; i < profile.PositiveExamples.Count; i++)
                {
                    var training = profile.PositiveExamples.Where((_, index) => index != i).ToArray();
                    var model = TrainedTapClassifier.Train(training, includeNegatives ? profile.NegativeExamples : Array.Empty<LabeledTap>(), profile.MinimumConfidence);
                    var sample = profile.PositiveExamples[i];
                    var result = model.Predict(sample.Feature);
                    if (result.WasAccepted && result.Zone == sample.Zone) correct++;
                    else
                    {
                        var reason = result.WasAccepted ? "Wrong zone" : result.RejectionReason.ToString() ?? "Rejected";
                        failures[reason] = failures.GetValueOrDefault(reason) + 1;
                    }
                }
                Console.WriteLine($"Leave-one-out ({(includeNegatives ? "with" : "without")} negatives): {correct}/{profile.PositiveExamples.Count}");
                foreach (var failure in failures) Console.WriteLine($"  {failure.Key}: {failure.Value}");
            }
            Console.WriteLine("These checks reuse calibration data; they do not measure real-world accuracy.");
        }

        Console.WriteLine();
        Console.WriteLine("Evaluation reports:");
        var reports = await evalRepo.ListAsync(profileId, default);
        foreach (var r in reports)
        {
            Console.WriteLine($"  {r.Id} @ {r.CreatedAtUtc:O}");
            Console.WriteLine($"    accuracy = {r.OverallAccuracy:P2}");
            Console.WriteLine($"    correct  = {r.CorrectTaps}/{r.TotalTaps}");
            Console.WriteLine($"    rejected = {r.RejectedTaps}");
            Console.WriteLine($"    median latency = {r.MedianLatencyMs:F1} ms");
        }
        return 0;
    }
}
