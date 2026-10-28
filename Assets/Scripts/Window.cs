using System;
using System.Collections.Generic;
using System.Linq;
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
    private const float HoverGrowth = 1.05f;
    private const float DoubleClickTime = 0.3f;

    [SerializeField] private RectTransform top;
    [SerializeField] private RectTransform background;
    [SerializeField] private FieldUI fieldPrefab;
    [SerializeField] private TMP_InputField title;

    public List<Field> fields = new();
    private readonly List<FieldUI> fieldUIs = new();

    // Both are raised in LateUpdate, so handlers change the simulation after it has stepped this frame. FieldChanged is
    // raised once for each field edited this frame, however many edits it got
    public event Action<int> FieldChanged;
    public event Action Closed;

    // Raised when this window is released over another one
    public event Action<Window> DroppedOn;

    private readonly HashSet<int> changed = new();
    private bool closing;

    [SerializeField] private bool topBarHeld;
    private Vector3 mouseOffset;
    private Window hovered;
    private bool highlighted;
    private float lastTopBarClick;

    public string Title { set => title.text = value; }

    // The title is an input field styled as plain text, only editable on renamable windows
    public bool Renamable { set => title.interactable = value; }

    // A group's background holds member windows instead of fields
    public bool IsGroup { get; private set; }

    private void Update()
    {
        if (!topBarHeld)
            return;

        transform.position = Input.mousePosition + mouseOffset;

        Window under = TopmostAt(transform.parent, Input.mousePosition, this);
        if (under != hovered)
        {
            if (hovered)
                hovered.Highlight(false);
            if (under)
                under.Highlight(true);
            hovered = under;
        }

        if (Input.GetMouseButtonUp(0))
        {
            topBarHeld = false;
            if (hovered)
            {
                hovered.Highlight(false);
                DroppedOn?.Invoke(hovered);
            }
            hovered = null;
        }
    }

    private void LateUpdate()
    {
        foreach (int index in changed)
            FieldChanged?.Invoke(index);
        changed.Clear();

        if (IsGroup)
            LayoutMembers();

        if (closing)
        {
            RaiseClosed();
            Destroy(gameObject);
        }
    }

    // The top-level window drawn on top at the point, so a point over a grouped window gives its group
    public static Window TopmostAt(Transform canvas, Vector2 point, Window except = null)
    {
        for (int i = canvas.childCount - 1; i >= 0; i--)
        {
            if (canvas.GetChild(i).TryGetComponent(out Window window) && window != except && window.Bounds().Contains(point))
                return window;
        }
        return null;
    }

    // The whole window, its body only counting when it isn't minimised. World space is screen space on this overlay
    // canvas
    private Rect Bounds()
    {
        Vector3[] bar = new Vector3[4], body = new Vector3[4];
        top.GetWorldCorners(bar);
        (background.gameObject.activeSelf ? background : top).GetWorldCorners(body);
        return Rect.MinMaxRect(bar[0].x, body[0].y, bar[2].x, bar[1].y);
    }

    // Shows the dragged window can be dropped onto this one
    private void Highlight(bool on)
    {
        if (on == highlighted)
            return;

        highlighted = on;
        transform.localScale *= on ? HoverGrowth : 1 / HoverGrowth;
    }

    // Makes this window a group. The new member goes below every member whose top bar is above the mouse, so dropping
    // onto a group also reorders it
    public void Add(Window window)
    {
        int index = background.Cast<Transform>().Count(member => member.position.y > Input.mousePosition.y);

        IsGroup = true;
        window.transform.SetParent(background, true);
        window.transform.SetSiblingIndex(index);
        background.gameObject.SetActive(true);
    }

    // Stacks the members down the body in sibling order and fits the body around them. Done every frame, so members
    // being minimised, closed or dragged out are picked up. A group down to one member leaves it in its place, so the
    // group is shrunk first or the member would keep the hover growth
    private void LayoutMembers()
    {
        if (background.childCount <= 1)
        {
            if (background.childCount == 1)
            {
                Highlight(false);
                Transform last = background.GetChild(0);
                last.SetParent(transform.parent, true);
                last.SetSiblingIndex(transform.GetSiblingIndex());
            }

            closing = true;
            return;
        }

        Vector3[] corners = new Vector3[4];
        background.GetWorldCorners(corners);
        float y = corners[1].y;

        foreach (Transform member in background)
        {
            Rect bounds = member.GetComponent<Window>().Bounds();
            member.position += new Vector3(transform.position.x - member.position.x, y - bounds.yMax, 0);
            y -= bounds.height;
        }

        ResizeBackground((corners[1].y - y) / background.lossyScale.y);
    }

    // Members are destroyed with their group, and are raised from here since a minimised group's members never reach
    // LateUpdate
    private void RaiseClosed()
    {
        foreach (Transform child in background)
        {
            if (child.TryGetComponent(out Window member))
                member.RaiseClosed();
        }
        Closed?.Invoke();
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

    private void ResizeBackground(float totalHeight)
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

    // Dragging a member pulls it out of its group, next to the outermost group. The top bar covers the title, so a
    // double click on it starts renaming
    public void OnTopBarClick()
    {
        Window outermost = GetComponentsInParent<Window>().Last();
        if (outermost != this)
            transform.SetParent(outermost.transform.parent, true);

        if (Time.unscaledTime - lastTopBarClick < DoubleClickTime)
            title.Select();
        lastTopBarClick = Time.unscaledTime;

        topBarHeld = true;
        Vector3 mousePos = Input.mousePosition;
        mouseOffset = transform.position - mousePos;
    }
}
