using NUnit.Framework;

/// <summary>FrameRateMonitor: spotting a frame rate that stays under 30 FPS</summary>
public class FrameRateMonitorTests
{
    /// <summary>Ticks the monitor at a steady frame rate for a while</summary>
    /// <returns>How many times it reported a low frame rate</returns>
    private static int Run(FrameRateMonitor monitor, float framesPerSecond, float seconds)
    {
        int reports = 0;
        float frame = 1 / framesPerSecond;
        for (float time = 0; time < seconds; time += frame)
        {
            if (monitor.Tick(frame))
                reports++;
        }
        return reports;
    }

    /// <summary>A smooth 60 FPS is never reported</summary>
    [Test]
    public void SteadySixtyFps_NeverReported()
    {
        Assert.AreEqual(0, Run(new FrameRateMonitor(), 60, 60));
    }

    /// <summary>Just above 30 FPS isn't slow</summary>
    [Test]
    public void ThirtyOneFps_NotReported()
    {
        Assert.AreEqual(0, Run(new FrameRateMonitor(), 31, 30));
    }

    /// <summary>A slow patch shorter than SlowSeconds isn't reported</summary>
    [Test]
    public void TwentyFps_ShortPatch_NotReported()
    {
        Assert.AreEqual(0, Run(new FrameRateMonitor(), 20, FrameRateMonitor.SlowSeconds - 0.1f));
    }

    /// <summary>A frame rate that stays under 30 is reported, a little after SlowSeconds while the smoothing catches up</summary>
    [Test]
    public void TwentyFps_Sustained_ReportedOnce()
    {
        FrameRateMonitor monitor = new();
        Assert.AreEqual(1, Run(monitor, 20, FrameRateMonitor.SlowSeconds + 1));
        Assert.AreEqual(0, Run(monitor, 20, 30), "It reports only once");
    }

    /// <summary>A short dip that recovers starts the count again</summary>
    [Test]
    public void ShortDips_NeverAddUp()
    {
        FrameRateMonitor monitor = new();
        int reports = 0;
        for (int i = 0; i < 10; i++)
        {
            reports += Run(monitor, 20, 2);
            reports += Run(monitor, 60, 2);
        }
        Assert.AreEqual(0, reports);
    }

    /// <summary>A single loading hitch isn't a slow frame rate</summary>
    [TestCase(5)]
    [TestCase(1.5f)]
    public void LoadingHitch_Ignored(float hitchSeconds)
    {
        FrameRateMonitor monitor = new();
        Assert.IsFalse(monitor.Tick(hitchSeconds));
        Assert.AreEqual(0, Run(monitor, 60, 10));
    }

    /// <summary>Frame times of 0, below 0, infinite or not a number are ignored</summary>
    [TestCase(0)]
    [TestCase(-0.1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void BadFrameTimes_Ignored(float frameSeconds)
    {
        FrameRateMonitor monitor = new();
        for (int i = 0; i < 1000; i++)
            Assert.IsFalse(monitor.Tick(frameSeconds));
        Assert.AreEqual(0, Run(monitor, 60, 10));
    }

    /// <summary>Exactly at the hitch limit still counts as a frame</summary>
    [Test]
    public void FrameAtHitchLimit_Counted()
    {
        FrameRateMonitor monitor = new();
        bool reported = false;
        for (int i = 0; i < 10 && !reported; i++)
            reported = monitor.Tick(FrameRateMonitor.HitchSeconds);
        Assert.IsTrue(reported);
    }
}
