using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Kinds of field a window can show</summary>
public enum FieldType
{
    Numeric,
    Dropdown,
    Toggle,
    Readout,
    Separator,
    Foldout,
    Graph,
    Buttons
}

/// <summary>One field of a window</summary>
[Serializable]
public struct Field
{
    public string name;
    public FieldType fieldType;

    // Numeric and Readout
    public float value;
    public float min;
    public float max;
    public string unit;
    /// <summary>Readout: how the value is written, null for one decimal at most</summary>
    public string format;

    // Dropdown, and Buttons where selected is the button last pressed
    public string[] options;
    public int selected;

    // Toggle, and whether a Foldout is open
    public bool isOn;

    // Foldout: how many of the fields after it are inside
    public int foldedFields;

    // Graph: the lines drawn, each point being seconds before the newest (at most 0) and a value. Also uses unit
    public Vector2[][] graphLines;
    public float graphSeconds;
    /// <summary>The vertical axis never covers less than this, so noise isn't blown up</summary>
    public float graphMinRange;

    /// <summary>Height of the field's UI, in pixels</summary>
    public readonly int GetHeight()
    {
        return fieldType switch
        {
            FieldType.Numeric => 110,
            FieldType.Dropdown => 100,
            FieldType.Toggle => 56,
            FieldType.Readout => 56,
            FieldType.Separator => FieldUI.SeparatorHeight,
            FieldType.Foldout => 56,
            FieldType.Graph => FieldUI.GraphHeight,
            FieldType.Buttons => 56,
            _ => throw new Exception($"There is no height for {fieldType}"),
        };
    }
}

/// <summary>Draggable window of fields. Dropping one window on another groups them</summary>
public class Window : MonoBehaviour
{
    private const float HoverGrowth = 1.05f;
    private const float DoubleClickTime = 0.3f;
    /// <summary>Empty space under a window's contents, in pixels</summary>
    private const float BottomPadding = 10;

    [SerializeField] private RectTransform top;
    [SerializeField] private RectTransform background;
    [SerializeField] private FieldUI fieldPrefab;
    [SerializeField] private TMP_InputField title;

    /// <summary>The window's fields. Call UpdateFields after changing the list</summary>
    public List<Field> fields = new();
    private readonly List<FieldUI> fieldUIs = new();

    /// <summary>Raised once per edited field per frame, after the simulation steps</summary>
    public event Action<int> FieldChanged;
    /// <summary>Raised in LateUpdate when the window closes</summary>
    public event Action Closed;

    /// <summary>Raised when this window is released over another one</summary>
    public event Action<Window> DroppedOn;

    private readonly HashSet<int> changed = new();
    private bool closing;

    [SerializeField] private bool topBarHeld;
    private Vector3 mouseOffset;
    private Vector3 pressPosition;
    /// <summary>False until the mouse moves past the drag threshold, so clicks don't ungroup</summary>
    private bool dragging;
    private Window hovered;
    private bool highlighted;
    private float lastTopBarClick;

    /// <summary>The window's title</summary>
    public string Title { set => title.text = value; }

    /// <summary>Whether the user can rename the window</summary>
    public bool Renamable { set => title.interactable = value; }

    /// <summary>A group holds other windows instead of fields</summary>
    public bool IsGroup { get; private set; }

    /// <summary>Only groupable windows can be dropped onto each other</summary>
    public bool Groupable { get; set; }

