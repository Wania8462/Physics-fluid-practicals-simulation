using System;
using System.Collections;
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
        return FieldUI.HeightOf(fieldType);
    }
}

/// <summary>Draggable window of fields. Dropping one window on another groups them</summary>
public class Window : MonoBehaviour
{
    /// <summary>How far a group's frame reaches past its members on each side, in pixels</summary>
    private const float GroupPadding = 10;
    /// <summary>The size a window opens from and closes to, and the size from which it's fully opaque. 1 is its own size</summary>
    private const float Collapsed = 0.05f, Solid = 0.6f;
    private const float DoubleClickTime = 0.3f;
    /// <summary>Empty space under a window's contents, in pixels</summary>
    private const float BottomPadding = 10;
    /// <summary>How dark a title bar button gets under the mouse and when pressed</summary>
    private const float ButtonHover = 0.15f, ButtonPress = 0.3f;
    /// <summary>The most of the title bar's height the title's text takes</summary>
    private const float TitleFill = 0.85f;

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
    /// <summary>How much bigger than its own size the window is drawn right now</summary>
    private float growth = 1;
    private Coroutine growing;
    private CanvasGroup fade;
    private float lastTopBarClick;

    /// <summary>The window's title</summary>
    public string Title { set => title.text = value; }

    /// <summary>Whether the user can rename the window</summary>
    public bool Renamable { set => title.interactable = value; }

    /// <summary>A group holds other windows instead of fields</summary>
    public bool IsGroup { get; private set; }

    /// <summary>Only groupable windows can be dropped onto each other</summary>
    public bool Groupable { get; set; }

    private void Awake()
    {
        // Invisible until Start, by when the fields have been built and styled at the window's real size
        fade = gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0;
        UIStyler.Apply(transform);
    }

    private void Start()
    {
        SetGrowth(Collapsed);
        growing = StartCoroutine(GrowTo(1, UITheme.Current.fadeTime));
    }

    /// <summary>Gives the title bar and the body the theme's shapes and colours. The fields style themselves</summary>
    public void ApplyTheme()
    {
        UITheme theme = UITheme.Current;
        Palette colors = UITheme.Colors;

        RoundTitleBar();
        top.GetComponent<Image>().color = colors.titleBar;

        Image body = background.GetComponent<Image>();
        UIStyler.Round(body, theme.radius, Corners.Bottom);
        body.color = IsGroup ? colors.border : colors.panel;

        title.textComponent.color = colors.onAccent;
        title.textComponent.fontSize = Mathf.Min(theme.sizeMain, ((RectTransform)title.transform).rect.height * TitleFill);
        title.caretColor = colors.onAccent;

        // Minimise and close are clear until the mouse is over them
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.transform.parent != transform)
                continue;

            UIStyler.Tint(button, Color.clear, new Color(0, 0, 0, ButtonHover), new Color(0, 0, 0, ButtonPress), Color.clear);
            foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
                CentreSymbol(text, colors.onAccent);
        }
    }

    /// <summary>
    /// Puts a title bar button's symbol in the middle of the button, as large as the title.
    /// The minimise underscore becomes a dash, which sits at mid height in any font
    /// </summary>
    private void CentreSymbol(TMP_Text symbol, Color color)
    {
        if (symbol.text == "_")
            symbol.text = "–";

        symbol.color = color;
        symbol.fontSize = title.textComponent.fontSize;
        symbol.horizontalAlignment = HorizontalAlignmentOptions.Center;
        symbol.verticalAlignment = VerticalAlignmentOptions.Middle;

        RectTransform rect = symbol.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    /// <summary>The title bar is round on top, and all round when the body under it is hidden</summary>
    private void RoundTitleBar()
    {
        bool open = background.gameObject.activeSelf;
        UIStyler.Round(top.GetComponent<Image>(), UITheme.Current.radius, open ? Corners.Top : Corners.All);
    }

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

        // What the window owns is cleaned up right away, only the look of it lingers while it shrinks
        if (closing)
        {
            RaiseClosed();
            enabled = false;
            fade.blocksRaycasts = false;
            if (growing != null)
                StopCoroutine(growing);
            StartCoroutine(ShrinkAway());
        }
    }

    private IEnumerator ShrinkAway()
    {
        yield return GrowTo(Collapsed, UITheme.Current.fadeTime);
        Destroy(gameObject);
    }

    /// <summary>The window on top at a point. A point over a grouped window gives its group</summary>
    public static Window TopmostAt(Transform canvas, Vector2 point, Window except = null)
    {
        for (int i = canvas.childCount - 1; i >= 0; i--)
        {
            // A window that's shrinking away is switched off
            if (canvas.GetChild(i).TryGetComponent(out Window window) && window.enabled && window != except && window.Bounds().Contains(point))
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
        if (growing != null)
            StopCoroutine(growing);
        growing = StartCoroutine(GrowTo(on ? UITheme.Current.growth : 1, UITheme.Current.growTime));
    }

    /// <summary>Eases the window to a size, fast at first and settling gently</summary>
    /// <param name="target">1 is the window's own size</param>
    /// <param name="seconds">How long it takes</param>
    private IEnumerator GrowTo(float target, float seconds)
    {
        float from = growth;

        for (float progress = 0; progress < 1; progress += Time.unscaledDeltaTime / seconds)
        {
            float left = 1 - progress;
            SetGrowth(Mathf.Lerp(from, target, 1 - left * left * left));
            yield return null;
        }

        SetGrowth(target);
    }

    /// <summary>Scales the window from whatever it is now, so a move into or out of a group in mid-growth doesn't upset it</summary>
    private void SetGrowth(float value)
    {
        transform.localScale *= value / growth;
        growth = value;
        fade.alpha = Mathf.InverseLerp(Collapsed, Solid, value);
    }

    /// <summary>Makes the title bar and body wider than the members, so the group shows as a frame around them</summary>
    private void BecomeGroup()
    {
        IsGroup = true;
        top.sizeDelta += new Vector2(2 * GroupPadding, 0);
        background.sizeDelta += new Vector2(2 * GroupPadding, 0);
        background.GetComponent<Image>().color = UITheme.Colors.border;

        // Minimise and close stay against the title bar's right end
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.transform.parent == transform)
                ((RectTransform)button.transform).anchoredPosition += new Vector2(GroupPadding * top.localScale.x, 0);
        }
    }

    /// <summary>Adds a window to this group, placed by the mouse position</summary>
    public void Add(Window window)
    {
        int index = background.Cast<Transform>().Count(member => member.position.y > Input.mousePosition.y);

        if (!IsGroup)
            BecomeGroup();
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
        float y = corners[1].y - GroupPadding * background.lossyScale.y;

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
        RoundTitleBar();
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
