using DesktopSystemMonitor.Mac;

using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class SensorHostPathResolverTests
{
    [Fact]
    public void Resolves_Only_The_Fixed_Bundle_Path()
    {
        var fileSystem = new FakeFileSystem();
        string root = @"/Applications/DesktopSystemMonitor.app";
        string candidate = root + "/Contents/MacOS/" + SensorHostPathResolver.SensorHostFileName;

        string resolved = SensorHostPathResolver.Resolve(root, candidate, fileSystem);

        Assert.Equal(candidate, resolved);
    }

    [Theory]
    [InlineData("relative/host")]
    [InlineData("/tmp/host")]
    public void Rejects_Relative_And_Bundle_External_Paths(string candidate)
    {
        var fileSystem = new FakeFileSystem();

        Assert.Throws<InvalidOperationException>(() => SensorHostPathResolver.Resolve(
            @"/Applications/DesktopSystemMonitor.app",
            candidate,
            fileSystem));
    }

    [Fact]
    public void Rejects_Symlink_And_NonRegular_Host()
    {
        string root = @"/Applications/DesktopSystemMonitor.app";
        string candidate = root + "/Contents/MacOS/" + SensorHostPathResolver.SensorHostFileName;
        var symlink = new FakeFileSystem { SymbolicLink = true };
        var directory = new FakeFileSystem { RegularFile = false };

        Assert.Throws<InvalidOperationException>(() => SensorHostPathResolver.Resolve(root, candidate, symlink));
        Assert.Throws<InvalidOperationException>(() => SensorHostPathResolver.Resolve(root, candidate, directory));
    }

    [Fact]
    public void Rejects_Intermediate_Bundle_Symlink_When_Canonical_Target_Is_Outside()
    {
        string root = @"/Applications/DesktopSystemMonitor.app";
        string candidate = root + "/Contents/MacOS/" + SensorHostPathResolver.SensorHostFileName;
        var fileSystem = new FakeFileSystem { CanonicalCandidate = "/private/tmp/host" };

        Assert.Throws<InvalidOperationException>(() => SensorHostPathResolver.Resolve(root, candidate, fileSystem));
    }

    private sealed class FakeFileSystem : ISensorHostFileSystem
    {
        public bool RegularFile { get; init; } = true;
        public bool SymbolicLink { get; init; }
        public string? CanonicalCandidate { get; init; }

        public string GetCanonicalPath(string path) => CanonicalCandidate is not null &&
            path.EndsWith(SensorHostPathResolver.SensorHostFileName, StringComparison.Ordinal)
            ? CanonicalCandidate
            : path;
        public bool IsRegularFile(string path) => RegularFile;
        public bool IsSymbolicLink(string path) => SymbolicLink;
    }
}
