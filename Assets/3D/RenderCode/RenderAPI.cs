using System;
using UnityEngine;

/// <summary>The UI's way into the renderers</summary>
public class RenderAPI : MonoBehaviour
{
    [SerializeField] private Visualiser visualiser;

    /// <summary>Whether the force's arrows are drawn</summary>
    public bool IsForceDrawn(BodyForce force)
    {
        return visualiser.shown[(int)force];
    }

    /// <summary>Draws the force as an arrow on every body, or stops drawing it</summary>
    public void DrawForce(BodyForce force, bool draw)
    {
        visualiser.shown[(int)force] = draw;
    }

    /// <summary>Arrow length per unit of sim force</summary>
    public float GetArrowScale()
    {
        return visualiser.arrowScale;
    }

    /// <summary>Sets the arrow length per unit of sim force</summary>
    public void SetArrowScale(float scale)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The arrow scale has to be above 0.");

        visualiser.arrowScale = scale;
    }

    /// <summary>Whether the force values are written next to the arrows</summary>
    public bool AreForceValuesDrawn()
    {
        return visualiser.showValues;
    }

    /// <summary>Writes each drawn force's size next to its arrow, or stops</summary>
    public void DrawForceValues(bool draw)
    {
        visualiser.showValues = draw;
    }

    /// <summary>Null draws nothing for the scale</summary>
    public void SetScaleText(int scale, string text)
    {
        visualiser.scaleTexts[scale] = text;
    }

    /// <summary>Also shows the scale's text floating over it</summary>
    public void ShowScaleLabel(int scale, bool show)
    {
        visualiser.scaleLabelsShown[scale] = show;
    }

    /// <summary>SimulationAPI.ForceScale, so the values read in newtons</summary>
    public void SetNewtonScale(float forcePerNewton)
    {
        if (forcePerNewton <= 0)
            throw new ArgumentOutOfRangeException(nameof(forcePerNewton), forcePerNewton, "The newton scale has to be above 0.");

        visualiser.forcePerNewton = forcePerNewton;
    }
}
