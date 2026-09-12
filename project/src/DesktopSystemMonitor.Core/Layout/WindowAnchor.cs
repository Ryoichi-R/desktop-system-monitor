using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.Core.Layout;

/// <summary>
/// Computes the widget's on-screen rectangle from user-persisted state and the
/// currently available monitors. Two invariants:
///
/// 1. When the saved monitor is still present, honor its stored right/top edge.
/// 2. Otherwise anchor to the primary monitor's working-area top-right corner,
///    then clamp so the widget never disappears off-screen.
/// </summary>
public static class WindowAnchor
{
    public const double Margin = 8.0;

    public static Rect Compute(
        double widgetWidth,
        double widgetHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primary,
        string? savedDeviceName,
        double? savedRightEdge,
        double? savedTopEdge,
        WindowPlacementMode placementMode = WindowPlacementMode.Custom,
        WindowPlacementAnchor placementAnchor = WindowPlacementAnchor.TopRight,
        double horizontalMargin = Margin,
        double verticalMargin = Margin,
        double? savedWorkAreaWidth = null,
        double? savedWorkAreaHeight = null,
        double? savedXRatio = null,
        double? savedYRatio = null)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(primary);
        if (widgetWidth <= 0 || widgetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widgetWidth));
        }

        MonitorInfo target = primary;
        if (savedDeviceName is not null)
        {
            foreach (var m in monitors)
            {
                if (string.Equals(m.DeviceName, savedDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    target = m;
                    break;
                }
            }
        }

        bool preset = placementMode == WindowPlacementMode.Preset;
        bool rightAnchored = placementAnchor is WindowPlacementAnchor.TopRight or WindowPlacementAnchor.BottomRight;
        bool bottomAnchored = placementAnchor is WindowPlacementAnchor.BottomLeft or WindowPlacementAnchor.BottomRight;
        double left;
        double top;
        if (preset)
        {
            left = rightAnchored
                ? target.WorkArea.Right - widgetWidth - horizontalMargin
                : target.WorkArea.Left + horizontalMargin;
            top = bottomAnchored
                ? target.WorkArea.Bottom - widgetHeight - verticalMargin
                : target.WorkArea.Top + verticalMargin;
        }
        else if (ShouldUseSavedRatio(
            target.WorkArea,
            savedWorkAreaWidth,
            savedWorkAreaHeight,
            savedXRatio,
            savedYRatio))
        {
            GetMovementBounds(
                widgetWidth,
                widgetHeight,
                target.WorkArea,
                out double movementMinLeft,
                out double movementMaxLeft,
                out double movementMinTop,
                out double movementMaxTop);
            if (movementMaxLeft >= movementMinLeft && movementMaxTop >= movementMinTop)
            {
                left = movementMinLeft + ((movementMaxLeft - movementMinLeft) * savedXRatio!.Value);
                top = movementMinTop + ((movementMaxTop - movementMinTop) * savedYRatio!.Value);
            }
            else
            {
                left = (savedRightEdge ?? (target.WorkArea.Right - Margin)) - widgetWidth;
                top = savedTopEdge ?? (target.WorkArea.Top + Margin);
            }
        }
        else
        {
            left = (savedRightEdge ?? (target.WorkArea.Right - Margin)) - widgetWidth;
            top = savedTopEdge ?? (target.WorkArea.Top + Margin);
        }

        // Clamp to monitor working area with margin so the widget stays fully
        // visible even when resolution or DPI changed.
        double clampMargin = preset ? 0 : Margin;
        double minLeft = target.WorkArea.Left + clampMargin;
        double maxLeft = target.WorkArea.Right - widgetWidth - clampMargin;
        double minTop = target.WorkArea.Top + clampMargin;
        double maxTop = target.WorkArea.Bottom - widgetHeight - clampMargin;

        if (maxLeft < minLeft)
        {
            maxLeft = minLeft;
        }
        if (maxTop < minTop)
        {
            maxTop = minTop;
        }

        double clampedLeft = Math.Clamp(left, minLeft, maxLeft);
        double clampedTop = Math.Clamp(top, minTop, maxTop);
        return new Rect(clampedLeft, clampedTop, widgetWidth, widgetHeight);
    }

    internal static bool TryCaptureIntent(
        Rect widget,
        MonitorInfo monitor,
        out WindowPlacementIntent intent)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        intent = default;
        if (!IsFinitePositive(widget.Width)
            || !IsFinitePositive(widget.Height)
            || !IsFiniteRect(widget)
            || !IsFiniteRect(monitor.WorkArea))
        {
            return false;
        }

        GetMovementBounds(
            widget.Width,
            widget.Height,
            monitor.WorkArea,
            out double minLeft,
            out double maxLeft,
            out double minTop,
            out double maxTop);
        if (maxLeft < minLeft || maxTop < minTop)
        {
            return false;
        }

        double xRatio = maxLeft == minLeft
            ? 0
            : Math.Clamp((widget.Left - minLeft) / (maxLeft - minLeft), 0, 1);
        double yRatio = maxTop == minTop
            ? 0
            : Math.Clamp((widget.Top - minTop) / (maxTop - minTop), 0, 1);
        if (!double.IsFinite(xRatio) || !double.IsFinite(yRatio))
        {
            return false;
        }

        intent = new WindowPlacementIntent(monitor.DeviceName, xRatio, yRatio);
        return true;
    }

    internal static bool TryComputeFromIntent(
        double widgetWidth,
        double widgetHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primary,
        WindowPlacementIntent intent,
        out Rect rect)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(primary);
        rect = default;
        if (!IsFinitePositive(widgetWidth)
            || !IsFinitePositive(widgetHeight)
            || !double.IsFinite(intent.XRatio)
            || !double.IsFinite(intent.YRatio)
            || intent.XRatio < 0
            || intent.XRatio > 1
            || intent.YRatio < 0
            || intent.YRatio > 1)
        {
            return false;
        }

        MonitorInfo target = FindByDeviceName(monitors, intent.DeviceName) ?? primary;
        if (!IsFiniteRect(target.WorkArea))
        {
            return false;
        }

        GetMovementBounds(
            widgetWidth,
            widgetHeight,
            target.WorkArea,
            out double minLeft,
            out double maxLeft,
            out double minTop,
            out double maxTop);
        if (maxLeft < minLeft || maxTop < minTop)
        {
            return false;
        }

        double left = minLeft + ((maxLeft - minLeft) * intent.XRatio);
        double top = minTop + ((maxTop - minTop) * intent.YRatio);
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return false;
        }

        rect = new Rect(left, top, widgetWidth, widgetHeight);
        return true;
    }

    internal static MonitorInfo FindMonitorForWidget(
        Rect widget,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primary)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(primary);
        double centerX = widget.Left + (widget.Width / 2);
        double centerY = widget.Top + (widget.Height / 2);
        return monitors.FirstOrDefault(monitor => monitor.WorkArea.Contains(centerX, centerY))
            ?? primary;
    }

    private static MonitorInfo? FindByDeviceName(
        IReadOnlyList<MonitorInfo> monitors,
        string? deviceName)
    {
        if (deviceName is null)
        {
            return null;
        }
        foreach (MonitorInfo monitor in monitors)
        {
            if (string.Equals(
                monitor.DeviceName,
                deviceName,
                StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }
        return null;
    }

    private static void GetMovementBounds(
        double widgetWidth,
        double widgetHeight,
        Rect workArea,
        out double minLeft,
        out double maxLeft,
        out double minTop,
        out double maxTop)
    {
        minLeft = workArea.Left + Margin;
        maxLeft = workArea.Right - widgetWidth - Margin;
        minTop = workArea.Top + Margin;
        maxTop = workArea.Bottom - widgetHeight - Margin;
    }

    private static bool ShouldUseSavedRatio(
        Rect currentWorkArea,
        double? savedWorkAreaWidth,
        double? savedWorkAreaHeight,
        double? savedXRatio,
        double? savedYRatio)
    {
        if (savedWorkAreaWidth is not { } width
            || savedWorkAreaHeight is not { } height
            || savedXRatio is not { } xRatio
            || savedYRatio is not { } yRatio
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0
            || !double.IsFinite(xRatio)
            || !double.IsFinite(yRatio)
            || xRatio is < 0 or > 1
            || yRatio is < 0 or > 1
            || !double.IsFinite(currentWorkArea.Width)
            || !double.IsFinite(currentWorkArea.Height))
        {
            return false;
        }

        return Math.Abs(currentWorkArea.Width - width) > 0.5
            || Math.Abs(currentWorkArea.Height - height) > 0.5;
    }

    private static bool IsFinitePositive(double value) =>
        double.IsFinite(value) && value > 0;

    private static bool IsFiniteRect(Rect rect) =>
        double.IsFinite(rect.Left)
        && double.IsFinite(rect.Top)
        && IsFinitePositive(rect.Width)
        && IsFinitePositive(rect.Height);
}

internal readonly record struct WindowPlacementIntent(
    string? DeviceName,
    double XRatio,
    double YRatio);
