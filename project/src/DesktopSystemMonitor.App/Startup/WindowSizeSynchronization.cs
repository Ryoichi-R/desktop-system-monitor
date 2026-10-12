using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopSystemMonitor.Windows.Window;
using Size = System.Windows.Size;

namespace DesktopSystemMonitor.App.Startup;

internal enum WindowSizeSyncStatus
{
    Matched,
    Corrected,
    NotReady,
    DpiNotConfirmed,
    NativeBoundsUnavailable,
    InvalidRequiredSize,
    Mismatch,
}

/// <summary>
/// Outcome of comparing the required outer window size (DIP) with the native
/// HWND size (px). Only <see cref="WindowSizeSyncStatus.Matched"/> and
/// <see cref="WindowSizeSyncStatus.Corrected"/> allow placement to succeed.
/// </summary>
internal readonly record struct WindowSizeSyncResult(WindowSizeSyncStatus Status, string Detail)
{
    internal static WindowSizeSyncResult Unchecked { get; } = new(WindowSizeSyncStatus.Matched, "unchecked");

    internal bool IsSynchronized => Status is WindowSizeSyncStatus.Matched or WindowSizeSyncStatus.Corrected;
}

/// <summary>
/// Converts the required outer DIP size to the physical HWND size. WPF rounds
/// a window's device size half away from zero (175% x 225 DIP = 393.75 px is
/// 394 px, 198 DIP = 346.5 px is 347 px), so the expected size uses the same
/// single rounding step and the DPI scale is applied exactly once.
/// </summary>
internal static class WindowPixelSize
{
    internal const double TolerancePx = 1d;
    private const double ScaleTolerance = 0.0001d;

    internal static bool TryGetScale(uint windowDpi, double transformX, double transformY, out double scale)
    {
        scale = windowDpi / 96d;
        return windowDpi > 0
            && double.IsFinite(transformX)
            && double.IsFinite(transformY)
            && transformX > 0
            && Math.Abs(transformX - transformY) <= ScaleTolerance
            && Math.Abs(transformX - scale) <= ScaleTolerance;
    }

    internal static bool TryToDevicePixels(double dip, double scale, out int pixels)
    {
        pixels = 0;
        if (!double.IsFinite(dip) || dip <= 0 || !double.IsFinite(scale) || scale <= 0)
        {
            return false;
        }
        double rounded = Math.Round(dip * scale, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(rounded) || rounded < 1 || rounded > int.MaxValue)
        {
            return false;
        }
        pixels = (int)rounded;
        return true;
    }

    internal static bool Matches(double actualPixels, int expectedPixels) =>
        double.IsFinite(actualPixels) && Math.Abs(actualPixels - expectedPixels) <= TolerancePx;

    internal static bool ContentMatches(double actualDip, double requiredDip, double scale) =>
        double.IsFinite(actualDip)
        && double.IsFinite(requiredDip)
        && scale > 0
        && Math.Abs(actualDip - requiredDip) <= TolerancePx / scale;

    internal static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Synchronizes a WPF window's native frame with its required outer DIP size.
/// A DPI change whose suggested rectangle was computed for another DPI can
/// leave WPF at the new DPI with an unchanged <see cref="FrameworkElement.Width"/>
/// while the HWND keeps the previous pixel size. Reassigning the same Width
/// is a no-op in that state, so the exact pixel size is applied natively after
/// the WPF path. Success requires the native size and, once the window is
/// loaded, the WPF client size to match at a confirmed DPI.
/// </summary>
internal static class WindowSizeSynchronizer
{
    internal static WindowSizeSyncResult Synchronize(
        Window window,
        Size required,
        bool apply,
        Func<string>? describeContent = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Dispatcher.VerifyAccess();
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return new WindowSizeSyncResult(WindowSizeSyncStatus.NotReady, "handle=none");
        }
        WindowSizeSyncResult result = Evaluate(window, handle, required, describeContent, out int expectedWidth, out int expectedHeight);
        if (result.Status != WindowSizeSyncStatus.Mismatch || !apply)
        {
            return result;
        }

        if (!AreClose(window.Width, required.Width) || !AreClose(window.Height, required.Height))
        {
            window.Width = required.Width;
            window.Height = required.Height;
            window.UpdateLayout();
            result = Evaluate(window, handle, required, describeContent, out expectedWidth, out expectedHeight);
            if (result.Status != WindowSizeSyncStatus.Mismatch)
            {
                return result.Status == WindowSizeSyncStatus.Matched ? Corrected(result, "wpf") : result;
            }
        }

