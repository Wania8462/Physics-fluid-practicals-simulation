using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which corners of a rounded sprite are round</summary>
[Flags]
public enum Corners
{
    Top = 1,
    Bottom = 2,
    All = Top | Bottom
}

/// <summary>
/// Draws the UI's shapes into white sprites and keeps them. Sizes are in screen pixels,
/// so each sprite is as sharp as it can be at the size it's shown
/// </summary>
// ponytail: sprites keep the size they were drawn at when the screen resolution changes, restyle on a resize if that shows
public static class UISprites
{
    private static readonly Dictionary<(string shape, int size, int detail, Corners corners), Sprite> cache = new();

    // The glyphs' corner points, in a square that goes from 0 to 1
    private static readonly Vector2[] TickPoints = { new(0.22f, 0.52f), new(0.42f, 0.32f), new(0.78f, 0.7f) };
    private static readonly Vector2[] ChevronPoints = { new(0.28f, 0.6f), new(0.5f, 0.4f), new(0.72f, 0.6f) };
    // The shaft out to the tip, then each side of the head
    private static readonly Vector2[] BackArrowPoints = { new(0.8f, 0.5f), new(0.22f, 0.5f), new(0.48f, 0.76f), new(0.22f, 0.5f), new(0.48f, 0.24f) };
    private const float TickWidth = 0.12f, ChevronWidth = 0.1f, BackArrowWidth = 0.15f;

    /// <summary>A filled box with round corners, to be drawn sliced</summary>
    public static Sprite Rounded(int radius, Corners corners = Corners.All)
    {
        int size = 2 * radius + 2;
        return Get(("rounded", radius, 0, corners), size, radius,
            (x, y) => Inside(BoxDistance(x, y, size, 0, radius, corners)));
    }

    /// <summary>Only the edge of a rounded box, to be drawn sliced without its centre</summary>
    public static Sprite Outline(int radius, int width)
    {
        int border = Mathf.Max(radius, width);
        int size = 2 * border + 2;
        return Get(("outline", radius, width, Corners.All), size, border, (x, y) =>
        {
            float distance = BoxDistance(x, y, size, 0, radius, Corners.All);
            return Inside(distance) * Inside(-distance - width);
        });
    }

    /// <summary>A rounded box that fades out over blur pixels around it, to be drawn sliced and that much bigger than what casts it</summary>
    public static Sprite Shadow(int radius, int blur)
    {
        int border = radius + blur;
        int size = 2 * border + 2;
        return Get(("shadow", radius, blur, Corners.All), size, border, (x, y) =>
        {
            float fade = 1 - Mathf.Clamp01(BoxDistance(x, y, size, blur, radius, Corners.All) / blur);
            return fade * fade;
        });
    }

    /// <summary>A tick</summary>
    public static Sprite Tick(int size)
    {
        return Get(("tick", size, 0, Corners.All), size, 0, (x, y) => Stroke(x, y, size, TickWidth, TickPoints));
    }

    /// <summary>A thick arrow pointing left</summary>
    public static Sprite BackArrow(int size)
    {
        return Get(("backArrow", size, 0, Corners.All), size, 0, (x, y) => Stroke(x, y, size, BackArrowWidth, BackArrowPoints));
    }

    /// <summary>An arrowhead pointing down</summary>
    public static Sprite Chevron(int size)
    {
        return Get(("chevron", size, 0, Corners.All), size, 0, (x, y) => Stroke(x, y, size, ChevronWidth, ChevronPoints));
    }

    /// <summary>A filled rounded box with a tick cut out of it, so what's behind shows through the tick</summary>
    public static Sprite CheckBox(int size, int radius)
    {
        return Get(("checkbox", size, radius, Corners.All), size, 0,
            (x, y) => Inside(BoxDistance(x, y, size, 0, radius, Corners.All)) * (1 - Stroke(x, y, size, TickWidth, TickPoints)));
    }

    /// <summary>The kept sprite, or a new square one drawn from how solid each pixel is</summary>
    /// <param name="border">Pixels on every side that keep their size when the sprite is sliced</param>
    /// <param name="alphaAt">From 0 to 1, for a pixel's middle</param>
    private static Sprite Get((string, int, int, Corners) key, int size, int border, Func<float, float, float> alphaAt)
    {
        if (cache.TryGetValue(key, out Sprite kept) && kept)
            return kept;

        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f))));

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, Vector4.one * border);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        cache[key] = sprite;
        return sprite;
    }

    /// <summary>How much of a pixel is inside a shape, given how far its middle is outside the edge</summary>
    private static float Inside(float distance)
    {
        return Mathf.Clamp01(0.5f - distance);
    }

    /// <summary>How far a point is outside a box centred in the texture, negative when inside</summary>
    /// <param name="inset">How far the box's sides are from the texture's</param>
    private static float BoxDistance(float x, float y, int size, float inset, float radius, Corners corners)
    {
        float half = size / 2f - inset;
        float fromMiddleX = Mathf.Abs(x - size / 2f), fromMiddleY = y - size / 2f;
        bool round = (corners & (fromMiddleY > 0 ? Corners.Top : Corners.Bottom)) != 0;
        float corner = round ? radius : 0;

        float pastX = fromMiddleX - half + corner, pastY = Mathf.Abs(fromMiddleY) - half + corner;
        float outside = new Vector2(Mathf.Max(pastX, 0), Mathf.Max(pastY, 0)).magnitude;
        return outside + Mathf.Min(Mathf.Max(pastX, pastY), 0) - corner;
    }

    /// <summary>How much of a pixel a round-ended line through these points covers</summary>
    /// <param name="width">Of the line, as a part of the sprite's size</param>
    private static float Stroke(float x, float y, int size, float width, Vector2[] points)
    {
        Vector2 point = new(x, y);
        float nearest = float.PositiveInfinity;

        for (int i = 1; i < points.Length; i++)
        {
            Vector2 start = points[i - 1] * size, along = (points[i] - points[i - 1]) * size;
            float part = Mathf.Clamp01(Vector2.Dot(point - start, along) / along.sqrMagnitude);
            nearest = Mathf.Min(nearest, (point - start - along * part).magnitude);
        }

        return Inside(nearest - width * size / 2);
    }
}
