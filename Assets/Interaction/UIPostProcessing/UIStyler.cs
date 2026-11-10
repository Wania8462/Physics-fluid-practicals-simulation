using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Gives the UI the theme's look. Runs when a scene loads, when a window opens and when the theme changes</summary>
public static class UIStyler
{
    /// <summary>How many theme units fit across the screen. A window is 400 of them wide</summary>
    private const float UnitsAcrossScreen = 2854.5f;
    /// <summary>A radius big enough to make the short sides of any image fully round</summary>
    private const float Pill = float.PositiveInfinity;
    /// <summary>How far a control's colour moves towards the text colour under the mouse and when pressed</summary>
    private const float HoverBlend = 0.1f, PressBlend = 0.2f;
    private const float TintFade = 0.1f, DisabledAlpha = 0.4f, SelectionAlpha = 0.35f;
    /// <summary>A loose text this much smaller than the usual size on its page is a caption</summary>
    private const float CaptionBelow = 0.7f;
    /// <summary>A gap this many times the usual one in a column of buttons separates two groups</summary>
    private const float GroupGap = 1.5f;
    /// <summary>Room a dropdown's option needs, in text heights</summary>
    private const float ItemHeight = 1.45f;
    /// <summary>The smallest a widget's text shrinks to fit its box, as a part of its size</summary>
    private const float SmallestFit = 0.5f;

    /// <summary>A button labelled with just this gets a drawn arrow, this much of the button's shorter side across</summary>
    private const string BackArrowText = "←";
    private const float BackArrowFill = 0.6f;

    private enum TextRole
    {
        Title,
        Main,
        Caption
    }

