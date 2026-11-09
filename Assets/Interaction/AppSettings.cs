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

/// <summary>App-wide settings chosen on the settings screen</summary>
public static class AppSettings
{
    public static Performace performace = Performace.Low;
    public static Theme theme = Theme.Dark;
    public static Units units = Units.UK;
    public static ColorblindFilter colorblindFilter = ColorblindFilter.None;
}