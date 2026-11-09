using Unity.Mathematics;
using UnityEngine;

/// <summary>The ripple tank view: a white plane on the tank floor, lit by light focused through the waves</summary>
public class WaveProjectionRenderer : MonoBehaviour
{
    private const float RefractiveIndex = 1.33f;

    [Tooltip("WaveProjection.compute")]
    [SerializeField] private ComputeShader compute;
    [Tooltip("WaveProjection.shader")]
    [SerializeField] private Shader shader;
    [Tooltip("How far below the water the light is focused, in sim units. Higher gives stronger bands")]
    [SerializeField] private float screenDistance = 400;
    [Tooltip("Standard deviation of the blur on the water height, in sim units. Raise it if still water looks speckled, lower it to keep short waves sharp")]
    [SerializeField] private float blurRadius = 9;
    [Tooltip("Seconds the water height is averaged over, which removes particle jitter. 0 turns it off")]
    [SerializeField] private float smoothingTime = 0.3f;
    [Tooltip("Limits how bright focused light gets: the brightest band is 1 / this times the plain plane")]
    [SerializeField] private float minFocus = 0.25f;
    [Tooltip("Brightness of the plane under still water. Below 1 so bright bands don't clip to white")]
    [SerializeField] private float planeBrightness = 0.6f;

    private SimulationManager sim;
    private Mesh mesh;
    private Material material;
    private RenderParams renderParams;
    private Matrix4x4 planeTransform;

    private ComputeBuffer counts;
    private RenderTexture heights, blurTemp, blurred, lightMap;

    private int clearKernel, depositKernel, resolveKernel, blurXKernel, blurYKernel, lightKernel;
    private int gridWidth, gridHeight;
    private Vector2 cellSize;

    /// <summary>When true, the next frame ignores the old heights instead of averaging with them</summary>
    private bool resetHistory = true;

    /// <summary>Builds the grid and plane for the simulation's tank and binds the particles</summary>
    public void Setup(SimulationManager sim)
    {
        this.sim = sim;
        ReleaseResources();

        Tank tank = sim.Tank;
        gridWidth = Mathf.Max(1, Mathf.RoundToInt(tank.size.x / sim.Spacing));
        gridHeight = Mathf.Max(1, Mathf.RoundToInt(tank.size.z / sim.Spacing));
        cellSize = new Vector2(tank.size.x / gridWidth, tank.size.z / gridHeight);

        counts = ComputeHelper.CreateStructuredBufferWithData<uint>(gridWidth * gridHeight);
        heights = CreateTexture();
        blurTemp = CreateTexture();
        blurred = CreateTexture();
        lightMap = CreateTexture();

        clearKernel = compute.FindKernel("ClearCounts");
        depositKernel = compute.FindKernel("Deposit");
        resolveKernel = compute.FindKernel("ResolveHeight");
        blurXKernel = compute.FindKernel("BlurX");
        blurYKernel = compute.FindKernel("BlurY");
        lightKernel = compute.FindKernel("Light");

        compute.SetBuffer(clearKernel, "Counts", counts);
        compute.SetBuffer(depositKernel, "Counts", counts);
        compute.SetBuffer(depositKernel, "Positions", sim.Buffers["Positions"]);
        compute.SetBuffer(resolveKernel, "Counts", counts);
        compute.SetTexture(resolveKernel, "Heights", heights);
        compute.SetTexture(blurXKernel, "Heights", heights);
        compute.SetTexture(blurXKernel, "BlurTemp", blurTemp);
        compute.SetTexture(blurYKernel, "BlurTemp", blurTemp);
        compute.SetTexture(blurYKernel, "Blurred", blurred);
        compute.SetTexture(lightKernel, "Blurred", blurred);
        compute.SetTexture(lightKernel, "LightMap", lightMap);

        if (material == null) material = new Material(shader);
        material.SetTexture("lightMap", lightMap);
        material.SetVector("tankHalfSize", new Vector2(tank.halfSize.x, tank.halfSize.z));
        renderParams = new RenderParams(material);

        mesh = MeshGenerator.Rectangle(tank.size.x, tank.size.z);
        planeTransform = Matrix4x4.TRS(new Vector3(0, -tank.halfSize.y, 0), Quaternion.Euler(90, 0, 0), Vector3.one);

        resetHistory = true;
    }

    /// <summary>Works out this frame's light and draws the plane</summary>
    public void Draw()
    {
        SetSettings();

        compute.Dispatch(clearKernel, compute.GetThreadGroups(clearKernel, gridWidth * gridHeight));
        compute.Dispatch(depositKernel, compute.GetThreadGroups(depositKernel, sim.numParticles));

        int3 gridGroups = compute.GetThreadGroups(resolveKernel, gridWidth, gridHeight);
        compute.Dispatch(resolveKernel, gridGroups);
        compute.Dispatch(blurXKernel, gridGroups);
        compute.Dispatch(blurYKernel, gridGroups);
        compute.Dispatch(lightKernel, gridGroups);

        material.SetFloat("planeBrightness", planeBrightness);
        Graphics.RenderMesh(renderParams, mesh, 0, planeTransform);

        resetHistory = false;
    }

    /// <summary>Makes the next frame start from the current water, not the heights from when the view was last on</summary>
    public void ResetHistory()
    {
        resetHistory = true;
    }

    /// <summary>Sends the settings that can change while running to the GPU</summary>
    private void SetSettings()
    {
        Tank tank = sim.Tank;
        float sigma = Mathf.Max(blurRadius, 0.01f);

        compute.SetInt("numParticles", sim.numParticles);
        compute.SetInt("gridWidth", gridWidth);
        compute.SetInt("gridHeight", gridHeight);
        compute.SetVector("gridOrigin", new Vector2(-tank.halfSize.x, -tank.halfSize.z));
        compute.SetVector("cellSize", cellSize);
        compute.SetFloat("particleHeight", Mathf.Pow(sim.Spacing, 3) / (cellSize.x * cellSize.y));
        compute.SetFloat("temporalBlend", resetHistory ? 1 : 1 - Mathf.Exp(-Time.deltaTime / smoothingTime));
        compute.SetInt("blurRadiusX", Mathf.CeilToInt(3 * sigma / cellSize.x));
        compute.SetInt("blurRadiusY", Mathf.CeilToInt(3 * sigma / cellSize.y));
        compute.SetVector("blurFalloff", new Vector2(BlurFalloff(sigma, cellSize.x), BlurFalloff(sigma, cellSize.y)));
        compute.SetFloat("lensGain", screenDistance * (1 - 1 / RefractiveIndex));
        compute.SetFloat("minFocus", minFocus);
    }

    /// <summary>Gaussian exponent per squared cell</summary>
    private static float BlurFalloff(float sigma, float cell) => -cell * cell / (2 * sigma * sigma);

    private RenderTexture CreateTexture()
    {
        RenderTexture texture = new(gridWidth, gridHeight, 0, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        return texture;
    }

    private void ReleaseResources()
    {
        ComputeHelper.Release(counts);
        Destroy(heights);
        Destroy(blurTemp);
        Destroy(blurred);
        Destroy(lightMap);
        Destroy(mesh);
    }

    private void OnDestroy()
    {
        ReleaseResources();
        Destroy(material);
    }
}