    /// <summary>Parts of a canvas that are drawn bigger, and how many times bigger</summary>
    private static readonly Dictionary<Transform, float> pages = new();
    /// <summary>What each loose text was found to be when first seen, so restyling doesn't change it</summary>
    private static readonly Dictionary<TMP_Text, TextRole> roles = new();
    /// <summary>Graphics a widget has styled in this pass, so they aren't taken for panels</summary>
    private static readonly HashSet<Graphic> claimed = new();
    /// <summary>Whether the canvas being styled is a full-screen page</summary>
    private static bool canvasIsPage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void StyleEveryScene()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyAll();
    }

    /// <summary>Draws this part of a canvas scale times bigger than a window</summary>
    public static void MarkPage(Transform page, float scale)
    {
        pages[page] = scale;
    }

    /// <summary>Styles every canvas, and the background of the screens that have a plain one</summary>
    public static void ApplyAll()
    {
        foreach (Transform gone in pages.Keys.Where(page => !page).ToList())
            pages.Remove(gone);
        foreach (TMP_Text gone in roles.Keys.Where(text => !text).ToList())
            roles.Remove(gone);

        foreach (Camera camera in Camera.allCameras)
        {
            if (camera.clearFlags == CameraClearFlags.SolidColor)
                camera.backgroundColor = UITheme.Colors.background;
        }

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas)
                Apply(canvas.transform);
        }
    }

    /// <summary>Styles everything under root, which has to be inside a canvas</summary>
    public static void Apply(Transform root)
    {
        Canvas canvas = root.GetComponentInParent<Canvas>(true);
        canvasIsPage = canvas && !canvas.rootCanvas.GetComponentInChildren<WindowManager>(true);
        claimed.Clear();

        // Fields set their text sizes before the widgets are styled, as a dropdown's rows fit their text
        StyleTexts(root);
        foreach (FieldUI field in root.GetComponentsInChildren<FieldUI>(true))
            field.ApplyTheme();
        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
            StyleSelectable(selectable);
        StylePanels(root);
        EqualiseGaps(root);

        // Windows go last, as their title bar buttons differ from other buttons
        foreach (Window window in root.GetComponentsInChildren<Window>(true))
            window.ApplyTheme();
    }

    // ---------- Sizes ----------

    /// <summary>How many screen pixels one of this object's own units covers</summary>
    private static float PixelsPerUnit(Transform transform)
    {
        Canvas canvas = transform.GetComponentInParent<Canvas>(true);
        Transform top = canvas ? canvas.rootCanvas.transform : null;
        float scale = canvas ? canvas.rootCanvas.scaleFactor : 1;

        for (; transform && transform != top; transform = transform.parent)
            scale *= transform.localScale.x;
        return scale > 0 ? scale : 1;
    }

    /// <summary>How many screen pixels a theme unit covers here, which is more on a page</summary>
    private static float UnitPixels(Transform transform)
    {
        Transform page = PageRoot(transform);
        float scale = page ? pages[page] : canvasIsPage ? UITheme.Current.pageScale : 1;
        return Screen.width / UnitsAcrossScreen * scale;
    }

    /// <summary>The marked page this is inside, null when it's in none</summary>
    private static Transform PageRoot(Transform transform)
    {
        for (; transform; transform = transform.parent)
        {
            if (pages.ContainsKey(transform))
                return transform;
        }
        return null;
    }

    /// <summary>A size in theme units as this object's own units</summary>
    private static float Local(Transform transform, float size)
    {
        return size * UnitPixels(transform) / PixelsPerUnit(transform);
    }

    /// <summary>The shorter side of a graphic, in screen pixels</summary>
    private static float ShortSidePixels(Graphic graphic)
    {
        Rect rect = graphic.rectTransform.rect;
        return Mathf.Min(rect.width, rect.height) * PixelsPerUnit(graphic.transform);
    }

    /// <summary>A corner radius in theme units as screen pixels, no more than half the image's shorter side</summary>
    private static int RadiusPixels(Image image, float radius)
    {
        return Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(radius * UnitPixels(image.transform), ShortSidePixels(image) / 2)));
    }

    /// <summary>Whether a window or a field looks after this itself</summary>
    private static bool Owned(Component component)
    {
        return component.GetComponentInParent<Window>(true) || component.GetComponentInParent<FieldUI>(true);
    }

    // ---------- Shapes and colours ----------

    /// <summary>Gives an image round corners that are the same size on screen everywhere</summary>
    /// <param name="radius">In theme units</param>
    public static void Round(Image image, float radius, Corners corners = Corners.All)
    {
        Slice(image, UISprites.Rounded(RadiusPixels(image, radius), corners));
    }

    /// <summary>Shows a sprite sliced, with each of its pixels covering one screen pixel</summary>
    public static void Slice(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = PixelsPerUnit(image.transform);
    }

    /// <summary>A colour moved a little towards the text colour, which shows on dark and on light</summary>
    public static Color Towards(Color color, float blend)
    {
        Color text = UITheme.Colors.textPrimary;
        return new Color(Mathf.Lerp(color.r, text.r, blend), Mathf.Lerp(color.g, text.g, blend), Mathf.Lerp(color.b, text.b, blend), 1);
    }

    /// <summary>Makes a control this colour, a little different under the mouse and when pressed</summary>
    public static void Tint(Selectable selectable, Color normal)
    {
        Tint(selectable, normal, Towards(normal, HoverBlend), Towards(normal, PressBlend), normal);
    }

    /// <summary>Sets the colours a control fades between. Its graphic turns white so they show as they are</summary>
    public static void Tint(Selectable selectable, Color normal, Color hover, Color pressed, Color selected)
    {
        if (selectable.targetGraphic)
        {
            selectable.targetGraphic.color = Color.white;
            claimed.Add(selectable.targetGraphic);
        }

        ColorBlock block = selectable.colors;
        block.normalColor = normal;
        block.highlightedColor = hover;
        block.pressedColor = pressed;
        block.selectedColor = selected;
        block.disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * DisabledAlpha);
        block.colorMultiplier = 1;
        block.fadeDuration = TintFade;

        selectable.transition = Selectable.Transition.ColorTint;
        selectable.colors = block;
    }

    /// <summary>Shows a glyph sprite in an image, drawn at the image's size on screen</summary>
    private static void SetGlyph(Image image, System.Func<int, Sprite> draw, Color color)
    {
        image.sprite = draw(Mathf.Max(1, Mathf.RoundToInt(ShortSidePixels(image))));
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = color;
        claimed.Add(image);
    }

    // ---------- Widgets ----------

    private static void StyleSelectable(Selectable selectable)
    {
        switch (selectable)
        {
            case Button button: StyleButton(button); break;
            case Slider slider: StyleSlider(slider); break;
            case Toggle toggle when toggle.GetComponentInParent<TMP_Dropdown>(true): StyleDropdownItem(toggle); break;
            case Toggle toggle: StyleToggle(toggle); break;
            case TMP_Dropdown dropdown: StyleDropdown(dropdown); break;
            case TMP_InputField input: StyleInput(input); break;
            case Scrollbar scrollbar: StyleScrollbar(scrollbar); break;

            // A widget there's no rule for yet shows in the missing colour. A bare Selectable is a hit area, left as it is
            default:
                if (selectable.GetType() != typeof(Selectable) && selectable.targetGraphic)
                    selectable.targetGraphic.color = UITheme.Colors.missing;
                if (selectable.targetGraphic)
                    claimed.Add(selectable.targetGraphic);
                break;
        }
    }

    private static void StyleButton(Button button)
    {
        // A button with no transition is an invisible hit area
        if (button.transition == Selectable.Transition.None || button.targetGraphic is not Image image)
            return;

        Round(image, UITheme.Current.controlRadius);
        Tint(button, UITheme.Colors.control);

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label && label.text.Trim() == BackArrowText)
            ShowBackArrow(button, label);
    }

    /// <summary>Swaps a back button's arrow character for a drawn arrow in the middle of the button</summary>
    private static void ShowBackArrow(Button button, TMP_Text label)
    {
        const string IconName = "Back arrow";
        label.enabled = false;

        Transform icon = button.transform.Find(IconName);
        if (!icon)
        {
            icon = new GameObject(IconName, typeof(RectTransform), typeof(Image)).transform;
            icon.SetParent(button.transform, false);
        }

        Rect space = ((RectTransform)button.transform).rect;
        ((RectTransform)icon).sizeDelta = Vector2.one * Mathf.Min(space.width, space.height) * BackArrowFill;

        Image image = icon.GetComponent<Image>();
        image.raycastTarget = false;
        SetGlyph(image, UISprites.BackArrow, UITheme.Colors.textPrimary);
    }

    /// <summary>A thin track, filled in the accent colour up to a round knob</summary>
    private static void StyleSlider(Slider slider)
    {
        Palette colors = UITheme.Colors;
        Image track = slider.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(image => image.rectTransform != slider.fillRect && image.rectTransform != slider.handleRect);

        if (track)
        {
            Round(track, Pill);
            track.color = colors.border;
            claimed.Add(track);
        }

        if (slider.fillRect && slider.fillRect.TryGetComponent(out Image fill))
        {
            Round(fill, Pill);
            fill.color = colors.accent;
            claimed.Add(fill);
        }

        if (slider.handleRect && slider.handleRect.TryGetComponent(out Image knob))
        {
            Round(knob, Pill);
            slider.targetGraphic = knob;
            Tint(slider, colors.textPrimary, colors.accent, colors.accent, colors.textPrimary);
        }
    }

    /// <summary>A rounded box that fills with the accent colour, with a tick, when it's on</summary>
    private static void StyleToggle(Toggle toggle)
    {
        if (toggle.targetGraphic is not Image box)
            return;

        Round(box, UITheme.Current.controlRadius);
        Tint(toggle, UITheme.Colors.control);

        if (toggle.graphic is not Image mark)
            return;

        RectTransform rect = mark.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        int radius = RadiusPixels(box, UITheme.Current.controlRadius);
        SetGlyph(mark, size => UISprites.CheckBox(size, radius), UITheme.Colors.accent);
    }

    /// <summary>A row of a dropdown's list: clear until the mouse is over it, with a tick by the chosen one</summary>
    private static void StyleDropdownItem(Toggle item)
    {
        Color control = UITheme.Colors.control;

        if (item.targetGraphic is Image back)
        {
            back.sprite = null;
            Color hover = Towards(control, HoverBlend);
            Tint(item, new Color(control.r, control.g, control.b, 0), hover, Towards(control, PressBlend), hover);
        }

        if (item.graphic is Image tick)
            SetGlyph(tick, UISprites.Tick, UITheme.Colors.accent);
    }

    private static void StyleDropdown(TMP_Dropdown dropdown)
    {
        Palette colors = UITheme.Colors;

        if (dropdown.targetGraphic is Image box)
        {
            Round(box, UITheme.Current.controlRadius);
            Tint(dropdown, colors.control);
        }

        Transform arrow = dropdown.transform.Find("Arrow");
        if (arrow && arrow.TryGetComponent(out Image arrowImage))
            SetGlyph(arrowImage, UISprites.Chevron, colors.textSecondary);

        if (!dropdown.template)
            return;

        if (dropdown.template.TryGetComponent(out Image list))
        {
            Round(list, UITheme.Current.controlRadius);
            list.color = colors.control;
            claimed.Add(list);
        }

        // An option's row grows to fit its text, and the list's content with it
        Toggle item = dropdown.template.GetComponentInChildren<Toggle>(true);
        if (item && dropdown.itemText)
        {
            RectTransform row = (RectTransform)item.transform, content = (RectTransform)row.parent;
            float textSize = dropdown.itemText.enableAutoSizing ? dropdown.itemText.fontSizeMax : dropdown.itemText.fontSize;
            float extra = textSize * ItemHeight - row.rect.height;
            if (extra > 0)
            {
                row.sizeDelta += new Vector2(0, extra);
                content.sizeDelta += new Vector2(0, extra);
            }
        }
    }

    private static void StyleInput(TMP_InputField input)
    {
        Palette colors = UITheme.Colors;

        if (input.targetGraphic is Image box)
        {
            Round(box, UITheme.Current.controlRadius);
            Color focused = Towards(colors.control, HoverBlend);
            Tint(input, colors.control, focused, focused, focused);
        }

        input.customCaretColor = true;
        input.caretColor = colors.textPrimary;
        input.selectionColor = new Color(colors.accent.r, colors.accent.g, colors.accent.b, SelectionAlpha);
        if (input.placeholder)
            input.placeholder.color = colors.textSecondary;
    }

    /// <summary>No visible track, just a thin round handle</summary>
    private static void StyleScrollbar(Scrollbar scrollbar)
    {
        Palette colors = UITheme.Colors;

        if (scrollbar.TryGetComponent(out Image track))
        {
            track.color = Color.clear;
            claimed.Add(track);
        }

        if (scrollbar.handleRect && scrollbar.handleRect.TryGetComponent(out Image handle))
        {
            Round(handle, Pill);
            scrollbar.targetGraphic = handle;
            Tint(scrollbar, colors.border, colors.textSecondary, colors.textSecondary, colors.border);
        }
    }

    // ---------- Texts ----------

    /// <summary>Gives every text the theme's font and colour, and the size of what it is: a title, main text or a caption</summary>
    private static void StyleTexts(Transform root)
    {
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        FindRoles(texts);

        foreach (TMP_Text text in texts)
        {
            text.color = UITheme.Colors.textPrimary;

            // A button with several texts draws a picture with them, like the menu button's three lines, so they keep their shape
            Button button = text.GetComponentInParent<Button>(true);
            if (button && button.GetComponentsInChildren<TMP_Text>(true).Length > 1)
                continue;

            text.font = UITheme.FontAsset;

            // Windows and fields size their own texts
            if (Owned(text))
                continue;

            bool loose = roles.TryGetValue(text, out TextRole role);
            float size = Local(text.transform, loose ? Size(role) : UITheme.Current.sizeMain);

            // A widget's text shrinks to stay inside its box, a loose text is always its full size
            text.enableAutoSizing = !loose;
            text.fontSizeMax = size;
            text.fontSizeMin = size * SmallestFit;
            text.fontSize = size;
        }
    }

    private static float Size(TextRole role)
    {
        UITheme theme = UITheme.Current;
        return role == TextRole.Title ? theme.sizeTitle : role == TextRole.Caption ? theme.sizeCaption : theme.sizeMain;
    }

    /// <summary>
    /// Works out what each new loose text is from the sizes it was given in the scene: the single largest
    /// on its page is the title, ones much smaller than the usual size are captions, the rest are main text
    /// </summary>
    private static void FindRoles(TMP_Text[] texts)
    {
        IEnumerable<TMP_Text> loose = texts.Where(text => !roles.ContainsKey(text) && !Owned(text) && !text.GetComponentInParent<Selectable>(true));

        foreach (IGrouping<Transform, TMP_Text> page in loose.GroupBy(text => PageRoot(text.transform) ?? text.GetComponentInParent<Canvas>(true).rootCanvas.transform))
        {
            List<(TMP_Text text, float size)> sized = page
                .Select(text => (text, size: Mathf.Round(text.fontSize * PixelsPerUnit(text.transform))))
                .OrderBy(entry => entry.size)
                .ToList();

            float largest = sized[^1].size;
            bool hasTitle = sized.Count(entry => entry.size == largest) == 1;
            float usual = sized
                .Take(hasTitle ? sized.Count - 1 : sized.Count)
                .GroupBy(entry => entry.size)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Key)
                .Select(group => group.Key)
                .FirstOrDefault();

            foreach ((TMP_Text text, float size) in sized)
            {
                roles[text] = hasTitle && size == largest ? TextRole.Title
                    : size < CaptionBelow * usual ? TextRole.Caption
                    : TextRole.Main;
            }
        }
    }

    // ---------- Panels and layout ----------

    /// <summary>
    /// Any visible image no widget has claimed is a panel. One with a sprite gets round corners,
    /// one without stays square, as a panel against the screen's edge should
    /// </summary>
    private static void StylePanels(Transform root)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (claimed.Contains(image) || image.color.a == 0 || image.GetComponent<Mask>() || Owned(image))
                continue;

            if (image.sprite)
                Round(image, UITheme.Current.radius);
            image.color = UITheme.Colors.panel;
        }
    }

    /// <summary>
    /// Spaces the buttons stacked in a column evenly between its first and last.
    /// A much larger gap is meant, so the buttons on each side of it are spaced on their own
    /// </summary>
    private static void EqualiseGaps(Transform root)
    {
        IEnumerable<RectTransform> buttons = root.GetComponentsInChildren<Button>(true)
            .Where(button => !Owned(button))
            .Select(button => (RectTransform)button.transform);

        foreach (IGrouping<(Transform, float), RectTransform> column in buttons.GroupBy(rect => (rect.parent, Mathf.Round(rect.anchoredPosition.x))))
        {
            List<RectTransform> stacked = column.OrderByDescending(rect => rect.anchoredPosition.y).ToList();
            if (stacked.Count < 3)
                continue;

            List<float> gaps = Enumerable.Range(1, stacked.Count - 1)
                .Select(i => stacked[i - 1].anchoredPosition.y - stacked[i].anchoredPosition.y)
                .ToList();
            float usual = gaps.OrderBy(gap => gap).ElementAt(gaps.Count / 2);

            int first = 0;
            for (int i = 1; i <= stacked.Count; i++)
            {
                if (i < stacked.Count && gaps[i - 1] <= GroupGap * usual)
                    continue;

                SpaceEvenly(stacked, first, i - 1);
                first = i;
            }
        }
    }

    /// <summary>Moves the buttons between two of a column so they're all the same distance apart</summary>
    private static void SpaceEvenly(List<RectTransform> stacked, int first, int last)
    {
        float top = stacked[first].anchoredPosition.y, bottom = stacked[last].anchoredPosition.y;

        for (int i = first + 1; i < last; i++)
        {
            RectTransform rect = stacked[i];
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, Mathf.Lerp(top, bottom, (float)(i - first) / (last - first)));
        }
    }
}
