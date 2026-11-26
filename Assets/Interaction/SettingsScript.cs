using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Settings screen: ties its controls to AppSettings</summary>
public class SettingsScript : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Dropdown themeDropdown;
    [SerializeField] private TMP_Dropdown unitsDropdown;
    [SerializeField] private TMP_Dropdown colorblindDropdown;

    void Start()
    {
        slider.value = (int)AppSettings.performace;
        themeDropdown.value = (int)AppSettings.theme;
        unitsDropdown.value = (int)AppSettings.units;
        colorblindDropdown.value = (int)AppSettings.colorblindFilter;

        slider.onValueChanged.AddListener(OnSliderChanged);
        themeDropdown.onValueChanged.AddListener(OnThemeChanged);
        unitsDropdown.onValueChanged.AddListener(OnUnitsChanged);
        colorblindDropdown.onValueChanged.AddListener(OnColorblindChanged);
    }

    private void OnSliderChanged(float value)
    {
        AppSettings.performace = (Performace)(int)value;
        AppSettings.Save();
    }

    private void OnThemeChanged(int index)
    {
        AppSettings.theme = (Theme)index;
        UIStyler.ApplyAll();
        AppSettings.Save();
    }

    private void OnUnitsChanged(int index)
    {
        AppSettings.units = (Units)index;
        AppSettings.Save();
    }

    private void OnColorblindChanged(int index)
    {
        AppSettings.colorblindFilter = (ColorblindFilter)index;
        AppSettings.Save();
    }
}
