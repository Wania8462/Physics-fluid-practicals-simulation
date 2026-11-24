using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>The colours of one theme</summary>
[Serializable]
public class Palette
{
    /// <summary>Shown on anything the styler has no rule for, so it stands out</summary>
    public Color missing = Rgb(0xFF00DC);
    /// <summary>Behind everything on the menu screens</summary>
    public Color background = Rgb(0x121212);
    /// <summary>Windows, the side menu and cards</summary>
    public Color panel = Rgb(0x1C1C1C);
    /// <summary>Buttons, text boxes, dropdowns and check boxes</summary>
    public Color control = Rgb(0x2A2A2A);
    /// <summary>Thin lines: separators, window outlines, the slider's track</summary>
    public Color border = Rgb(0x3A3A3A);
    public Color titleBar = Rgb(0xFB8500);
    /// <summary>What's filled in or switched on, and the outline of a drop target</summary>
    public Color accent = Rgb(0xFFA136);
    /// <summary>Text on the title bar and on the accent colour</summary>
    public Color onAccent = Rgb(0x191610);
    public Color textPrimary = Rgb(0xE6E9EF);
    public Color textSecondary = Rgb(0x8B93A5);
    public Color textDisabled = Rgb(0x4B5263);
    public Color warning = Rgb(0xFFCC00);
    public Color error = Rgb(0xEF4444);

    /// <summary>The light theme's colours</summary>
    public static Palette Light()
    {
        return new Palette
        {
            background = Rgb(0xFEFEFE),
            panel = Rgb(0xFAFAFA),
            control = Rgb(0xECEEF2),
            border = Rgb(0xD9DCE3),
            textPrimary = Rgb(0x191610),
            textSecondary = Rgb(0x5B6373),
            textDisabled = Rgb(0xB4BAC6),
        };
    }

    /// <param name="hex">The colour as 0xRRGGBB</param>
    private static Color Rgb(int hex)
    {
        return new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);
    }
}

/// <summary>
/// The look of the whole UI. Edit it on the UITheme prefab in Resources, changes show when a scene loads.
/// Sizes are in window units: a window is 400 wide
/// </summary>
public class UITheme : MonoBehaviour
{
    /// <summary>Text needs at least this much contrast against what's behind it to read easily</summary>
    private const float MinContrast = 4.5f;

    public Palette darkColors = new();
    public Palette lightColors = Palette.Light();

    /// <summary>Roboto when left empty</summary>
    [Header("Text")]
    public Font font;
    public float sizeTitle = 28;
    public float sizeMain = 22;
    public float sizeCaption = 18;
    /// <summary>How many times bigger everything in the side menu is than in a window</summary>
    public float sideMenuScale = 1.7f;

    /// <summary>Corners of windows and cards</summary>
    [Header("Shapes")]
    public float radius = 8;
    /// <summary>Corners of buttons, text boxes and dropdowns</summary>
    public float controlRadius = 6;
    /// <summary>Space around and between the rows of a window</summary>
    public float spacing = 8;
    /// <summary>How far a window's shadow spreads</summary>
    public float shadow = 4;
    /// <summary>How many times bigger everything is on the menus, the settings screen and the settings card</summary>
    public float pageScale = 2.5f;

    /// <summary>Seconds a window takes to open or close</summary>
    [Header("Motion")]
    public float fadeTime = 0.2f;
    /// <summary>Seconds a drop target takes to grow or shrink</summary>
    public float growTime = 0.25f;
    /// <summary>How much bigger a drop target gets</summary>
    public float growth = 1.07f;

    private static UITheme current;
    private static TMP_FontAsset fontAsset;

    /// <summary>The theme from the prefab in Resources, or the defaults in code when it's missing</summary>
    public static UITheme Current
    {
        get
        {
            if (!current)
                current = Resources.Load<UITheme>("UITheme");
            if (!current)
            {
                current = new GameObject("UITheme") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<UITheme>();
                DontDestroyOnLoad(current.gameObject);
            }
            return current;
        }
    }

    /// <summary>The colours of the theme picked in the settings</summary>
    public static Palette Colors => AppSettings.theme == Theme.Light ? Current.lightColors : Current.darkColors;

    /// <summary>The theme's font as TextMeshPro needs it. Characters it lacks come from the default font</summary>
    public static TMP_FontAsset FontAsset
    {
        get
        {
            if (fontAsset)
                return fontAsset;

            Font source = Current.font ? Current.font : Resources.Load<Font>("Roboto-Regular");
            fontAsset = source ? TMP_FontAsset.CreateFontAsset(source) : null;
            if (fontAsset)
                fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            else
                fontAsset = TMP_Settings.defaultFontAsset;
            return fontAsset;
        }
    }

    /// <summary>Warns in the console about text colours that are hard to read on what's behind them</summary>
    private void OnValidate()
    {
        foreach ((string theme, Palette colors) in new[] { ("dark", darkColors), ("light", lightColors) })
        {
            (string, Color, string, Color)[] pairs =
            {
                ("textPrimary", colors.textPrimary, "background", colors.background),
                ("textPrimary", colors.textPrimary, "panel", colors.panel),
                ("textPrimary", colors.textPrimary, "control", colors.control),
                ("textSecondary", colors.textSecondary, "panel", colors.panel),
                ("onAccent", colors.onAccent, "titleBar", colors.titleBar),
                ("onAccent", colors.onAccent, "accent", colors.accent),
            };

            foreach ((string text, Color fore, string surface, Color back) in pairs)
            {
                float contrast = Contrast(fore, back);
                if (contrast < MinContrast)
                    Debug.LogWarning($"UITheme {theme}: {text} on {surface} has a contrast of {contrast:0.0}:1, under {MinContrast}:1 it's hard to read", this);
            }
        }
    }

    /// <returns>From 1 (the same colour) to 21 (black on white)</returns>
    private static float Contrast(Color a, Color b)
    {
        float brighter = Mathf.Max(Brightness(a), Brightness(b));
        float darker = Mathf.Min(Brightness(a), Brightness(b));
        return (brighter + 0.05f) / (darker + 0.05f);
    }

    /// <summary>How bright a colour looks to the eye, from 0 to 1</summary>
    private static float Brightness(Color color)
    {
        Color linear = color.linear;
        return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
    }
}
