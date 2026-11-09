using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>The UI of one window field</summary>
public class FieldUI : MonoBehaviour
{
    /// <summary>Height of a separator field, in pixels</summary>
    public const int SeparatorHeight = 16;
    private const float SeparatorMargin = 16, SeparatorThickness = 2;

    /// <summary>Height of a graph field, in pixels</summary>
    public const int GraphHeight = 230;
    /// <summary>Room above the plot for the field's name and below it for the time axis, in pixels</summary>
    private const float GraphTop = 48, GraphBottom = 30;
    private const float GraphTextScale = 0.6f;

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
    private LineGraph graph;
    private TMP_Text graphHigh, graphLow, graphOldest;
    private GameObject buttonRow;
    private TMP_Text[] buttonTexts;

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

        bool isGraph = field.fieldType == FieldType.Graph;
        if (isGraph && graph == null)
            CreateGraph();
        if (graph != null)
            graph.gameObject.SetActive(isGraph);
        if (isGraph)
            ShowGraph();

        bool isButtons = field.fieldType == FieldType.Buttons;
        if (isButtons && buttonRow == null)
            CreateButtons();
        if (buttonRow != null)
            buttonRow.SetActive(isButtons);
        if (isButtons)
        {
            for (int i = 0; i < buttonTexts.Length; i++)
                buttonTexts[i].text = field.options[i];
        }

        // Nothing sits beside a foldout's or graph's label, so it can use the whole row
        if (isFoldout || isGraph)
        {
            RectTransform labelRect = label.rectTransform;
            labelRect.sizeDelta = new Vector2(((RectTransform)transform).sizeDelta.x - 2 * labelRect.anchoredPosition.x, labelRect.sizeDelta.y);
        }

        bool numeric = field.fieldType == FieldType.Numeric;
        bool readout = field.fieldType == FieldType.Readout;
        input.gameObject.SetActive(numeric);
        unit.gameObject.SetActive(numeric || readout);
        slider.gameObject.SetActive(numeric);
        minText.gameObject.SetActive(numeric);
        maxText.gameObject.SetActive(numeric);
        dropdown.gameObject.SetActive(field.fieldType == FieldType.Dropdown);
        toggle.gameObject.SetActive(field.fieldType == FieldType.Toggle);

        if (readout)
        {
            ShowReadout();
        }
        else if (numeric)
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

    /// <summary>One button per option, sharing a row equally and styled like the text input</summary>
    private void CreateButtons()
    {
        const float RowHeight = 40, RowTop = 8, Gap = 8;

        RectTransform row = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(transform, false);
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.sizeDelta = new Vector2(-2 * SeparatorMargin, RowHeight);
        row.anchoredPosition = new Vector2(0, -RowTop);
        buttonRow = row.gameObject;

        Image inputImage = input.GetComponent<Image>();
        int count = field.options.Length;
        buttonTexts = new TMP_Text[count];

        for (int i = 0; i < count; i++)
        {
            Button button = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();

            Image image = button.GetComponent<Image>();
            image.sprite = inputImage.sprite;
            image.type = inputImage.type;
            image.color = inputImage.color;

            RectTransform rect = image.rectTransform;
            rect.SetParent(row, false);
            rect.anchorMin = new Vector2((float)i / count, 0);
            rect.anchorMax = new Vector2((float)(i + 1) / count, 1);
            rect.offsetMin = new Vector2(Gap / 2, 0);
            rect.offsetMax = new Vector2(-Gap / 2, 0);

            TMP_Text text = Instantiate(label, rect);
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            buttonTexts[i] = text;

            int pressed = i;
            button.onClick.AddListener(() =>
            {
                field.selected = pressed;
                Changed?.Invoke(index, field);

                // A selected button would also be pressed by Space, the pause key
                EventSystem.current.SetSelectedGameObject(null);
            });
        }
    }

    /// <summary>The plot and its axis labels, in the label's colour and font</summary>
    private void CreateGraph()
    {
        graph = new GameObject("Graph", typeof(RectTransform), typeof(LineGraph)).GetComponent<LineGraph>();
        graph.raycastTarget = false;
        graph.frameColor = label.color;

        RectTransform rect = graph.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(-2 * SeparatorMargin, GraphHeight - GraphTop - GraphBottom);
        rect.anchoredPosition = new Vector2(0, -GraphTop);

        graphHigh = CreateGraphLabel(new Vector2(0, 1), new Vector2(4, -2));
        graphLow = CreateGraphLabel(new Vector2(0, 0), new Vector2(4, 2));
        graphOldest = CreateGraphLabel(new Vector2(0, 0), new Vector2(0, -GraphBottom + 2));
        CreateGraphLabel(new Vector2(1, 0), new Vector2(0, -GraphBottom + 2)).text = "now";
    }

    /// <summary>A small text at a corner of the plot, aligned towards that corner's side</summary>
    private TMP_Text CreateGraphLabel(Vector2 corner, Vector2 offset)
    {
        TMP_Text text = Instantiate(label, graph.transform);
        text.fontSize = label.fontSize * GraphTextScale;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.horizontalAlignment = corner.x == 0 ? HorizontalAlignmentOptions.Left : HorizontalAlignmentOptions.Right;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = corner;
        rect.anchorMax = corner;
        rect.pivot = corner;
        rect.sizeDelta = new Vector2(150, 26);
        rect.anchoredPosition = offset;
        return text;
    }

    /// <summary>Fits the vertical axis to every line being shown and draws them</summary>
    private void ShowGraph()
    {
        Vector2[][] lines = field.graphLines ?? Array.Empty<Vector2[]>();
        float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;

        foreach (Vector2[] line in lines)
        {
            foreach (Vector2 point in line)
            {
                lowest = Mathf.Min(lowest, point.y);
                highest = Mathf.Max(highest, point.y);
            }
        }

        if (lowest > highest)
            lowest = highest = 0;

        // A little space so the line doesn't touch the frame
        float middle = (lowest + highest) / 2;
        float halfRange = 0.55f * Mathf.Max(highest - lowest, field.graphMinRange);
        float low = middle - halfRange, high = middle + halfRange;

        graph.Show(lines, field.graphSeconds, low, high);
        graphHigh.text = $"{high:0.#} {field.unit}";
        graphLow.text = $"{low:0.#} {field.unit}";
        graphOldest.text = $"-{field.graphSeconds:0} s";
    }

    /// <summary>Writes the value and unit as one plain text against the right edge, as large as the name</summary>
    private void ShowReadout()
    {
        RectTransform labelRect = label.rectTransform;
        RectTransform unitRect = unit.rectTransform;
        float margin = labelRect.anchoredPosition.x;

        label.textWrappingMode = TextWrappingModes.NoWrap;
        float labelWidth = label.GetPreferredValues().x;
        labelRect.sizeDelta = new Vector2(labelWidth, labelRect.sizeDelta.y);

        float left = margin + labelWidth + margin;
        unit.fontSize = label.fontSize;
        unit.textWrappingMode = TextWrappingModes.NoWrap;
        unit.horizontalAlignment = HorizontalAlignmentOptions.Right;
        unitRect.anchoredPosition = new Vector2(left, unitRect.anchoredPosition.y);
        unitRect.sizeDelta = new Vector2(((RectTransform)transform).sizeDelta.x - left - margin, unitRect.sizeDelta.y);

        string gap = field.unit == "%" ? "" : " ";
        unit.text = $"{field.value.ToString(field.format ?? "0.#")}{gap}{field.unit}";
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
