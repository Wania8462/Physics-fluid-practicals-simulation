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

        // A readout is the numeric field's top row, its value shown in the input field the user can't type into
        bool numeric = field.fieldType == FieldType.Numeric;
        bool hasValue = numeric || field.fieldType == FieldType.Readout;
        input.gameObject.SetActive(hasValue);
        input.interactable = numeric;
        unit.gameObject.SetActive(hasValue);
        slider.gameObject.SetActive(numeric);
        minText.gameObject.SetActive(numeric);
        maxText.gameObject.SetActive(numeric);
        dropdown.gameObject.SetActive(field.fieldType == FieldType.Dropdown);
        toggle.gameObject.SetActive(field.fieldType == FieldType.Toggle);

        if (hasValue)
            unit.text = field.unit;

        if (field.fieldType == FieldType.Readout)
        {
            input.SetTextWithoutNotify(field.value.ToString("0.0"));
        }
        else if (numeric)
        {
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
