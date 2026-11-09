using UnityEngine;

/// <summary>How the fluid is drawn</summary>
enum FluidRenderMode
{
    Billboards,
    ScreenSpace
}

/// <summary>Sets up and draws every renderer when the simulation asks</summary>
public class RenderManager : MonoBehaviour
{
    [SerializeField] private SimulationManager sim;
    [SerializeField] private EnvironmentRenderer environmentRenderer;
    [SerializeField] private ParticleRenerer particleRenderer;
    [SerializeField] private BodyRenderer bodyRenderer;
    [SerializeField] private ScreenSpaceFluidRenderer screenSpaceFluidRenderer;
    [Tooltip("Draws the forces on the bodies. Optional, scenes without force arrows can leave it empty")]
    [SerializeField] private Visualiser visualiser;
    [Tooltip("Draws the ripple tank view. Optional, scenes without waves can leave it empty")]
    [SerializeField] private WaveProjectionRenderer waveProjection;
    [Tooltip("Billboards draws every particle as a flat disc. ScreenSpace draws the fluid as one smooth, refractive surface with the screen-space fluid renderer")]
    [SerializeField] private FluidRenderMode fluidRenderMode = FluidRenderMode.Billboards;
    [Tooltip("Draw rigid bodies as solid meshes instead of as their boundary particles")]
    [SerializeField] private bool drawSolidBodies = true;
    [Tooltip("Direction the light travels in, shared by every shader that declares lightDirection")]
    [SerializeField] private Vector3 lightDirection = new(-0.4f, -1, 0.6f);

    private bool waveProjectionOn;

    /// <summary>Whether the ripple tank view replaces everything else that's drawn</summary>
    public bool WaveProjectionOn
    {
        get => waveProjectionOn;
        set
        {
            if (waveProjection == null || value == waveProjectionOn) return;

            waveProjectionOn = value;
            waveProjection.ResetHistory();
        }
    }

    /// <summary>Sets up every renderer for the new buffers</summary>
    private void Setup(SimulationManager sim)
    {
        TrySetup(environmentRenderer, () => environmentRenderer.Setup(sim));
        TrySetup(particleRenderer, () => particleRenderer.Setup(sim));
        TrySetup(bodyRenderer, () => bodyRenderer.Setup(sim));

        // Optional unless in screen-space mode
        if (screenSpaceFluidRenderer != null || fluidRenderMode == FluidRenderMode.ScreenSpace)
            TrySetup(screenSpaceFluidRenderer, () => screenSpaceFluidRenderer.Setup(sim, bodyRenderer, visualiser));

        if (visualiser != null)
            TrySetup(visualiser, () => visualiser.Setup(sim));

        if (waveProjection != null)
            TrySetup(waveProjection, () => waveProjection.Setup(sim));
    }

    /// <summary>Logs a renderer that fails to set up, without stopping the others</summary>
    private void TrySetup(Object renderer, System.Action setup)
    {
        try
        {
            setup();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Render manager: {(renderer != null ? renderer.GetType().Name : "an unassigned renderer")} failed to set up\n{e}", renderer);
        }
    }

    /// <summary>Draws everything for this frame</summary>
    private void Draw()
    {
        Shader.SetGlobalVector("lightDirection", lightDirection);

        if (visualiser != null)
            visualiser.hidden = waveProjectionOn;

        if (waveProjectionOn)
        {
            if (screenSpaceFluidRenderer != null)
                screenSpaceFluidRenderer.enabled = false;

            if (visualiser != null)
                visualiser.Draw();

            waveProjection.Draw();
            return;
        }

        environmentRenderer.DrawTank();

        bool screenSpace = fluidRenderMode == FluidRenderMode.ScreenSpace;
        if (screenSpaceFluidRenderer != null)
            screenSpaceFluidRenderer.enabled = screenSpace;

        if (screenSpace)
            screenSpaceFluidRenderer.Draw();
        else
            particleRenderer.DrawParticles();

        if (drawSolidBodies)
            bodyRenderer.DrawBodies();
        else
            particleRenderer.DrawBoundaryParticles();

        if (visualiser != null)
            visualiser.Draw();
    }

    /// <summary>Runs before SimulationManager.Start, so the first BuffersChanged isn't missed</summary>
    private void OnEnable()
    {
        sim.BuffersChanged += Setup;
        sim.StepFinished += Draw;
    }

    private void OnDisable()
    {
        sim.BuffersChanged -= Setup;
        sim.StepFinished -= Draw;
    }
}
