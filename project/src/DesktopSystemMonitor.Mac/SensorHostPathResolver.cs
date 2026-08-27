using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Mac;

internal interface ISensorHostFileSystem
{
    string GetCanonicalPath(string path);
    bool IsRegularFile(string path);
    bool IsSymbolicLink(string path);
}

internal sealed class RealSensorHostFileSystem : ISensorHostFileSystem
{
    public string GetCanonicalPath(string path) => MacRealPath.Resolve(Path.GetFullPath(path));

    public bool IsRegularFile(string path) => File.Exists(path) && !Directory.Exists(path);

    public bool IsSymbolicLink(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

internal static partial class MacRealPath
{
    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial IntPtr RealPath(string path, IntPtr resolvedPath);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);

    internal static string Resolve(string path)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return path;
        }

        IntPtr resolved = RealPath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            throw new IOException($"Unable to resolve canonical macOS path: {path}");
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved)
                ?? throw new IOException($"macOS realpath returned an empty path: {path}");
        }
        finally
        {
            Free(resolved);
        }
    }
}

internal static class SensorHostPathResolver
{
    internal const string SensorHostFileName = "DesktopSystemMonitor.Mac.SensorHost";

    internal static string Resolve(string bundleRoot, string candidatePath, ISensorHostFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (!IsMacAbsolutePath(bundleRoot) || !IsMacAbsolutePath(candidatePath))
        {
            throw new InvalidOperationException("SensorHost paths must be absolute.");
        }

        string canonicalRoot = TrimDirectorySeparator(fileSystem.GetCanonicalPath(bundleRoot));
        string canonicalCandidate = fileSystem.GetCanonicalPath(candidatePath);
        string expected = JoinMacPath(canonicalRoot, "Contents", "MacOS", SensorHostFileName);
        if (!PathEquals(canonicalCandidate, expected))
        {
            throw new InvalidOperationException("SensorHost is outside the fixed app bundle layout.");
        }
        if (fileSystem.IsSymbolicLink(candidatePath))
        {
            throw new InvalidOperationException("SensorHost symlinks are not allowed.");
        }
        if (!fileSystem.IsRegularFile(candidatePath))
        {
            throw new InvalidOperationException("SensorHost must be a regular file.");
        }

        return canonicalCandidate;
    }

    private static string TrimDirectorySeparator(string path) => path.TrimEnd('/');

    private static bool IsMacAbsolutePath(string path) => path.StartsWith('/');

    private static string JoinMacPath(params string[] parts) =>
        string.Join("/", parts.Select(part => part.Trim('/'))).Insert(0, "/");

    private static bool PathEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
