using System;
using UnityEngine;

public enum Performace
{
    Low,
    Medium,
    High
}

public enum Theme
{
    Dark,
    Light
}

public enum Units
{
    UK,
    Metric,
    Imperial
}

public enum ColorblindFilter
{
    None,
    FilterA,
    FilterB
}

/// <summary>App-wide settings chosen on the settings screen, saved between runs by name</summary>
public static class AppSettings
{
    internal const string PerformanceKey = "Settings.Performance";
    internal const string ThemeKey = "Settings.Theme";
    internal const string UnitsKey = "Settings.Units";
    internal const string ColorblindFilterKey = "Settings.ColorblindFilter";

    internal const Performace DefaultPerformance = Performace.Low;
    internal const Theme DefaultTheme = Theme.Dark;
    internal const Units DefaultUnits = Units.UK;
    internal const ColorblindFilter DefaultColorblindFilter = ColorblindFilter.None;

    public static Performace performace = DefaultPerformance;
    public static Theme theme = DefaultTheme;
    public static Units units = DefaultUnits;
    public static ColorblindFilter colorblindFilter = DefaultColorblindFilter;

    /// <summary>Reads the saved settings. A missing or unreadable one goes back to its default</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Load()
    {
        performace = Read(PerformanceKey, DefaultPerformance);
        theme = Read(ThemeKey, DefaultTheme);
        units = Read(UnitsKey, DefaultUnits);
        colorblindFilter = Read(ColorblindFilterKey, DefaultColorblindFilter);
    }

    /// <summary>Saves every setting</summary>
    public static void Save()
    {
        PlayerPrefs.SetString(PerformanceKey, performace.ToString());
        PlayerPrefs.SetString(ThemeKey, theme.ToString());
        PlayerPrefs.SetString(UnitsKey, units.ToString());
        PlayerPrefs.SetString(ColorblindFilterKey, colorblindFilter.ToString());
        PlayerPrefs.Save();
    }

    /// <summary>The saved enum value's name, or fallback if it's missing or names no value</summary>
    private static T Read<T>(string key, T fallback) where T : struct, Enum
    {
        // TryParse also takes numbers such as "99", so IsDefined rules out values the enum doesn't have
        return Enum.TryParse(PlayerPrefs.GetString(key, ""), out T value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
    }
}
