#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace DesktopSystemMonitor.Build.Tests;

// Runs the real Directory.Build.targets identity task against throw-away Git
// repositories under the temporary directory. Git configuration is isolated, and
// only those temporary repositories are committed or modified.
public sealed class BuildIdentityTargetsTests : IDisposable
{
    private const string ProbeProject = """
        <Project>
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <Import Project="Directory.Build.targets" />
          <Target Name="Probe" DependsOnTargets="EmbedDesktopBuildIdentity">
            <WriteLinesToFile File="$(IdentityOutput)" Lines="$(DesktopSourceId);$(DesktopDirty);$(DesktopBuiltUtc)" Overwrite="true" />
          </Target>
        </Project>
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dsm-build-identity-" + Guid.NewGuid().ToString("N"));
    private readonly string _gitConfig;
    private readonly string _git = FindExecutable("git") ?? throw new InvalidOperationException("Git is required to verify the build identity task.");

    public BuildIdentityTargetsTests()
    {
        Directory.CreateDirectory(_root);
        _gitConfig = Path.Combine(_root, "isolated.gitconfig");
        File.WriteAllText(_gitConfig, string.Empty);
    }

    public void Dispose()
    {
        // Git marks object files read-only on Windows.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Clean_Commit_Embeds_Head_Without_Changes_And_Utc_Build_Time()
    {
        var repository = CommittedRepository();
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);

        var identity = Probe(repository);

        Assert.Equal(RunGit(repository, "rev-parse", "--short=12", "HEAD"), identity.Source);
        Assert.Equal("false", identity.Dirty);
        var built = DateTimeOffset.ParseExact(identity.Built, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.InRange(built, started, DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Theory]
    [InlineData("modified", "true")]
    [InlineData("staged", "true")]
    [InlineData("untracked", "true")]
    [InlineData("ignored", "false")]
    public void Working_Tree_State_Sets_Dirty(string change, string expected)
    {
        var repository = CommittedRepository();
        switch (change)
        {
            case "modified":
                File.AppendAllText(Path.Combine(repository, "README.txt"), "changed\n");
                break;
            case "staged":
                File.WriteAllText(Path.Combine(repository, "staged.txt"), "staged\n");
                RunGit(repository, "add", "staged.txt");
                break;
            case "untracked":
                File.WriteAllText(Path.Combine(repository, "untracked.txt"), "untracked\n");
                break;
            case "ignored":
                File.WriteAllText(Path.Combine(repository, "local.ignored"), "ignored\n");
                break;
        }

        var identity = Probe(repository);

        Assert.Equal(RunGit(repository, "rev-parse", "--short=12", "HEAD"), identity.Source);
        Assert.Equal(expected, identity.Dirty);
    }

    [Fact]
    public void Repository_Without_Commit_Has_Unknown_Source()
    {
        var repository = NewTree("uncommitted");
        RunGit(repository, "init", "-q");

        Assert.Equal(("unknown", "true"), Without(Probe(repository)));
    }

    [Fact]
    public void Failed_Status_Keeps_Source_And_Reports_Unknown_Changes()
    {
        var repository = CommittedRepository();
        File.WriteAllText(Path.Combine(repository, ".git", "index"), "corrupt");

        var identity = Probe(repository);

        Assert.Equal(RunGit(repository, "rev-parse", "--short=12", "HEAD"), identity.Source);
        Assert.Equal("unknown", identity.Dirty);
    }

    [Fact]
    public void Missing_Git_Is_Unknown()
    {
        var repository = CommittedRepository();
        var emptyPath = Directory.CreateDirectory(Path.Combine(_root, "empty-path")).FullName;

        Assert.Equal(("unknown", "unknown"), Without(Probe(repository, emptyPath)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_Copy_Inside_Another_Checkout_Does_Not_Inherit_Its_Identity(bool emptyGitMarker)
    {
        var outer = CommittedRepository();
        var archive = Path.Combine(outer, "archive");
        WriteProbe(archive);
        if (emptyGitMarker) Directory.CreateDirectory(Path.Combine(archive, ".git"));

        Assert.Equal(("unknown", "unknown"), Without(Probe(archive)));
    }

    [Theory]
    [InlineData("directory-and-work-tree")]
    [InlineData("work-tree")]
    [InlineData("common-directory")]
    [InlineData("index")]
    [InlineData("objects")]
    [InlineData("config-parameters")]
    [InlineData("config-count")]
    public void Foreign_Git_Environment_Does_Not_Override_Source_Or_Changes(string selection)
    {
        var repository = CommittedRepository();
        var foreign = CommittedRepository("foreign");
        File.AppendAllText(Path.Combine(foreign, "README.txt"), "foreign commit\n");
        RunGit(foreign, "add", "README.txt");
        RunGit(foreign, "-c", "user.name=Build Identity Test", "-c", "user.email=build-identity@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-q", "-m", "foreign");
        File.AppendAllText(Path.Combine(foreign, "README.txt"), "foreign dirty\n");
        var expected = RunGit(repository, "rev-parse", "--short=12", "HEAD");
        Assert.NotEqual(expected, RunGit(foreign, "rev-parse", "--short=12", "HEAD"));
        var environment = new Dictionary<string, string>();
        switch (selection)
        {
            case "directory-and-work-tree":
                environment["GIT_DIR"] = Path.Combine(foreign, ".git");
                environment["GIT_WORK_TREE"] = foreign;
                break;
            case "work-tree":
                environment["GIT_WORK_TREE"] = foreign;
                break;
            case "common-directory":
                environment["GIT_COMMON_DIR"] = Path.Combine(foreign, ".git");
                break;
            case "index":
                var index = Path.Combine(_root, "corrupt-index");
                File.WriteAllText(index, "corrupt");
                environment["GIT_INDEX_FILE"] = index;
                break;
            case "objects":
                environment["GIT_OBJECT_DIRECTORY"] = Path.Combine(foreign, ".git", "objects");
                break;
            case "config-parameters":
                environment["GIT_CONFIG_PARAMETERS"] = "'core.worktree=" + foreign + "'";
                break;
            case "config-count":
                environment["GIT_CONFIG_COUNT"] = "1";
                environment["GIT_CONFIG_KEY_0"] = "core.worktree";
                environment["GIT_CONFIG_VALUE_0"] = foreign;
                break;
        }

        Assert.Equal((expected, "false"), Without(Probe(repository, environment: environment)));
    }

    private static (string Source, string Dirty) Without((string Source, string Dirty, string Built) identity)
        => (identity.Source, identity.Dirty);

    private string NewTree(string name)
    {
        var repository = Path.Combine(_root, name);
        WriteProbe(repository);
        File.WriteAllText(Path.Combine(repository, "README.txt"), "fixture\n");
        File.WriteAllText(Path.Combine(repository, ".gitignore"), "*.ignored\n");
        return repository;
    }

    private string CommittedRepository(string name = "repository")
    {
        var repository = NewTree(name);
        RunGit(repository, "init", "-q");
        RunGit(repository, "add", "-A");
        RunGit(repository, "-c", "user.name=Build Identity Test", "-c", "user.email=build-identity@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-q", "-m", "fixture");
        return repository;
    }

    private static void WriteProbe(string repository)
    {
        var project = Directory.CreateDirectory(Path.Combine(repository, "project")).FullName;
        using (var fixture = typeof(BuildIdentityTargetsTests).Assembly.GetManifestResourceStream(
            "DesktopSystemMonitor.Build.Tests.Directory.Build.targets")
            ?? throw new InvalidOperationException("The build identity fixture was not embedded."))
        using (var output = File.Create(Path.Combine(project, "Directory.Build.targets")))
            fixture.CopyTo(output);
        File.WriteAllText(Path.Combine(project, "DesktopSystemMonitor.App.csproj"), ProbeProject);
    }

    private (string Source, string Dirty, string Built) Probe(string repository, string? path = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var output = Path.Combine(_root, "identity-" + Guid.NewGuid().ToString("N") + ".txt");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
            ? host
            : FindExecutable("dotnet") ?? throw new InvalidOperationException("dotnet was not found.");
        var result = Run(dotnet, _root, path, environment, "msbuild", Path.Combine(repository, "project", "DesktopSystemMonitor.App.csproj"),
            "-t:Probe", "-nologo", "-verbosity:quiet", "-nodeReuse:false", "-p:IdentityOutput=" + output);
        Assert.True(result.ExitCode == 0, result.Output);
        var lines = File.ReadAllLines(output);
        Assert.Equal(3, lines.Length);
        return (lines[0], lines[1], lines[2]);
    }

    private string RunGit(string repository, params string[] arguments)
    {
        var result = Run(_git, repository, null, null, arguments);
        Assert.True(result.ExitCode == 0, result.Output);
        return result.Output.Trim();
    }

    private (int ExitCode, string Output) Run(string fileName, string workingDirectory, string? path, IReadOnlyDictionary<string, string>? environment, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // Inherited Git or MSBuild variables could point at the developer's repository or build.
        foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(name);
        start.Environment["GIT_CONFIG_GLOBAL"] = _gitConfig;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (path is not null) start.Environment["PATH"] = path;
        // Inject regression overrides after isolation; the production task must
        // ignore them itself, without changing this test process's environment.
        if (environment is not null)
            foreach (var entry in environment) start.Environment[entry.Key] = entry.Value;
        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(fileName + " did not finish.");
        }
        return (process.ExitCode, standardOutput.Result + standardError.Result);
    }

    private static string? FindExecutable(string name)
    {
        var names = OperatingSystem.IsWindows() ? new[] { name + ".exe", name + ".cmd" } : new[] { name };
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => names.Select(candidate => Path.Combine(directory, candidate)))
            .FirstOrDefault(File.Exists);
    }
}
