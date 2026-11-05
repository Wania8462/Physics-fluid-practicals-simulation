using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

/// <summary>Timings of one named block of code</summary>
public class CallStats
{
    private List<long> calls = new();

    public void AddCall(long call)
    {
        calls.Add(call);
    }

    public int Count => calls.Count;

    public double GetAverage() => (int)calls.Average();
    public double GetSum() => calls.Sum();
    public string GetStats() => $"Calls: {calls.Count}, Avg: {calls.Average()}";
}

/// <summary>Times named blocks of code. Names start with a number, which orders the logs</summary>
public static class Watcher
{
    private static ConcurrentDictionary<string, CallStats> _stats = new();

    /// <summary>Runs action and records how long it took under name</summary>
    public static void ExecuteWithTimer(string name, Action action)
    {
        var sw = new Stopwatch();
        sw.Start();
        action();
        sw.Stop();
        _stats.GetOrAdd(name, new CallStats()).AddCall(sw.ElapsedTicks);
    }

    /// <summary>Calls recorded for the first block</summary>
    public static int Count => _stats.First().Value.Count;

    /// <summary>Average time of every block that took any time, in order</summary>
    public static string LogImportant()
    {
        var sb = new StringBuilder();
        var sortedStats = _stats.OrderBy(x => int.Parse(string.Concat(x.Key.TakeWhile(char.IsDigit))));

        foreach (var stat in sortedStats)
        {
            if (stat.Value.GetAverage() > 0)
                sb.AppendLine(stat.Key + ": " + stat.Value.GetAverage());
        }

        return sb.ToString();
    }

    /// <summary>Average time of every block, in order</summary>
    public static string Log()
    {
        var sb = new StringBuilder();
        var sortedStats = _stats.OrderBy(x => int.Parse(string.Concat(x.Key.TakeWhile(char.IsDigit))));

        foreach (var stat in sortedStats)
            sb.AppendLine(stat.Key + ": " + stat.Value.GetAverage());

        return sb.ToString();
    }

    /// <summary>Average time of the first block, usually the whole step</summary>
    public static double GetTotal()
    {
        var first = _stats.OrderBy(x => int.Parse(string.Concat(x.Key.TakeWhile(char.IsDigit)))).First();
        return first.Value.GetAverage();
    }

    public static void Reset()
    {
        _stats.Clear();
    }
}