using System;
using UnityEngine;

// The UI's only way into the renderers, as SimulationAPI is into the simulation
public class RenderAPI : MonoBehaviour
{
    [SerializeField] private Visualiser visualiser;

    public bool IsForceDrawn(BodyForce force)
    {
        return visualiser.shown[(int)force];
    }

    // Draws the force as an arrow on every body, or stops drawing it
    public void DrawForce(BodyForce force, bool draw)
    {
        visualiser.shown[(int)force] = draw;
    }

    public float GetArrowScale()
    {
        return visualiser.arrowScale;
    }

    // Arrow length per unit of sim force
    public void SetArrowScale(float scale)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The arrow scale has to be above 0.");

        visualiser.arrowScale = scale;
    }

    public bool AreForceValuesDrawn()
    {
        return visualiser.showValues;
    }

    // Writes each drawn force's size at the middle of its arrow
    public void DrawForceValues(bool draw)
    {
        visualiser.showValues = draw;
    }

    // Writes the text on the sides of the scale, and in its label when that is shown. The renderer places both where the
    // scale is. Null draws nothing for the scale
    public void SetScaleText(int scale, string text)
    {
        visualiser.scaleTexts[scale] = text;
    }

    // Shows the scale's text as a label over its pan, like the force values
    public void ShowScaleLabel(int scale, bool show)
    {
        visualiser.scaleLabelsShown[scale] = show;
    }

    // The sim force of 1 N, SimulationAPI.ForceScale, so the values read in newtons
    public void SetNewtonScale(float forcePerNewton)
    {
        if (forcePerNewton <= 0)
            throw new ArgumentOutOfRangeException(nameof(forcePerNewton), forcePerNewton, "The newton scale has to be above 0.");

        visualiser.forcePerNewton = forcePerNewton;
    }
}
