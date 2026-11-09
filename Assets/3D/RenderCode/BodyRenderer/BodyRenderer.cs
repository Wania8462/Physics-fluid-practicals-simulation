using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Draws every rigid body as a solid cube, sphere or scale</summary>
public class BodyRenderer : MonoBehaviour
{
    private const string DepthPassName = "DepthOnly";

    [Tooltip("Unit cube, edge length 1, such as Unity's built-in Cube")]
    [SerializeField] private Mesh cubeMesh;
    [Tooltip("Unit sphere, diameter 1, such as Unity's built-in Sphere")]
    [SerializeField] private Mesh sphereMesh;
    [Tooltip("Cylinder of diameter 1 and height 2 along y, such as Unity's built-in Cylinder. Draws the pans of the scales")]
    [SerializeField] private Mesh cylinderMesh;
    [Tooltip("A material with the Custom/Body3D shader")]
    [SerializeField] private Material material;
    [SerializeField] private Color color = Color.gray;

    private GraphicsBuffer cubeCommandBuf;
    private GraphicsBuffer sphereCommandBuf;
    private GraphicsBuffer scaleBaseCommandBuf;
    private GraphicsBuffer scalePanCommandBuf;
    private RenderParams cubeRp;
    private RenderParams sphereRp;
    private RenderParams scaleBaseRp;
    private RenderParams scalePanRp;
    private int depthPass;

    /// <summary>One draw per shape, each covering every body. A scale is two draws, base and pan</summary>
    public void Setup(SimulationManager sim)
    {
        cubeRp = SetupDraw(sim, cubeMesh, BodyShape3D.Cube, 0, ref cubeCommandBuf);
        sphereRp = SetupDraw(sim, sphereMesh, BodyShape3D.Sphere, 0, ref sphereCommandBuf);
        scaleBaseRp = SetupDraw(sim, cubeMesh, BodyShape3D.Scale, 0, ref scaleBaseCommandBuf);
        scalePanRp = SetupDraw(sim, cylinderMesh, BodyShape3D.Scale, 1, ref scalePanCommandBuf);

        depthPass = material.FindPass(DepthPassName);
        if (depthPass < 0)
            throw new System.InvalidOperationException($"{material.shader.name} has no {DepthPassName} pass, so the screen-space fluid can't be hidden behind bodies");
    }

    /// <summary>One draw covering every body of a shape</summary>
    /// <param name="part">For scales, 0 draws the base and 1 the pan</param>
    private RenderParams SetupDraw(SimulationManager sim, Mesh mesh, BodyShape3D shape, int part, ref GraphicsBuffer commandBuf)
    {
        ComputeHelper.Release(commandBuf);
        commandBuf = ComputeHelper.CreateCommandBuffer();
        commandBuf.SetData(ComputeHelper.CreateCommandData(mesh, sim.Buffers["BodyStates"].count));

        RenderParams rp = ComputeHelper.CreateRenderParams(material);
        rp.matProps.SetBuffer("BodyStates", sim.Buffers["BodyStates"]);
        rp.matProps.SetBuffer("BodyProperties", sim.Buffers["BodyProperties"]);
        rp.matProps.SetInteger("drawShape", (int)shape);
        rp.matProps.SetInteger("drawPart", part);
        return rp;
    }

    /// <summary>Draws every body, lit</summary>
    public void DrawBodies()
    {
        Draw(cubeRp, cubeMesh, cubeCommandBuf);
        Draw(sphereRp, sphereMesh, sphereCommandBuf);
        Draw(scaleBaseRp, cubeMesh, scaleBaseCommandBuf);
        Draw(scalePanRp, cylinderMesh, scalePanCommandBuf);
    }

    /// <summary>Depth only, so bodies hide the screen-space fluid behind them</summary>
    public void DrawDepth(CommandBuffer cmd)
    {
        cmd.DrawMeshInstancedIndirect(cubeMesh, 0, material, depthPass, cubeCommandBuf, 0, cubeRp.matProps);
        cmd.DrawMeshInstancedIndirect(sphereMesh, 0, material, depthPass, sphereCommandBuf, 0, sphereRp.matProps);
        cmd.DrawMeshInstancedIndirect(cubeMesh, 0, material, depthPass, scaleBaseCommandBuf, 0, scaleBaseRp.matProps);
        cmd.DrawMeshInstancedIndirect(cylinderMesh, 0, material, depthPass, scalePanCommandBuf, 0, scalePanRp.matProps);
    }

    private void Draw(RenderParams rp, Mesh mesh, GraphicsBuffer commandBuf)
    {
        rp.matProps.SetColor("color", color);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount: 1);
    }

    private void ReleaseBuffers()
    {
        ComputeHelper.Release(cubeCommandBuf);
        ComputeHelper.Release(sphereCommandBuf);
        ComputeHelper.Release(scaleBaseCommandBuf);
        ComputeHelper.Release(scalePanCommandBuf);
    }

    private void OnDestroy() => ReleaseBuffers();

#if UNITY_EDITOR
    private void OnEnable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;

    private void OnDisable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
#endif
}
