using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Forces drawn on each body. The resultant is the sum of the others</summary>
public enum BodyForce
{
    Gravity,
    Contact,
    Buoyancy,
    Resultant
}

/// <summary>Draws force arrows and values on the bodies, and the scale readings</summary>
public class Visualiser : MonoBehaviour
{
    private const int ArrowsPerBody = 4;
    private static readonly Vector2 ValuePadding = new(8, 4);
    /// <summary>Small gap so the side text doesn't flicker against the base</summary>
    private const float SideTextGap = 0.05f;

    [Tooltip("The camera the arrows are drawn for")]
    [SerializeField] private Camera cam;
    [Tooltip("A material with the Custom/ForceArrow3D shader")]
    [SerializeField] private Material material;
    [Tooltip("An empty RectTransform filling a Screen Space - Overlay canvas, placed before the windows so they cover the values")]
    [SerializeField] private RectTransform valueParent;

    // ponytail: linear, so very light and very heavy bodies can't both read well
    [Tooltip("Arrow length per unit of sim force. A 2 L block of wood is 25 units wide and weighs about 10000")]
    [SerializeField] internal float arrowScale = 0.002f;
    [Tooltip("Arrows of bigger forces are drawn as long as one of this many newtons, so they stay on screen. The value still reads the real force")]
    [SerializeField] private float maxArrowNewtons = 50;
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
    [Tooltip("Colour of the scale readings, on their sides and in their labels")]
    [SerializeField] private Color scaleColor = Color.black;
    [Tooltip("Colour of the depth lines. Keep it different from the body colour")]
    [SerializeField] private Color depthLineColor = Color.white;
    [Tooltip("Thickness of the depth lines, in pixels")]
    [SerializeField] private float depthLineWidth = 2;

    /// <summary>Which forces are drawn, indexed by BodyForce</summary>
    internal readonly bool[] shown = new bool[ArrowsPerBody];
    /// <summary>Whether each force value is written next to its arrow</summary>
    internal bool showValues;
    /// <summary>Whether the bodies show horizontal lines marking how deep they sit</summary>
    internal bool showDepthLines;

    /// <summary>Text written on each scale, null for none</summary>
    internal readonly string[] scaleTexts = new string[RigidBodies3D.MaxScales];
    /// <summary>Whether each scale's text also floats over it</summary>
    internal readonly bool[] scaleLabelsShown = new bool[RigidBodies3D.MaxScales];

    private SimulationManager sim;

    private readonly TextMeshPro[][] sideTexts = new TextMeshPro[RigidBodies3D.MaxScales][];

    private Mesh mesh;
    private GraphicsBuffer commandBuf;
    private RenderParams rp;
    private CommandBuffer cmd;
    private bool drawArrows;

    private ComputeBuffer statesBuffer;
    private ComputeBuffer forcesBuffer;
    private RigidBodyState3D[] states;
    private Vector4[] forces;

    private readonly List<(RectTransform box, TMP_Text text)> labels = new();

    private Color[] Colors => new[] { gravityColor, contactColor, buoyancyColor, resultantColor };
    private float MaxArrowLength => maxArrowNewtons * forcePerNewton * arrowScale;

    /// <summary>Binds the body buffers. Runs whenever they are rebuilt</summary>
    public void Setup(SimulationManager sim)
    {
        this.sim = sim;

        if (mesh == null)
            mesh = CreateArrowMesh();

        ComputeHelper.Release(commandBuf);
        commandBuf = ComputeHelper.CreateCommandBuffer();
        commandBuf.SetData(ComputeHelper.CreateCommandData(mesh, sim.Buffers["BodyStates"].count * ArrowsPerBody));

        rp = ComputeHelper.CreateRenderParams(material);
        rp.matProps.SetBuffer("BodyStates", sim.Buffers["BodyStates"]);
        rp.matProps.SetBuffer("BodyForces", sim.Buffers["SmoothedBodyForces"]);

        statesBuffer = sim.Buffers["BodyStates"];
        forcesBuffer = sim.Buffers["SmoothedBodyForces"];
    }

    /// <summary>Updates the arrow settings and requests the forces for the values</summary>
    public void Draw()
    {
        // The body shader reads these
        Shader.SetGlobalFloat("depthLines", showDepthLines ? 1 : 0);
        Shader.SetGlobalColor("depthLineColor", depthLineColor);
        Shader.SetGlobalFloat("depthLineWidth", depthLineWidth);

        int shownForces = 0;
        for (int i = 0; i < ArrowsPerBody; i++)
            if (shown[i])
                shownForces |= 1 << i;

        drawArrows = shownForces != 0;
        if (!drawArrows) return;

        rp.matProps.SetInteger("shownForces", shownForces);
        rp.matProps.SetFloat("arrowScale", arrowScale);
        rp.matProps.SetFloat("maxArrowLength", MaxArrowLength);
        rp.matProps.SetFloat("width", width);
        rp.matProps.SetFloat("headLength", headLength);
        rp.matProps.SetVectorArray("colors", Array.ConvertAll(Colors, color => (Vector4)color));

        // Reads the forces back for the value labels
        if (showValues)
        {
            AsyncGPUReadback.Request(statesBuffer, request => { if (!request.hasError) states = request.GetData<RigidBodyState3D>().ToArray(); });
            AsyncGPUReadback.Request(forcesBuffer, request => { if (!request.hasError) forces = request.GetData<Vector4>().ToArray(); });
        }
    }

    /// <summary>Draws the arrows after the fluid, so it sets the camera matrices itself</summary>
    private void RecordArrows(Camera camera)
    {
        if (camera != cam) return;

        cmd.Clear();
        if (!drawArrows) return;

        cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        cmd.DrawMeshInstancedIndirect(mesh, 0, material, 0, commandBuf, 0, rp.matProps);
    }

