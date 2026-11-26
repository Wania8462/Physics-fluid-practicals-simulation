using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static PlayModeHelpers;

/// <summary>The settings screen: changing a control changes the setting and saves it</summary>
public class SettingsSceneTests
{
    private string savedUnits, savedPerformance;
    private Units units;
    private Performace performance;

    [UnitySetUp]
    public IEnumerator LoadSettings()
    {
        savedUnits = PlayerPrefs.GetString(AppSettings.UnitsKey, null);
        savedPerformance = PlayerPrefs.GetString(AppSettings.PerformanceKey, null);
        units = AppSettings.units;
        performance = AppSettings.performace;
        yield return LoadScene("SettingsScene");
    }

    [TearDown]
    public void Restore()
    {
        RestoreKey(AppSettings.UnitsKey, savedUnits);
        RestoreKey(AppSettings.PerformanceKey, savedPerformance);
        PlayerPrefs.Save();
        AppSettings.units = units;
        AppSettings.performace = performance;
    }

    private static void RestoreKey(string key, string value)
    {
        if (value == null)
            PlayerPrefs.DeleteKey(key);
        else
            PlayerPrefs.SetString(key, value);
    }

    /// <summary>A private control of the settings screen</summary>
    private static T Control<T>(string field)
    {
        return (T)typeof(SettingsScript).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Find<SettingsScript>());
    }

    /// <summary>Choosing imperial units sets and saves them</summary>
    [UnityTest]
    public IEnumerator UnitsDropdown_SetsAndSaves()
    {
        TMP_Dropdown dropdown = Control<TMP_Dropdown>("unitsDropdown");
        dropdown.value = (int)Units.Imperial;
        yield return null;

        Assert.AreEqual(Units.Imperial, AppSettings.units);
        Assert.AreEqual("Imperial", PlayerPrefs.GetString(AppSettings.UnitsKey));
    }

    /// <summary>The performance slider only stops at its levels, and each is saved</summary>
    [UnityTest]
    public IEnumerator PerformanceSlider_SetsAndSaves()
    {
        Slider slider = Control<Slider>("slider");
        Assert.AreEqual(0, slider.minValue);
        Assert.AreEqual(System.Enum.GetValues(typeof(Performace)).Length - 1, slider.maxValue, "The slider has a level the settings don't");

        slider.value = (int)Performace.High;
        yield return null;
        Assert.AreEqual(Performace.High, AppSettings.performace);
        Assert.AreEqual("High", PlayerPrefs.GetString(AppSettings.PerformanceKey));
    }

    /// <summary>The screen opens showing the saved settings</summary>
    [UnityTest]
    public IEnumerator Opens_ShowingCurrentSettings()
    {
        Assert.AreEqual((int)AppSettings.units, Control<TMP_Dropdown>("unitsDropdown").value);
        Assert.AreEqual((int)AppSettings.performace, (int)Control<Slider>("slider").value);
        yield return null;
    }
}
