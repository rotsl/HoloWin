namespace HoloWin.Actions;

public static class ScreenshotDestination
{
    public static string Resolve(string value)
    {
        var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a screenshot folder or PNG file.");
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
            return Path.Combine(path, $"HoloWin-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.png");
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an existing folder or a filename ending in .png.");
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("The screenshot folder does not exist.");
        return path;
    }
}
