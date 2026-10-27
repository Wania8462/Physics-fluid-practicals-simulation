using Unity.Mathematics;
using UnityEngine;

enum ParticleColor3D
{
    SolidBlue,
    BlueToRed,
    BlueToWhite
}

public class ParticleRenerer : MonoBehaviour
{
    [SerializeField] private int particleQuality = 5;
    [SerializeField] private ParticleColor3D color = ParticleColor3D.BlueToWhite;
    [SerializeField] private float maxSpeed = 100;
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Material material;
    [SerializeField] private Material boundaryMaterial;

    private Mesh mesh;

    private ComputeBuffer colorsBuffer;
    private GraphicsBuffer commandBuf;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    private RenderParams rp;

    private GraphicsBuffer boundaryCommandBuf;
    private RenderParams boundaryRp;
    private bool drawBoundaryParticles;

    private int3 threadGroups;

    const int CalculateColorsKernelID = 0;

    public void Setup(SimulationManager sim)
    {
        mesh = mesh == null ? MeshGenerator.Circle(sim.particleRadius, particleQuality) : mesh;
        ComputeHelper.Release(commandBuf);
        commandBuf = ComputeHelper.CreateCommandBuffer();
        commandData = ComputeHelper.CreateCommandData(mesh, sim.numParticles);
        commandBuf.SetData(commandData);
        rp = ComputeHelper.CreateRenderParams(material);

        threadGroups = compute.GetThreadGroups(CalculateColorsKernelID, sim.numParticles);
        compute.SetBuffer(CalculateColorsKernelID, "Velocities", sim.Buffers["Velocities"]);
        compute.SetInt("numParticles", sim.numParticles);

        ComputeHelper.Release(colorsBuffer);
        colorsBuffer = ComputeHelper.CreateStructuredBufferWithData(GetDefaultColors(sim.numParticles));
        compute.SetBuffer(CalculateColorsKernelID, "Colors", colorsBuffer);
        compute.SetBool("solidBlue", color == ParticleColor3D.SolidBlue);
        compute.SetBool("blueToRed", color == ParticleColor3D.BlueToRed);
        compute.SetBool("blueToWhite", color == ParticleColor3D.BlueToWhite);
        compute.SetFloat("maxSpeed", maxSpeed);

        rp.matProps.SetBuffer("Positions", sim.Buffers["Positions"]);
        rp.matProps.SetBuffer("Colors", colorsBuffer);

        SetupBoundaryParticles(sim);
    }

    // Rigid bodies are drawn as their boundary particles in a solid colour, like in the 2D renderer
    private void SetupBoundaryParticles(SimulationManager sim)
    {
        ComputeHelper.Release(boundaryCommandBuf);
        boundaryCommandBuf = null;
        drawBoundaryParticles = false;

        if (sim.numBoundaryParticles == 0) return;

        if (boundaryMaterial == null)
        {
            Debug.LogWarning("Particle renderer: no boundary material assigned, so rigid bodies won't be drawn. Create one with the Custom/SolidParticle3D shader");
            return;
        }

        boundaryCommandBuf = ComputeHelper.CreateCommandBuffer();
        boundaryCommandBuf.SetData(ComputeHelper.CreateCommandData(mesh, sim.numBoundaryParticles));
        boundaryRp = ComputeHelper.CreateRenderParams(boundaryMaterial);
        boundaryRp.matProps.SetBuffer("Positions", sim.Buffers["BoundaryPositions"]);
        drawBoundaryParticles = true;
    }

    public void DrawParticles()
    {
        compute.Dispatch(CalculateColorsKernelID, threadGroups);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount: 1);
    }

    public void DrawBoundaryParticles()
    {
        if (drawBoundaryParticles)
            Graphics.RenderMeshIndirect(boundaryRp, mesh, boundaryCommandBuf, commandCount: 1);
    }

    private float4[] GetDefaultColors(int length)
    {
        float4[] defaultColors = new float4[length];

        for (int i = 0; i < length; i++)
            defaultColors[i] = new float4(1, 0, 1, 1);

        return defaultColors;
    }

    private void ReleaseBuffers()
    {
        ComputeHelper.Release(commandBuf);
        ComputeHelper.Release(boundaryCommandBuf);
        ComputeHelper.Release(colorsBuffer);
    }

    private void OnDestroy() => ReleaseBuffers();

#if UNITY_EDITOR
    private void OnEnable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;

    private void OnDisable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
#endif
}