        if (!WindowPlacementNativeApi.TrySetSize(handle, expectedWidth, expectedHeight))
        {
            return new WindowSizeSyncResult(WindowSizeSyncStatus.Mismatch, $"nativeResize=failed; {result.Detail}");
        }
        window.UpdateLayout();
        result = Evaluate(window, handle, required, describeContent, out _, out _);
        return result.Status == WindowSizeSyncStatus.Matched ? Corrected(result, "native") : result;
    }

    private static WindowSizeSyncResult Evaluate(
        Window window,
        IntPtr handle,
        Size required,
        Func<string>? describeContent,
        out int expectedWidth,
        out int expectedHeight)
    {
        expectedWidth = 0;
        expectedHeight = 0;
        uint windowDpi = WindowPlacementNativeApi.GetDpiForWindow(handle);
        Matrix? transform = DeviceTransform(handle);
        string basis = $"requiredDip=({WindowPixelSize.Format(required.Width)},{WindowPixelSize.Format(required.Height)}); windowDpi={windowDpi}; transform=({WindowPixelSize.Format(transform?.M11 ?? double.NaN)},{WindowPixelSize.Format(transform?.M22 ?? double.NaN)})";
        if (transform is not Matrix deviceTransform)
        {
            return new WindowSizeSyncResult(WindowSizeSyncStatus.NotReady, basis);
        }
        if (!WindowPixelSize.TryGetScale(windowDpi, deviceTransform.M11, deviceTransform.M22, out double scale))
        {
            return new WindowSizeSyncResult(WindowSizeSyncStatus.DpiNotConfirmed, basis);
        }
        if (!WindowPixelSize.TryToDevicePixels(required.Width, scale, out expectedWidth)
            || !WindowPixelSize.TryToDevicePixels(required.Height, scale, out expectedHeight))
        {
            return new WindowSizeSyncResult(WindowSizeSyncStatus.InvalidRequiredSize, basis);
        }
        if (!WindowPlacementNativeApi.TryGetBounds(handle, out DesktopSystemMonitor.Core.Layout.Rect native))
        {
            return new WindowSizeSyncResult(
                WindowSizeSyncStatus.NativeBoundsUnavailable,
                $"{basis}; expectedPx=({expectedWidth},{expectedHeight})");
        }
        string content = describeContent is null ? string.Empty : $"; {describeContent()}";
        string detail = $"{basis}; expectedPx=({expectedWidth},{expectedHeight}); nativePx=({WindowPixelSize.Format(native.Width)},{WindowPixelSize.Format(native.Height)}); actualDip=({WindowPixelSize.Format(window.ActualWidth)},{WindowPixelSize.Format(window.ActualHeight)}){content}";
        bool nativeMatches = WindowPixelSize.Matches(native.Width, expectedWidth)
            && WindowPixelSize.Matches(native.Height, expectedHeight);
        // Before the first Show the client size has not been laid out yet;
        // the native frame is still verified so startup placement can proceed.
        bool contentMatches = !window.IsLoaded
            || (WindowPixelSize.ContentMatches(window.ActualWidth, required.Width, scale)
                && WindowPixelSize.ContentMatches(window.ActualHeight, required.Height, scale));
        return new WindowSizeSyncResult(
            nativeMatches && contentMatches ? WindowSizeSyncStatus.Matched : WindowSizeSyncStatus.Mismatch,
            detail);
    }

    /// <summary>
    /// Device transform of the window's own HWND target. Unlike
    /// <see cref="PresentationSource.FromVisual"/>, it is available as soon as
    /// the handle exists, so startup placement before the first Show can be
    /// verified at the window's DPI.
    /// </summary>
    internal static Matrix? DeviceTransform(IntPtr handle) =>
        handle == IntPtr.Zero ? null : HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformToDevice;

    private static WindowSizeSyncResult Corrected(WindowSizeSyncResult matched, string path) =>
        new(WindowSizeSyncStatus.Corrected, $"correctedBy={path}; {matched.Detail}");

    private static bool AreClose(double left, double right) =>
        double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) < 0.0001d;
}
