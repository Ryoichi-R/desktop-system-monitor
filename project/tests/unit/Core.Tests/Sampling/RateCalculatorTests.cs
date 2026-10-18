using DesktopSystemMonitor.Core.Sampling;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Sampling;

public class RateCalculatorTests
{
    private const double TicksPerSecond = 10_000_000d; // pretend 100 ns ticks

    [Fact]
    public void first_sample_only_establishes_baseline_and_returns_warmup()
    {
        var calc = new RateCalculator();
        var sample = calc.Update(1000, 0, TicksPerSecond);
        Assert.True(sample.IsWarmingUp);
        Assert.False(calc.HasBaseline == false);
    }

    [Fact]
    public void steady_1000_bytes_per_second_produces_1000()
    {
        var calc = new RateCalculator();
        _ = calc.Update(0, 0, TicksPerSecond);
        var sample = calc.Update(1000, (long)TicksPerSecond, TicksPerSecond);
        Assert.Equal(1000d, sample.RatePerSecond, precision: 6);
        Assert.Equal(1000, sample.Delta);
        Assert.Equal(1000, calc.SessionAccumulated);
    }

    [Fact]
    public void zero_elapsed_returns_warmup_and_does_not_update_baseline()
    {
        var calc = new RateCalculator();
        _ = calc.Update(0, 5, TicksPerSecond);
        var sample = calc.Update(1000, 5, TicksPerSecond);
        Assert.True(sample.IsWarmingUp);
        var next = calc.Update(2000, 5 + (long)TicksPerSecond, TicksPerSecond);
        Assert.Equal(2000d, next.RatePerSecond, precision: 6);
    }

    [Fact]
    public void reset_when_new_value_is_far_below_previous_is_treated_as_baseline()
    {
        var calc = new RateCalculator();
        _ = calc.Update(1_000_000_000, 0, TicksPerSecond);
        var sample = calc.Update(10, (long)TicksPerSecond, TicksPerSecond);
        Assert.True(sample.IsWarmingUp);
        Assert.Equal(0, calc.SessionAccumulated);
    }

    [Fact]
    public void wrap_near_boundary_is_recovered_as_positive_delta()
    {
        long boundary = 100;
        var calc = new RateCalculator(wrapBoundary: boundary);
        _ = calc.Update(95, 0, TicksPerSecond);
        var sample = calc.Update(5, (long)TicksPerSecond, TicksPerSecond);
        Assert.False(sample.IsWarmingUp);
        Assert.Equal(10, sample.Delta);
        Assert.Equal(10d, sample.RatePerSecond, precision: 6);
    }

    [Fact]
    public void negative_current_value_throws()
    {
        var calc = new RateCalculator();
        Assert.Throws<ArgumentOutOfRangeException>(() => calc.Update(-1, 0, TicksPerSecond));
    }

    [Fact]
    public void reset_clears_baseline_and_session()
    {
        var calc = new RateCalculator();
        _ = calc.Update(0, 0, TicksPerSecond);
        _ = calc.Update(500, (long)TicksPerSecond, TicksPerSecond);
        calc.Reset();
        Assert.False(calc.HasBaseline);
        Assert.Equal(0, calc.SessionAccumulated);
        var sample = calc.Update(1000, 0, TicksPerSecond);
        Assert.True(sample.IsWarmingUp);
    }

    [Fact]
    public void session_accumulated_grows_monotonically_across_ticks()
    {
        var calc = new RateCalculator();
        _ = calc.Update(0, 0, TicksPerSecond);
        _ = calc.Update(1_000, (long)TicksPerSecond, TicksPerSecond);
        _ = calc.Update(3_000, 2 * (long)TicksPerSecond, TicksPerSecond);
        _ = calc.Update(3_500, 3 * (long)TicksPerSecond, TicksPerSecond);
        Assert.Equal(3_500, calc.SessionAccumulated);
    }
}
