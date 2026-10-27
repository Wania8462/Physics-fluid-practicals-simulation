using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// The forces in BodyForces in their order, then the resultant, which the shader adds up from the others
public enum BodyForce
{
    Gravity,
    Contact,
    Buoyancy,
    Resultant
}

// Draws the forces on every rigid body as arrows from its centre of mass, posed in the vertex shader straight from the
// body buffers like BodyRenderer, and their values as UI labels. Forces are the last step's, so they stay while paused
public class Visualiser : MonoBehaviour
{
    private const int ArrowsPerBody = 4;    // BodyForce values
    private static readonly Vector2 ValuePadding = new(8, 4);

    [Tooltip("The camera the arrows are drawn for")]
    [SerializeField] private Camera cam;
    [Tooltip("A material with the Custom/ForceArrow3D shader")]
    [SerializeField] private Material material;
    [Tooltip("An empty RectTransform filling a Screen Space - Overlay canvas, placed before the windows so they cover the values")]
    [SerializeField] private RectTransform valueParent;

    // ponytail: linear, so a 0.1 kg cork and an 80 kg steel block can't both read well at one scale. Clamp or use a log
    // scale if the practicals need both at once
    [Tooltip("Arrow length per unit of sim force. A 2 L block of wood is 25 units wide and weighs about 10000")]
    [SerializeField] internal float arrowScale = 0.002f;
    [Tooltip("Head radius in sim units, the shaft is half as wide")]
    [SerializeField] private float width = 1.5f;
    [SerializeField] private float headLength = 5;
    [SerializeField] private Color gravityColor = Color.red;
    [SerializeField] private Color contactColor = Color.green;
    [SerializeField] private Color buoyancyColor = Color.cyan;
    [SerializeField] private Color resultantColor = Color.yellow;
    [Tooltip("Font size of the force values, in pixels")]
    [SerializeField] private int valueFontSize = 16;
    [Tooltip("Sim force of 1 N, which the values are divided by. The Visualisation window sets it from SimulationAPI.ForceScale when it opens")]
    [SerializeField] internal float forcePerNewton = 1;

    // Set through RenderAPI, indexed by BodyForce
    internal readonly bool[] shown = new bool[ArrowsPerBody];
    internal bool showValues;

    private Mesh mesh;
    private GraphicsBuffer commandBuf;
    private RenderParams rp;
    private CommandBuffer cmd;
    private bool drawArrows;

    // CPU copies of the body buffers for the values, read back while they are shown
    private ComputeBuffer statesBuffer;
    private ComputeBuffer forcesBuffer;
    private RigidBodyState3D[] states;
    private Vector4[] forces;

    // One per value drawn so far, the ones not needed this frame are hidden
    private readonly List<(RectTransform box, TMP_Text text)> labels = new();

    private Color[] Colors => new[] { gravityColor, contactColor, buoyancyColor, resultantColor };

    public void Setup(SimulationManager sim)
    {
        if (mesh == null)
            mesh = CreateArrowMesh();

        ComputeHelper.Release(commandBuf);
        commandBuf = ComputeHelper.CreateCommandBuffer();
        commandBuf.SetData(ComputeHelper.CreateCommandData(mesh, sim.Buffers["BodyStates"].count * ArrowsPerBody));

        rp = ComputeHelper.CreateRenderParams(material);
        rp.matProps.SetBuffer("BodyStates", sim.Buffers["BodyStates"]);
        rp.matProps.SetBuffer("BodyForces", sim.Buffers["BodyForces"]);

        statesBuffer = sim.Buffers["BodyStates"];
        forcesBuffer = sim.Buffers["BodyForces"];
    }

    // Settings are set every frame so inspector changes show while playing. The light direction is a global set by RenderManager
    public void Draw()
    {
        int shownForces = 0;
        for (int i = 0; i < ArrowsPerBody; i++)
            if (shown[i])
                shownForces |= 1 << i;

        drawArrows = shownForces != 0;
        if (!drawArrows) return;

        rp.matProps.SetInteger("shownForces", shownForces);
        rp.matProps.SetFloat("arrowScale", arrowScale);
        rp.matProps.SetFloat("width", width);
        rp.matProps.SetFloat("headLength", headLength);
        rp.matProps.SetVectorArray("colors", Array.ConvertAll(Colors, color => (Vector4)color));

        // The values are drawn on the CPU, so they need the forces back. Async, so the GPU isn't stalled, which leaves
        // the values a frame or two behind the arrows
        if (showValues)
        {
            AsyncGPUReadback.Request(statesBuffer, request => { if (!request.hasError) states = request.GetData<RigidBodyState3D>().ToArray(); });
            AsyncGPUReadback.Request(forcesBuffer, request => { if (!request.hasError) forces = request.GetData<Vector4>().ToArray(); });
        }
    }

    // Recorded right before the camera renders, after everything that moves it this frame has run. The buffer runs after
    // everything else the camera draws, the screen-space fluid included, so it sets the camera's matrices itself: the
    // fluid's Blits replace them
    private void RecordArrows(Camera camera)
    {
        if (camera != cam) return;

        cmd.Clear();
        if (!drawArrows) return;

        cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        cmd.DrawMeshInstancedIndirect(mesh, 0, material, 0, commandBuf, 0, rp.matProps);
    }

