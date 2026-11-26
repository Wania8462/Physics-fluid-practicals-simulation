using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>AppSettings: saving the settings by name and falling back to defaults for missing or corrupt values</summary>
public class AppSettingsTests
{
    private static readonly string[] Keys = { AppSettings.PerformanceKey, AppSettings.ThemeKey, AppSettings.UnitsKey, AppSettings.ColorblindFilterKey };

    /// <summary>The user's real saved values and settings, put back after each test</summary>
    private readonly Dictionary<string, string> savedPrefs = new();
    private (Performace, Theme, Units, ColorblindFilter) savedSettings;

    [SetUp]
    public void BackUp()
    {
        savedPrefs.Clear();
        foreach (string key in Keys)
        {
            if (PlayerPrefs.HasKey(key))
                savedPrefs[key] = PlayerPrefs.GetString(key);
            PlayerPrefs.DeleteKey(key);
        }
        savedSettings = (AppSettings.performace, AppSettings.theme, AppSettings.units, AppSettings.colorblindFilter);
    }

    [TearDown]
    public void Restore()
    {
        foreach (string key in Keys)
        {
            if (savedPrefs.TryGetValue(key, out string value))
                PlayerPrefs.SetString(key, value);
            else
                PlayerPrefs.DeleteKey(key);
        }
        PlayerPrefs.Save();
        (AppSettings.performace, AppSettings.theme, AppSettings.units, AppSettings.colorblindFilter) = savedSettings;
    }

    /// <summary>Puts every setting away from its default, so Load has to change them</summary>
    private static void SetAllAwayFromDefault()
    {
        AppSettings.performace = Performace.High;
        AppSettings.theme = Theme.Light;
        AppSettings.units = Units.Imperial;
        AppSettings.colorblindFilter = ColorblindFilter.FilterB;
    }

    private static void AssertDefaults()
    {
        Assert.AreEqual(AppSettings.DefaultPerformance, AppSettings.performace);
        Assert.AreEqual(AppSettings.DefaultTheme, AppSettings.theme);
        Assert.AreEqual(AppSettings.DefaultUnits, AppSettings.units);
        Assert.AreEqual(AppSettings.DefaultColorblindFilter, AppSettings.colorblindFilter);
    }

    /// <summary>Nothing saved loads the defaults</summary>
    [Test]
    public void Load_NothingSaved_Defaults()
    {
        SetAllAwayFromDefault();
        AppSettings.Load();
        AssertDefaults();
    }

    /// <summary>Saved settings load back as they were</summary>
    [Test]
    public void SaveThenLoad_RoundTrips()
    {
        SetAllAwayFromDefault();
        AppSettings.Save();
        AppSettings.performace = Performace.Low;
        AppSettings.units = Units.UK;

        AppSettings.Load();
        Assert.AreEqual(Performace.High, AppSettings.performace);
        Assert.AreEqual(Theme.Light, AppSettings.theme);
        Assert.AreEqual(Units.Imperial, AppSettings.units);
        Assert.AreEqual(ColorblindFilter.FilterB, AppSettings.colorblindFilter);
    }

    /// <summary>Every units and performance value survives a save and load</summary>
    [Test]
    public void SaveThenLoad_EveryValue()
    {
        foreach (Units units in System.Enum.GetValues(typeof(Units)))
        {
            AppSettings.units = units;
            AppSettings.Save();
            AppSettings.Load();
            Assert.AreEqual(units, AppSettings.units);
        }

        foreach (Performace performance in System.Enum.GetValues(typeof(Performace)))
        {
            AppSettings.performace = performance;
            AppSettings.Save();
            AppSettings.Load();
            Assert.AreEqual(performance, AppSettings.performace);
        }
    }

    /// <summary>Settings are saved by name, so reordering an enum can't change them</summary>
    [Test]
    public void Save_StoresNames()
    {
        AppSettings.units = Units.Imperial;
        AppSettings.performace = Performace.Medium;
        AppSettings.Save();

        Assert.AreEqual("Imperial", PlayerPrefs.GetString(AppSettings.UnitsKey));
        Assert.AreEqual("Medium", PlayerPrefs.GetString(AppSettings.PerformanceKey));
    }

    /// <summary>A saved value that isn't one of the names goes back to the default</summary>
    [TestCase("")]
    [TestCase("abc")]
    [TestCase("99")]
    [TestCase("-1")]
    [TestCase("Ultra")]
    [TestCase("   ")]
    public void Load_CorruptValue_Default(string corrupt)
    {
        foreach (string key in Keys)
            PlayerPrefs.SetString(key, corrupt);
        SetAllAwayFromDefault();

        AppSettings.Load();
        AssertDefaults();
    }

    /// <summary>A value saved as a number by something else goes back to the default</summary>
    [Test]
    public void Load_WrongType_Default()
    {
        foreach (string key in Keys)
            PlayerPrefs.SetInt(key, 2);
        SetAllAwayFromDefault();

        AppSettings.Load();
        AssertDefaults();
    }

    /// <summary>One corrupt setting doesn't reset the others</summary>
    [Test]
    public void Load_OneCorrupt_OthersKept()
    {
        AppSettings.units = Units.Metric;
        AppSettings.performace = Performace.High;
        AppSettings.Save();
        PlayerPrefs.SetString(AppSettings.PerformanceKey, "broken");

        AppSettings.Load();
        Assert.AreEqual(Units.Metric, AppSettings.units);
        Assert.AreEqual(AppSettings.DefaultPerformance, AppSettings.performace);
    }
}
