using System;
using NUnit.Framework;

/// <summary>Watcher and CallStats: the timers behind the benchmark logs</summary>
public class BenchmarkTests
{
    [SetUp]
    public void ClearTimings() => Watcher.Reset();

    [TearDown]
    public void ClearTimingsAfter() => Watcher.Reset();

    /// <summary>Each timed call is counted and the action runs</summary>
    [Test]
    public void ExecuteWithTimer_RecordsEveryCall()
    {
        int runs = 0;
        for (int i = 0; i < 3; i++)
            Watcher.ExecuteWithTimer("1. Step", () => runs++);

        Assert.AreEqual(3, runs);
        Assert.AreEqual(3, Watcher.Count);
        StringAssert.Contains("1. Step", Watcher.Log());
    }

    /// <summary>Blocks are logged in the order of the number their names start with</summary>
    [Test]
    public void Log_OrdersByLeadingNumber()
    {
        Watcher.ExecuteWithTimer("10. Late", () => { });
        Watcher.ExecuteWithTimer("2. Early", () => { });

        string log = Watcher.Log();
        Assert.Less(log.IndexOf("2. Early", StringComparison.Ordinal), log.IndexOf("10. Late", StringComparison.Ordinal));
    }

    /// <summary>An action that throws passes the exception on and isn't recorded</summary>
    [Test]
    public void ExecuteWithTimer_ActionThrows_PropagatesAndRecordsNothing()
    {
        Assert.Throws<InvalidOperationException>(() => Watcher.ExecuteWithTimer("1. Bad", () => throw new InvalidOperationException()));
        Assert.AreEqual("", Watcher.Log());
    }

    /// <summary>A null action is rejected</summary>
    [Test]
    public void ExecuteWithTimer_NullAction_Throws()
    {
        Assert.Catch(() => Watcher.ExecuteWithTimer("1. Null", null));
    }

    /// <summary>With nothing timed there's no first block to report</summary>
    [Test]
    public void Empty_CountAndTotal_Throw()
    {
        Assert.Throws<InvalidOperationException>(() => _ = Watcher.Count);
        Assert.Throws<InvalidOperationException>(() => Watcher.GetTotal());
        Assert.AreEqual("", Watcher.Log());
    }

    /// <summary>With two blocks, a name that doesn't start with a number can't be ordered</summary>
    [Test]
    public void Log_NameWithoutNumber_Throws()
    {
        Watcher.ExecuteWithTimer("1. Fine", () => { });
        Watcher.ExecuteWithTimer("No number", () => { });
        Assert.Throws<FormatException>(() => Watcher.Log());
    }

    /// <summary>The average is rounded down to whole ticks</summary>
    [Test]
    public void CallStats_Average_RoundsDown()
    {
        CallStats stats = new();
        stats.AddCall(1);
        stats.AddCall(2);

        Assert.AreEqual(1, stats.GetAverage());
        Assert.AreEqual(3, stats.GetSum());
        Assert.AreEqual(2, stats.Count);
    }

    /// <summary>Empty stats sum to 0 but have no average</summary>
    [Test]
    public void CallStats_Empty()
    {
        CallStats stats = new();
        Assert.AreEqual(0, stats.GetSum());
        Assert.Throws<InvalidOperationException>(() => stats.GetAverage());
    }
}