    /// <summary>Places the force value labels and the scale readings</summary>
    private void LateUpdate()
    {
        int used = 0;

        if (showValues && states != null && forces != null)
        {
            Color[] colors = Colors;
            const int ForceCount = RigidBodies3D.ForceCount;

            // The two readbacks may disagree on body count after one is added or removed
            int numBodies = Mathf.Min(states.Length, forces.Length / ForceCount);

            for (int body = 0; body < numBodies; body++)
            {
                Vector3 resultant = Vector3.zero;
                for (int f = 0; f < ForceCount; f++)
                    resultant += (Vector3)forces[body * ForceCount + f];

                for (int arrow = 0; arrow < ArrowsPerBody; arrow++)
                {
                    Vector3 force = arrow == (int)BodyForce.Resultant ? resultant : (Vector3)forces[body * ForceCount + arrow];

                    // Empty buffers hold a placeholder body with no forces
                    if (!shown[arrow] || force == Vector3.zero) continue;

                    Vector3 middle = (Vector3)states[body].position.xyz + Vector3.ClampMagnitude(force * arrowScale, MaxArrowLength) / 2;
                    PlaceLabel(ref used, middle, $"{force.magnitude / forcePerNewton:0.0} N", colors[arrow]);
                }
            }
        }

        for (int scale = 0; scale < RigidBodies3D.MaxScales; scale++)
        {
            if (scaleTexts[scale] == null || sim == null || !sim.TryGetScalePosition(scale, out Vector3 position))
            {
                if (sideTexts[scale] != null)
                    sideTexts[scale][0].transform.parent.gameObject.SetActive(false);
                continue;
            }

            sideTexts[scale] ??= CreateSideTexts(scale);
            Transform sides = sideTexts[scale][0].transform.parent;
            sides.gameObject.SetActive(true);
            sides.position = position;
            foreach (TextMeshPro side in sideTexts[scale])
                side.text = scaleTexts[scale];

            if (scaleLabelsShown[scale])
            {
                float panTop = RigidBodies3D.ScaleHalfExtents.y + 2 * RigidBodies3D.ScalePanHalfHeight;
                PlaceLabel(ref used, position + Vector3.up * panTop, scaleTexts[scale], scaleColor);
            }
        }

        for (int i = used; i < labels.Count; i++)
            labels[i].box.gameObject.SetActive(false);
    }

    /// <summary>Shows the next free label over a point in the world</summary>
    private void PlaceLabel(ref int used, Vector3 world, string value, Color color)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);
        if (screen.z < 0) return;

        if (used == labels.Count)
            labels.Add(CreateLabel());
        (RectTransform box, TMP_Text text) = labels[used++];

        text.text = value;
        text.color = color;
        text.fontSize = valueFontSize;
        box.sizeDelta = text.GetPreferredValues() + ValuePadding;

        // A null camera is right for an overlay canvas
        RectTransformUtility.ScreenPointToLocalPointInRectangle(valueParent, screen, null, out Vector2 local);
        box.localPosition = local;
        box.gameObject.SetActive(true);
    }

    /// <summary>The reading written on each side of a scale's base</summary>
    private TextMeshPro[] CreateSideTexts(int scale)
    {
        Transform sides = new GameObject($"Scale {scale} reading").transform;
        sides.SetParent(transform, false);

        Vector3 halfExtents = RigidBodies3D.ScaleHalfExtents;
        Vector3[] outwards = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        TextMeshPro[] texts = new TextMeshPro[outwards.Length];

        for (int i = 0; i < outwards.Length; i++)
        {
            Vector3 outward = outwards[i];
            TextMeshPro text = new GameObject("Side", typeof(RectTransform), typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            text.transform.SetParent(sides, false);

            text.transform.localPosition = Vector3.Scale(outward, halfExtents) + outward * SideTextGap;
            text.transform.localRotation = Quaternion.LookRotation(-outward);

            float width = 2 * Mathf.Abs(Vector3.Dot(Quaternion.LookRotation(-outward) * Vector3.right, halfExtents));
            text.rectTransform.sizeDelta = new Vector2(width, 2 * halfExtents.y);
            text.enableAutoSizing = true;
            text.fontSizeMin = 1;
            text.fontSizeMax = 100;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = scaleColor;

            texts[i] = text;
        }

        return texts;
    }

    /// <summary>A white box with the value centred on it, which doesn't block clicks</summary>
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

    /// <summary>
    /// A unit arrow along +y that the shader stretches to each force.
    /// Shaft, head, then head base, so they cover each other without depth
    /// </summary>
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
            // Triangles are clockwise from outside
            Vector3 left = Radial(i, Sides), right = Radial(i + 1, Sides);

            int bottomLeft = Add(left * 0.5f, left, 0), bottomRight = Add(right * 0.5f, right, 0);
            int topLeft = Add(left * 0.5f + Vector3.up, left, 0), topRight = Add(right * 0.5f + Vector3.up, right, 0);
            shaft.AddRange(new[] { topLeft, topRight, bottomRight, topLeft, bottomRight, bottomLeft });

            Vector3 leftNormal = (left + Vector3.up).normalized, rightNormal = (right + Vector3.up).normalized;
            int tip = Add(Vector3.up, (leftNormal + rightNormal).normalized, 1);
            head.AddRange(new[] { tip, Add(right + Vector3.up, rightNormal, 0), Add(left + Vector3.up, leftNormal, 0) });

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

    private void OnDisable()
    {
        Camera.onPreRender -= RecordArrows;
        // Globals outlive the scene, so other scenes would keep the lines
        Shader.SetGlobalFloat("depthLines", 0);
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
