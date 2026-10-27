using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum FieldType
{
    Numeric,
    Dropdown,
    Toggle
}

[Serializable]
public struct Field
{
    public string name;
    public FieldType fieldType;

    // Numeric
    public float value;
    public float min;
    public float max;
    public string unit;

    // Dropdown
    public string[] options;
    public int selected;

    // Toggle
    public bool isOn;

    public readonly int GetHeight()
    {
        return fieldType switch
        {
            FieldType.Numeric => 110,
            FieldType.Dropdown => 100,
            FieldType.Toggle => 56,
            _ => throw new Exception($"There is no height for {fieldType}"),
        };
    }
}

public class Window : MonoBehaviour
{
    [SerializeField] private RectTransform background;
    [SerializeField] private FieldUI fieldPrefab;
    [SerializeField] private TMP_Text title;

    public List<Field> fields = new();
    private readonly List<FieldUI> fieldUIs = new();

    // Both are raised in LateUpdate, so handlers change the simulation after it has stepped this frame. FieldChanged is
    // raised once for each field edited this frame, however many edits it got
    public event Action<int> FieldChanged;
    public event Action Closed;

    private readonly HashSet<int> changed = new();
    private bool closing;

    [SerializeField] private bool topBarHeld;
    private Vector3 mouseOffset;

    public string Title { set => title.text = value; }

    private void Update()
    {
        if (Input.GetMouseButtonUp(0))
            topBarHeld = false;

        if (topBarHeld)
            OnTopBarHold();
    }

    private void LateUpdate()
    {
        foreach (int index in changed)
            FieldChanged?.Invoke(index);
        changed.Clear();

        if (closing)
        {
            Closed?.Invoke();
            Destroy(gameObject);
        }
    }

    public void UpdateFields()
    {
        foreach (Transform child in background)
            Destroy(child.gameObject);
        fieldUIs.Clear();

        int totalHeight = 0;

        for (int i = 0; i < fields.Count; i++)
        {
            FieldUI ui = Instantiate(fieldPrefab, background);
            ((RectTransform)ui.transform).anchoredPosition = new Vector2(0, -totalHeight);
            ui.Bind(fields[i], i);
            ui.Changed += (index, f) =>
            {
                fields[index] = f;
                changed.Add(index);
            };
            fieldUIs.Add(ui);

            totalHeight += fields[i].GetHeight();
        }

        ResizeBackground(totalHeight);
    }

    // Changes a field from code, without raising FieldChanged
    public void SetField(int index, Field field)
    {
        fields[index] = field;
        fieldUIs[index].Bind(field, index);
    }

    public void SetToggle(int index, bool isOn)
    {
        Field field = fields[index];
        field.isOn = isOn;
        SetField(index, field);
    }

    private void ResizeBackground(int totalHeight)
    {
        background.pivot = new Vector2(background.pivot.x, 1f);
        background.anchorMin = new Vector2(background.anchorMin.x, 1f);
        background.anchorMax = new Vector2(background.anchorMax.x, 1f);
        background.anchoredPosition = new Vector2(background.anchoredPosition.x, 0f);
        background.sizeDelta = new Vector2(background.sizeDelta.x, totalHeight);
    }

    public void Close()
    {
        closing = true;
    }

    // Field UI will live under background, so hiding it hides the fields too
    public void ToggleMinimize()
    {
        background.gameObject.SetActive(!background.gameObject.activeSelf);
    }

    public void OnTopBarClick()
    {
        topBarHeld = true;
        Vector3 mousePos = Input.mousePosition;
        mouseOffset = transform.position - mousePos;
    }

    private void OnTopBarHold()
    {
        transform.position = Input.mousePosition + mouseOffset;
    }
}
