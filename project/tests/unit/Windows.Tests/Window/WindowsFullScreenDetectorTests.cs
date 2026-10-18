using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Window;

public sealed class WindowsFullScreenDetectorTests
{
    [Fact]
    public void IsForegroundFullScreenDelegatesToTheRealWin32ImplementationWithoutThrowing()
    {
        var detector = new WindowsFullScreenDetector();

        // 実行環境の前面ウィンドウに依存するため真偽値は問わない。委譲先の
        // FullScreenDetector.IsForegroundFullScreen(IntPtr)へ正しく届き、
        // 例外を投げずに完了することだけを確認する。
        Exception? exception = Record.Exception(() => detector.IsForegroundFullScreen(IntPtr.Zero));

        Assert.Null(exception);
    }
}
