using Atoll.Platform.Windows.Animation;
using Xunit;

namespace Atoll.UnitTests;

public class SpringMathTests
{
    [Fact]
    public void Progress_StartsAtZero_EndsAtOne()
    {
        Assert.Equal(0.0, SpringMath.Progress(0));
        Assert.Equal(1.0, SpringMath.Progress(1));
        Assert.Equal(0.0, SpringMath.Progress(-0.5));
        Assert.Equal(1.0, SpringMath.Progress(1.5));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.9)]
    public void CriticallyDamped_IsMonotonicAndDoesNotOvershoot(double t)
    {
        var p = SpringMath.Progress(t);
        var next = SpringMath.Progress(Math.Min(1.0, t + 0.05));
        Assert.InRange(p, 0.0, 1.0);
        Assert.True(next >= p);
    }

    [Fact]
    public void CriticallyDamped_HalfTimeIsAbout82Percent()
    {
        // For response R, at t = R/2 the response is ~82% complete.
        var p = SpringMath.Progress(0.5);
        Assert.InRange(p, 0.78, 0.85);
    }

    [Fact]
    public void Underdamped_OvershootsTarget()
    {
        var max = 0.0;
        for (var t = 0.0; t <= 1.0; t += 0.01)
            max = System.Math.Max(max, SpringMath.Progress(t, 0.4));

        Assert.True(max > 1.0);
    }

    [Fact]
    public void Lerp_Interpolates()
    {
        Assert.Equal(5.0, SpringMath.Lerp(0, 10, 0.5));
        Assert.Equal(0.0, SpringMath.Lerp(0, 10, 0));
        Assert.Equal(10.0, SpringMath.Lerp(0, 10, 1));
    }
}