    // Each value sits at the middle of its arrow on the canvas, so it faces the camera, keeps its size and stays under the windows
    private void LateUpdate()
    {
        int used = 0;

        if (showValues && states != null && forces != null)
        {
            Color[] colors = Colors;
            const int ForceCount = RigidBodies3D.ForceCount;

            // The two readbacks can come from either side of a body being added or removed
            int numBodies = Mathf.Min(states.Length, forces.Length / ForceCount);

            for (int body = 0; body < numBodies; body++)
            {
                Vector3 resultant = Vector3.zero;
                for (int f = 0; f < ForceCount; f++)
                    resultant += (Vector3)forces[body * ForceCount + f];

                for (int arrow = 0; arrow < ArrowsPerBody; arrow++)
                {
                    Vector3 force = arrow == (int)BodyForce.Resultant ? resultant : (Vector3)forces[body * ForceCount + arrow];

                    // The placeholder body of an empty buffer has no forces
                    if (!shown[arrow] || force == Vector3.zero) continue;

                    Vector3 screen = cam.WorldToScreenPoint((Vector3)states[body].position.xyz + force * arrowScale / 2);
                    if (screen.z < 0) continue;     // Behind the camera

                    if (used == labels.Count)
                        labels.Add(CreateLabel());
                    (RectTransform box, TMP_Text text) = labels[used++];

                    text.text = $"{force.magnitude / forcePerNewton:0.0} N";
                    text.color = colors[arrow];
                    text.fontSize = valueFontSize;
                    box.sizeDelta = text.GetPreferredValues() + ValuePadding;

                    // A null camera is right for an overlay canvas
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(valueParent, screen, null, out Vector2 local);
                    box.localPosition = local;
                    box.gameObject.SetActive(true);
                }
            }
        }

        for (int i = used; i < labels.Count; i++)
            labels[i].box.gameObject.SetActive(false);
    }

    // A white box with the value centred on it. Neither takes clicks, so they don't block the windows or the scene
    private (RectTransform box, TMP_Text text) CreateLabel()
    {
        GameObject box = new("Force value", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(valueParent, false);
        box.GetComponent<Image>().raycastTarget = false;

        TextMeshProUGUI text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(box.transform, false);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = Vector2.zero;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        return ((RectTransform)box.transform, text);
    }

    // Unit arrow along +y, sized by the shader: the shaft runs from y = 0 to 1 with radius 0.5 and the head sits on it with
    // radius 1. uv.y is 1 at the tip and 0 everywhere else, so the shader can stretch the shaft and keep the head's length.
    // The arrows draw without depth, so the head comes after the shaft to cover it, and its base after both
    private static Mesh CreateArrowMesh()
    {
        const int Sides = 12;

        List<Vector3> vertices = new();
        List<Vector3> normals = new();
        List<Vector2> uvs = new();
        List<int> shaft = new(), head = new(), headBase = new();

        int Add(Vector3 vertex, Vector3 normal, float tip)
        {
            vertices.Add(vertex);
            normals.Add(normal);
            uvs.Add(new Vector2(0, tip));
            return vertices.Count - 1;
        }

        int baseCentre = Add(Vector3.up, Vector3.down, 0);

        for (int i = 0; i < Sides; i++)
        {
            // Seen from outside, the next side is to the right. Triangles are clockwise from outside, Unity's front face
            Vector3 left = Radial(i, Sides), right = Radial(i + 1, Sides);

            int bottomLeft = Add(left * 0.5f, left, 0), bottomRight = Add(right * 0.5f, right, 0);
            int topLeft = Add(left * 0.5f + Vector3.up, left, 0), topRight = Add(right * 0.5f + Vector3.up, right, 0);
            shaft.AddRange(new[] { topLeft, topRight, bottomRight, topLeft, bottomRight, bottomLeft });

            Vector3 leftNormal = (left + Vector3.up).normalized, rightNormal = (right + Vector3.up).normalized;
            int tip = Add(Vector3.up, (leftNormal + rightNormal).normalized, 1);
            head.AddRange(new[] { tip, Add(right + Vector3.up, rightNormal, 0), Add(left + Vector3.up, leftNormal, 0) });

            // Faces down, so it is clockwise from below
            headBase.AddRange(new[] { baseCentre, Add(left + Vector3.up, Vector3.down, 0), Add(right + Vector3.up, Vector3.down, 0) });
        }

        Mesh arrow = new() { name = "Force arrow" };
        arrow.SetVertices(vertices);
        arrow.SetNormals(normals);
        arrow.SetUVs(0, uvs);
        shaft.AddRange(head);
        shaft.AddRange(headBase);
        arrow.SetTriangles(shaft, 0);
        return arrow;
    }

    private static Vector3 Radial(int side, int sides)
    {
        float angle = 2 * Mathf.PI * side / sides;
        return new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
    }

    private void OnEnable()
    {
        cmd = new CommandBuffer { name = "Force arrows" };
        if (cam != null)
            cam.AddCommandBuffer(CameraEvent.AfterEverything, cmd);
        Camera.onPreRender += RecordArrows;

#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffer;
#endif
    }

    // Also runs before the object is destroyed and before scripts reload, so the command buffer is always released
    private void OnDisable()
    {
        Camera.onPreRender -= RecordArrows;
        if (cam != null)
            cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cmd);
        cmd.Release();

#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffer;
#endif
    }

    private void OnDestroy()
    {
        ReleaseBuffer();
        Destroy(mesh);
    }

    private void ReleaseBuffer() => ComputeHelper.Release(commandBuf);
}