    private void Update()
    {
        if (!topBarHeld)
            return;

        if (!dragging)
        {
            if (Input.GetMouseButtonUp(0))
            {
                topBarHeld = false;
                return;
            }
            if (Vector3.Distance(Input.mousePosition, pressPosition) < EventSystem.current.pixelDragThreshold)
                return;

            dragging = true;
            Window outermost = GetComponentsInParent<Window>().Last();
            if (outermost != this)
                transform.SetParent(outermost.transform.parent, true);
        }

        transform.position = Input.mousePosition + mouseOffset;

        Window under = TopmostAt(transform.parent, Input.mousePosition, this);
        if (under && !(Groupable && under.Groupable))
            under = null;
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

    /// <summary>The window on top at a point. A point over a grouped window gives its group</summary>
    public static Window TopmostAt(Transform canvas, Vector2 point, Window except = null)
    {
        for (int i = canvas.childCount - 1; i >= 0; i--)
        {
            if (canvas.GetChild(i).TryGetComponent(out Window window) && window != except && window.Bounds().Contains(point))
                return window;
        }
        return null;
    }

    /// <summary>The window on screen, without its body when minimised</summary>
    private Rect Bounds()
    {
        Vector3[] bar = new Vector3[4], body = new Vector3[4];
        top.GetWorldCorners(bar);
        (background.gameObject.activeSelf ? background : top).GetWorldCorners(body);
        return Rect.MinMaxRect(bar[0].x, body[0].y, bar[2].x, bar[1].y);
    }

    /// <summary>Grows the window slightly to show a dragged window can be dropped on it</summary>
    private void Highlight(bool on)
    {
        if (on == highlighted)
            return;

        highlighted = on;
        transform.localScale *= on ? HoverGrowth : 1 / HoverGrowth;
    }

    /// <summary>Adds a window to this group, placed by the mouse position</summary>
    public void Add(Window window)
    {
        int index = background.Cast<Transform>().Count(member => member.position.y > Input.mousePosition.y);

        IsGroup = true;
        window.transform.SetParent(background, true);
        window.transform.SetSiblingIndex(index);
        background.gameObject.SetActive(true);
    }

    /// <summary>Stacks the members down the group. A group down to one member dissolves</summary>
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

    /// <summary>Closing a group closes its members too</summary>
    private void RaiseClosed()
    {
        foreach (Transform child in background)
        {
            if (child.TryGetComponent(out Window member))
                member.RaiseClosed();
        }
        Closed?.Invoke();
    }

    /// <summary>Rebuilds the field UI from fields</summary>
    public void UpdateFields()
    {
        foreach (Transform child in background)
            Destroy(child.gameObject);
        fieldUIs.Clear();

        for (int i = 0; i < fields.Count; i++)
        {
            FieldUI ui = Instantiate(fieldPrefab, background);
            ui.Bind(fields[i], i);
            ui.Changed += (index, f) =>
            {
                fields[index] = f;
                if (f.fieldType == FieldType.Foldout)
                    LayoutFields();
                else
                    changed.Add(index);
            };
            fieldUIs.Add(ui);
        }

        LayoutFields();
    }

    /// <summary>Stacks the fields down the window, leaving out those in a closed foldout</summary>
    private void LayoutFields()
    {
        int totalHeight = 0;
        int hiddenUntil = 0;

        for (int i = 0; i < fields.Count; i++)
        {
            bool shown = i >= hiddenUntil;
            fieldUIs[i].gameObject.SetActive(shown);
            if (!shown)
                continue;

            ((RectTransform)fieldUIs[i].transform).anchoredPosition = new Vector2(0, -totalHeight);
            totalHeight += fields[i].GetHeight();

            if (fields[i].fieldType == FieldType.Foldout && !fields[i].isOn)
                hiddenUntil = i + 1 + fields[i].foldedFields;
        }

        ResizeBackground(totalHeight);
    }

    /// <summary>False when a closed foldout, a minimised window or a minimised group hides the field</summary>
    public bool IsFieldVisible(int index)
    {
        return fieldUIs[index].gameObject.activeInHierarchy;
    }

    /// <summary>Doesn't raise FieldChanged</summary>
    public void SetField(int index, Field field)
    {
        fields[index] = field;
        fieldUIs[index].Bind(field, index);
    }

    /// <summary>Sets a toggle from code, without raising FieldChanged</summary>
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
        background.sizeDelta = new Vector2(background.sizeDelta.x, totalHeight + BottomPadding);
    }

    /// <summary>Closes the window at the end of the frame</summary>
    public void Close()
    {
        closing = true;
    }

    /// <summary>Hides or shows the fields</summary>
    public void ToggleMinimize()
    {
        background.gameObject.SetActive(!background.gameObject.activeSelf);
    }

    /// <summary>Starts dragging the window. A double click renames it</summary>
    public void OnTopBarClick()
    {
        if (Time.unscaledTime - lastTopBarClick < DoubleClickTime)
            title.Select();
        lastTopBarClick = Time.unscaledTime;

        topBarHeld = true;
        dragging = false;
        pressPosition = Input.mousePosition;
        mouseOffset = transform.position - pressPosition;
    }
}
