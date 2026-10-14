using HoloWin.Actions;
using Xunit;

namespace HoloWin.Actions.Tests;

public class ScreenshotDestinationTests
{
    [Fact]
    public void QuotedFolderProducesUniquePngNames()
    {
        var folder = Path.GetTempPath();
        var first = ScreenshotDestination.Resolve("\"" + folder + "\"");
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), Path.GetDirectoryName(first));
        Assert.EndsWith(".png", first);
        Assert.NotEqual(first, ScreenshotDestination.Resolve(folder));
    }
    [Fact]
    public void ExplicitPngIsPreservedAndOtherExtensionsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "example.png");
        Assert.Equal(path, ScreenshotDestination.Resolve(path));
        Assert.Throws<ArgumentException>(() => ScreenshotDestination.Resolve(Path.ChangeExtension(path, ".jpg")));
    }
}
