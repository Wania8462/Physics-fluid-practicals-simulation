using UnityEngine;
using UnityEngine.UI;

/// <summary>A framed graph of one or more lines whose newest points are at the right edge, drawn into a texture</summary>
public class LineGraph : RawImage
{
    private const int TextureWidth = 720, TextureHeight = 304;
    /// <summary>How far each line reaches from its middle, in texels</summary>
    private const int LineRadius = 1, FrameRadius = 1;

    /// <summary>Colour of the frame around the plot</summary>
    public Color32 frameColor = Color.black;
    /// <summary>The colour of each line in turn</summary>
    private static readonly Color32[] LineColors = { Color.red, Color.green };

    private Texture2D target;
    private Color32[] pixels;

    /// <summary>Draws these lines, each point being seconds before the newest (at most 0) and a value</summary>
    // ponytail: redraws and uploads the whole texture for every new sample, draw into it incrementally if it shows in the profiler
    public void Show(Vector2[][] lines, float seconds, float low, float high)
    {
        if (target == null)
        {
            target = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[TextureWidth * TextureHeight];
            texture = target;
        }

        System.Array.Clear(pixels, 0, pixels.Length);

        int right = TextureWidth - 1, top = TextureHeight - 1;
        DrawLine(0, 0, right, 0, FrameRadius, frameColor);
        DrawLine(0, top, right, top, FrameRadius, frameColor);
        DrawLine(0, 0, 0, top, FrameRadius, frameColor);
        DrawLine(right, 0, right, top, FrameRadius, frameColor);

        Vector2Int Plot(Vector2 point) => new(
            Mathf.RoundToInt((1 + point.x / seconds) * right),
            Mathf.RoundToInt((point.y - low) / (high - low) * top));

        for (int line = 0; line < lines.Length; line++)
        {
            Vector2[] points = lines[line];
            for (int i = 1; i < points.Length; i++)
            {
                Vector2Int start = Plot(points[i - 1]), end = Plot(points[i]);
                DrawLine(start.x, start.y, end.x, end.y, LineRadius, LineColors[line % LineColors.Length]);
            }
        }

        target.SetPixels32(pixels);
        target.Apply(false);
    }

    /// <summary>Stamps a square along a straight line between two texels</summary>
    private void DrawLine(int startX, int startY, int endX, int endY, int radius, Color32 lineColour)
    {
        int steps = Mathf.Max(Mathf.Abs(endX - startX), Mathf.Abs(endY - startY), 1);

        for (int step = 0; step <= steps; step++)
        {
            int x = startX + (endX - startX) * step / steps;
            int y = startY + (endY - startY) * step / steps;

            for (int stampY = Mathf.Max(y - radius, 0); stampY <= Mathf.Min(y + radius, TextureHeight - 1); stampY++)
                for (int stampX = Mathf.Max(x - radius, 0); stampX <= Mathf.Min(x + radius, TextureWidth - 1); stampX++)
                    pixels[stampY * TextureWidth + stampX] = lineColour;
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Destroy(target);
    }
}
