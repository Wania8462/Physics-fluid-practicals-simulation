using UnityEngine;

// How the fluid particles are drawn
enum FluidRenderMode
{
    Billboards,
    ScreenSpace
}

// The only listener of the simulation's events, so it decides what is set up and drawn
public class RenderManager : MonoBehaviour
{
    [SerializeField] private SimulationManager sim;
    [SerializeField] private EnvironmentRenderer environmentRenderer;
    [SerializeField] private ParticleRenerer particleRenderer;
    [SerializeField] private BodyRenderer bodyRenderer;
    [SerializeField] private ScreenSpaceFluidRenderer screenSpaceFluidRenderer;
    [Tooltip("Draws the forces on the bodies. Optional, scenes without force arrows can leave it empty")]
    [SerializeField] private Visualiser visualiser;
    [Tooltip("Billboards draws every particle as a flat disc. ScreenSpace draws the fluid as one smooth, refractive surface with the screen-space fluid renderer")]
    [SerializeField] private FluidRenderMode fluidRenderMode = FluidRenderMode.Billboards;
    [Tooltip("Draw rigid bodies as solid meshes instead of as their boundary particles")]
    [SerializeField] private bool drawSolidBodies = true;
    [Tooltip("Direction the light travels in, shared by every shader that declares lightDirection")]
    [SerializeField] private Vector3 lightDirection = new(-0.4f, -1, 0.6f);

    private void Setup(SimulationManager sim)
    {
        TrySetup(environmentRenderer, () => environmentRenderer.Setup(sim));
        TrySetup(particleRenderer, () => particleRenderer.Setup(sim));
        TrySetup(bodyRenderer, () => bodyRenderer.Setup(sim));

        // Only needed in screen-space mode, so leaving it unassigned isn't an error while the fluid is drawn as billboards
        if (screenSpaceFluidRenderer != null || fluidRenderMode == FluidRenderMode.ScreenSpace)
            TrySetup(screenSpaceFluidRenderer, () => screenSpaceFluidRenderer.Setup(sim, bodyRenderer));

        if (visualiser != null)
            TrySetup(visualiser, () => visualiser.Setup(sim));
    }

    // One renderer failing to set up doesn't stop the others. Clicking the error selects the renderer that failed
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

    private void Draw()
    {
        // Global, so every renderer's shader lights from the same direction without being passed it. Set each frame so inspector changes show
        Shader.SetGlobalVector("lightDirection", lightDirection);

        environmentRenderer.DrawTank();

        // Disabling the screen-space renderer takes its command buffer off the camera, so billboard mode renders as before
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

    // Runs before SimulationManager.Start, so the first BuffersChanged isn't missed
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
