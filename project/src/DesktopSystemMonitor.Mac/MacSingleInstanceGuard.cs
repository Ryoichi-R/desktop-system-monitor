using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DesktopSystemMonitor.Mac;

/// <summary>Holds a stable, per-user file lock for the lifetime of the Mac app.</summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacSingleInstanceGuard : IDisposable
{
    private const int Exclusive = 2;
    private const int NonBlocking = 4;
    private const int WouldBlock = 35;
    private const int Interrupted = 4;
    private const int NoFollow = 0x100;
    private const int CloseOnExec = 0x1000000;
    private readonly SafeFileHandle _handle;

    private MacSingleInstanceGuard(SafeFileHandle handle) => _handle = handle;

    public static MacSingleInstanceGuard? TryAcquire() =>
        TryAcquire(Path.Combine(Path.GetDirectoryName(new MacAppPathProvider().SettingsFilePath)!, "instance.lock"));

    internal static MacSingleInstanceGuard? TryAcquire(string lockPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // Keep the file in place so all launchers lock the same inode. Open the
        // existing file without FileStream's implicit sharing lock.
        if (!File.Exists(lockPath))
        {
            try
            {
                using var created = new FileStream(lockPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                });
            }
            catch (IOException) when (File.Exists(lockPath)) { }
        }
        int descriptor = Open(lockPath, NoFollow | CloseOnExec);
        if (descriptor < 0)
            throw new IOException($"Could not open the macOS instance lock (errno {Marshal.GetLastPInvokeError()}).");
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        bool acquired = false;
        try
        {
            int error;
            do
            {
                if (Flock(handle, Exclusive | NonBlocking) == 0)
                {
                    acquired = true;
                    return new(handle);
                }
                error = Marshal.GetLastPInvokeError();
            }
            while (error == Interrupted);
            if (error == WouldBlock) return null;
            throw new IOException($"Could not acquire the macOS instance lock (errno {error}).");
        }
        finally
        {
            // Ownership transfers only on a successful flock.
            if (!acquired) handle.Dispose();
        }
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(SafeFileHandle handle, int operation);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);

    public void Dispose() => _handle.Dispose();
}
