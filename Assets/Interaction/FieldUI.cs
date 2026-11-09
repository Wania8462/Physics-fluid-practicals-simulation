using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The UI of one window field</summary>
public class FieldUI : MonoBehaviour
{
    /// <summary>Height of a separator field, in pixels</summary>
    public const int SeparatorHeight = 16;
    private const float SeparatorMargin = 16, SeparatorThickness = 2;

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
    private Image separator;
    private Button foldoutButton;

    /// <summary>Raised with the field's index and new value when the user edits it</summary>
    public event Action<int, Field> Changed;

    private void Awake()
    {
        slider.onValueChanged.AddListener(SetValue);
        input.onEndEdit.AddListener(OnInputEntered);
        dropdown.onValueChanged.AddListener(OnDropdownChanged);
        toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    /// <summary>Shows a field. index is its position in the window</summary>
    public void Bind(Field field, int index)
    {
        this.field = field;
        this.index = index;
        label.text = LabelText();

        bool isSeparator = field.fieldType == FieldType.Separator;
        if (isSeparator && separator == null)
            separator = CreateSeparator();
        if (separator != null)
            separator.gameObject.SetActive(isSeparator);

        bool isFoldout = field.fieldType == FieldType.Foldout;
        if (isFoldout && foldoutButton == null)
            foldoutButton = CreateFoldoutButton();
        if (foldoutButton != null)
            foldoutButton.gameObject.SetActive(isFoldout);

        // Nothing sits beside a foldout's label, so it can use the whole row
        if (isFoldout)
        {
            RectTransform labelRect = label.rectTransform;
            labelRect.sizeDelta = new Vector2(((RectTransform)transform).sizeDelta.x - 2 * labelRect.anchoredPosition.x, labelRect.sizeDelta.y);
        }

        // A readout looks like a numeric field the user can't type into
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
        else if (field.fieldType == FieldType.Toggle)
        {
            toggle.SetIsOnWithoutNotify(field.isOn);
        }
    }

    /// <summary>A thin line across the middle of the separator, in the label's colour</summary>
    private Image CreateSeparator()
    {
        Image line = new GameObject("Separator", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        line.raycastTarget = false;
        line.color = new Color(label.color.r, label.color.g, label.color.b, 0.5f);

        RectTransform rect = line.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(-2 * SeparatorMargin, SeparatorThickness);
        rect.anchoredPosition = new Vector2(0, -SeparatorHeight / 2f);
        return line;
    }

    /// <summary>The field's name, with an arrow showing whether a foldout is open</summary>
    private string LabelText()
    {
        if (field.fieldType == FieldType.Foldout)
            return (field.isOn ? "▼ " : "► ") + field.name;

        return field.name ?? "";
    }

    /// <summary>An invisible button covering the whole row</summary>
    private Button CreateFoldoutButton()
    {
        Button button = new GameObject("Foldout", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OnFoldoutClicked);

        Image hitArea = button.GetComponent<Image>();
        hitArea.color = Color.clear;

        RectTransform rect = hitArea.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(0, field.GetHeight());
        rect.anchoredPosition = Vector2.zero;
        return button;
    }

    private void OnFoldoutClicked()
    {
        field.isOn = !field.isOn;
        label.text = LabelText();
        Changed?.Invoke(index, field);
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
