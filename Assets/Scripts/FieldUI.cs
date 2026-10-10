using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FieldUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text unit;
    [SerializeField] private TMP_Text minText;
    [SerializeField] private TMP_Text maxText;
    [SerializeField] private TMP_InputField input;
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private Toggle toggle;

    private Field field;
    private int index;

    public event Action<int, Field> Changed;

    private void Awake()
    {
        slider.onValueChanged.AddListener(SetValue);
        input.onEndEdit.AddListener(OnInputEntered);
        dropdown.onValueChanged.AddListener(OnDropdownChanged);
        toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    public void Bind(Field field, int index)
    {
        this.field = field;
        this.index = index;
        label.text = field.name;

        bool numeric = field.fieldType == FieldType.Numeric;
        input.gameObject.SetActive(numeric);
        unit.gameObject.SetActive(numeric);
        slider.gameObject.SetActive(numeric);
        minText.gameObject.SetActive(numeric);
        maxText.gameObject.SetActive(numeric);
        dropdown.gameObject.SetActive(field.fieldType == FieldType.Dropdown);
        toggle.gameObject.SetActive(field.fieldType == FieldType.Toggle);

        if (numeric)
        {
            unit.text = field.unit;
            minText.text = field.min.ToString();
            maxText.text = field.max.ToString();
            slider.minValue = field.min;
            slider.maxValue = field.max;
            ShowValue(field.value);
        }
        else if (field.fieldType == FieldType.Dropdown)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(field.options));
            dropdown.SetValueWithoutNotify(field.selected);
        }
        else
        {
            toggle.SetIsOnWithoutNotify(field.isOn);
        }
    }

    private void OnInputEntered(string text)
    {
        if (float.TryParse(text, out float value))
            SetValue(value);
        else
            ShowValue(field.value);
    }

    private void SetValue(float value)
    {
        field.value = Mathf.Clamp(value, field.min, field.max);
        ShowValue(field.value);
        Changed?.Invoke(index, field);
    }

    private void ShowValue(float value)
    {
        slider.SetValueWithoutNotify(value);
        input.SetTextWithoutNotify(value.ToString("0.##"));
    }

    private void OnDropdownChanged(int value)
    {
        field.selected = value;
        Changed?.Invoke(index, field);
    }

    private void OnToggleChanged(bool isOn)
    {
        field.isOn = isOn;
        Changed?.Invoke(index, field);
    }
}
