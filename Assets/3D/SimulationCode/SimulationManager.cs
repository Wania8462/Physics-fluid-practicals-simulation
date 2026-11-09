using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

/// <summary>Simulation settings shown in the inspector</summary>
[Serializable]
public struct SimulationSettings3D
{
    [Header("General")]
    public float interactionRadius;
    public float gravity;
    public float collisionDamping;

    [Header("Density")]
    [Tooltip("PBF iterations per step. More keeps deep fluid less compressed")]
    public int solverIterations;
    // ponytail: the safe value depends on spacing and interactionRadius, retune it if they change
    [Tooltip("Softens the density constraint. Below about 0.02 (spacing 3, radius 6) the solver overshoots and the fluid blows apart, higher is springier")]
    public float relaxation;
    [Tooltip("Strength k of the s_corr term that keeps particles from clumping, 0.1 in the PBF paper")]
    public float artificialPressure;
    [Tooltip("Pulls neighbouring particles together, mostly at the free surface, so the fluid holds together instead of scattering. Sim acceleration, 0 turns it off")]
    public float cohesion;
    [Tooltip("Sim mass per sim unit³. Only sets the particles' mass, the fluid's packing doesn't change")]
    public float restDensity;

    [Header("Viscosity")]
    public float highViscosity;
    public float lowViscosity;

    [Header("Rigid bodies")]
    public float boundaryFriction;
    public float bodyFriction;
    public RigidBodySettings3D[] bodies;
}

/// <summary>Runs the fluid and rigid body simulation on the GPU</summary>
public class SimulationManager : MonoBehaviour
{
    [Header("Simulation")]
    [SerializeField] internal bool paused;
    [SerializeField] public float particleRadius = 0.5f;
    [SerializeField] internal SimulationSettings3D settings;

    [Header("Time")]
    [SerializeField] private int targetFrameRate = 120;
    [SerializeField] private bool useRealDeltaTime;
    [SerializeField] private float fakeFramerate = 120;

    [Header("Readings")]
    [Tooltip("Seconds the drawn body forces take to ease about two thirds of the way to a new value. 0 draws them raw")]
    [SerializeField] private float forceSmoothingTime = 0.25f;
    [Tooltip("How near fluid has to be to a body's surface to count as touching it, in particle spacings. Lower it if floating bodies read too submerged. Keep it under interactionRadius")]
    [SerializeField] private float wetDistance = 1.5f;

    [Header("Dragging objects")]
    [Tooltip("Top speed of a thrown object, in sim units per second. At 120 steps per second, above about 360 it moves more than a particle spacing per step and can pass through fluid")]
    [SerializeField] private float maxThrowSpeed = 300;
    [Tooltip("Seconds of dragging that decide a throw's velocity. Higher ignores flicks, lower follows the last movement. 0 uses only the last frame")]
    [SerializeField] private float throwSmoothingTime = 0.05f;
    [Tooltip("Degrees per second a dragged cube turns towards lying flat while it's pushed into a wall")]
    [SerializeField] private float wallAlignSpeed = 180;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Spawn3DParticles spawn;
    private SPValues3D SP;

    /// <summary>Raised when the buffers are rebuilt, so the renderers can rebind them</summary>
    public event Action<SimulationManager> BuffersChanged;
    /// <summary>Raised every frame, even when paused, so the renderers draw</summary>
    public event Action StepFinished;
    /// <summary>Raised before each simulation step with its length in sim seconds, so bodies can be driven</summary>
    public event Action<float> StepStarting;

    [HideInInspector] public int numParticles;
    [HideInInspector] public int numBoundaryParticles;
    public float InteractionRadius => settings.interactionRadius;
    /// <summary>Spawn spacing, which PBF holds the fluid at</summary>
    public float Spacing => spawn.Spacing;
    /// <summary>The tank the fluid is simulated in</summary>
    public Tank Tank => spawn.GetTank(particleRadius);
    /// <summary>Height of the top of the fluid as spawned</summary>
    public float SurfaceHeight => spawn.SurfaceHeight;
    /// <summary>Sim seconds one step advances</summary>
    private float StepTime => useRealDeltaTime ? Time.deltaTime : 1 / fakeFramerate;

    private Dictionary<string, int> KernelIDs;
    /// <summary>Every GPU buffer, by its name in Simulation.compute</summary>
    public Dictionary<string, ComputeBuffer> Buffers = new();

    private RigidBodyData3D bodies;
    private int numBodies;

    /// <summary>Old body buffers, released next frame once nothing is drawing with them</summary>
    private readonly List<ComputeBuffer> replacedBuffers = new();

    /// <summary>Which body slot each scale uses, -1 if it isn't placed</summary>
    private readonly int[] scaleSlots = Enumerable.Repeat(-1, RigidBodies3D.MaxScales).ToArray();
    private Vector4[] scaleForces;
    private bool scaleReadingWanted;
    private Vector4[] smoothedForces;
    private bool buoyancyWanted;

    /// <summary>The body being dragged, -1 if there's none</summary>
    private int heldBody = -1;
    /// <summary>Smoothed velocity of the dragged body, which it keeps when released</summary>
    private float3 heldVelocity;

    /// <summary>Ids of the bodies asked about since the last measurement</summary>
    private readonly HashSet<int> submergedWanted = new();
    /// <summary>World up in each body's frame</summary>
    private Vector4[] submergedUps;
    /// <summary>For every boundary particle, non-zero if it touches fluid</summary>
    private uint[] boundaryWet;

    private int3 threadGroups;
    private int3 gridThreadGroups;
    private int3 boundaryThreadGroups;
    private int numScanSteps;

    private void Start()
    {
        Setup();
    }

    /// <summary>Whether the user is typing in a text field, so keys aren't hotkeys</summary>
    private static bool Typing()
    {
        GameObject selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        return selected && selected.TryGetComponent(out TMP_InputField input) && input.isFocused;
    }

    private void Update()
    {
        bool hotkeys = !Typing();

        if (hotkeys && Input.GetKeyDown(KeyCode.R))
            Setup();

        if (replacedBuffers.Count > 0)
        {
            foreach (ComputeBuffer buffer in replacedBuffers)
                ComputeHelper.Release(buffer);

            replacedBuffers.Clear();
            BuffersChanged?.Invoke(this);
        }

        if (hotkeys && Input.GetKeyDown(KeyCode.Space))
            paused = !paused;

        if (!paused || hotkeys && Input.GetKeyDown(KeyCode.RightArrow))
            ExecuteStep();

        else if (hotkeys && Input.GetKeyDown(KeyCode.PageDown))
        {
            for (int i = 0; i < 10; i++)
                ExecuteStep();
        }

        UpdateComputeSettings();

        if (scaleReadingWanted)
            AsyncGPUReadback.Request(Buffers["BodyScaleForces"], request => { if (!request.hasError) scaleForces = request.GetData<Vector4>().ToArray(); });
        scaleReadingWanted = false;

        if (buoyancyWanted)
            AsyncGPUReadback.Request(Buffers["SmoothedBodyForces"], request => { if (!request.hasError) smoothedForces = request.GetData<Vector4>().ToArray(); });
        buoyancyWanted = false;

        MeasureSubmergedBodies();

        StepFinished?.Invoke();
    }

    /// <summary>Measures the bodies the UI asked about and requests the results</summary>
    private void MeasureSubmergedBodies()
    {
        if (submergedWanted.Count == 0) return;

        foreach (int id in submergedWanted)
        {
            int body = bodies.inspectorIndices.IndexOf(id);
            if (body < 0) continue;

            compute.SetInt("submergedBody", body);
            compute.Dispatch(KernelIDs["MeasureSubmergedBody"], 1, 1, 1);
        }

        submergedWanted.Clear();
        AsyncGPUReadback.Request(Buffers["BodySubmergedUp"], request => { if (!request.hasError) submergedUps = request.GetData<Vector4>().ToArray(); });
        AsyncGPUReadback.Request(Buffers["BoundaryWet"], request => { if (!request.hasError) boundaryWet = request.GetData<uint>().ToArray(); });
    }

    private void ExecuteStep()
    {
        Watcher.ExecuteWithTimer("1. Time step", SimulationStep);
    }

    /// <summary>One step: forces and viscosity, the PBF solver with the bodies, then body contacts</summary>
    private void SimulationStep()
    {
        float dt = StepTime;
        compute.SetFloat("dt", dt);

        StepStarting?.Invoke(dt);

        compute.Dispatch(KernelIDs["ExternalForces"], threadGroups);

        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGroups);
        compute.Dispatch(KernelIDs["ApplyViscosity"], threadGroups);
        compute.Dispatch(KernelIDs["ApplyForceBuffersToVelocities"], threadGroups);

        compute.Dispatch(KernelIDs["AdvancePredictedPositions"], threadGroups);

        if (numBodies > 0)
        {
            DispatchPerBody("BodyAdvancePredictedStates");
            compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
        }

        BuildGrid();
        if (numBodies > 0)
            BuildBoundaryGrid();

        for (int i = 0; i < settings.solverIterations; i++)
        {
            compute.Dispatch(KernelIDs["CalculateLambdas"], threadGroups);

            if (numBodies > 0)
                compute.Dispatch(KernelIDs["ClearBoundaryForceBuffers"], boundaryThreadGroups);

            compute.Dispatch(KernelIDs["CalculateDeltaPositions"], threadGroups);
            compute.Dispatch(KernelIDs["ApplyDeltaPositions"], threadGroups);
            compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGroups);

            if (numBodies > 0)
            {
                DispatchPerBody("SumCouplingForces");
                DispatchPerBody("ApplyCouplingForces");
                compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
            }
        }

        if (numBodies > 0)
        {
            DispatchPerBody("SumContacts");
            DispatchPerBody("ApplyContacts");
            DispatchPerBody("BodyCalculateVelocity");
            compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
            compute.Dispatch(KernelIDs["ResolveBodyCollisions"], threadGroups);

            // Same smoothing speed whatever the frame rate
            compute.SetFloat("forceSmoothing", 1 - Mathf.Exp(-dt / forceSmoothingTime));
            DispatchPerBody("SmoothBodyForces");
        }

        compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGroups);

        compute.Dispatch(KernelIDs["CalculateVelocity"], threadGroups);
    }

    /// <summary>Sorts particles into grid cells for neighbour search</summary>
    private void BuildGrid()
    {
        compute.Dispatch(KernelIDs["ClearGrid"], gridThreadGroups);
        compute.Dispatch(KernelIDs["CountParticles"], threadGroups);

        for (int i = 0; i < numScanSteps; i++)
        {
            compute.SetInt("scanStride", 1 << i);
            compute.SetInt("scanFlip", i % 2);
            compute.Dispatch(KernelIDs["ScanStep"], gridThreadGroups);
        }

        compute.Dispatch(KernelIDs["ResetCursor"], gridThreadGroups);
        compute.Dispatch(KernelIDs["Scatter"], threadGroups);
    }

    /// <summary>Sorts the boundary particles into grid cells</summary>
    private void BuildBoundaryGrid()
    {
        compute.Dispatch(KernelIDs["ClearBoundaryGrid"], gridThreadGroups);
        compute.Dispatch(KernelIDs["CountBoundaryParticles"], boundaryThreadGroups);

        for (int i = 0; i < numScanSteps; i++)
        {
            compute.SetInt("scanStride", 1 << i);
            compute.SetInt("scanFlip", i % 2);
            compute.Dispatch(KernelIDs["BoundaryScanStep"], gridThreadGroups);
        }

        compute.Dispatch(KernelIDs["ResetBoundaryCursor"], gridThreadGroups);
        compute.Dispatch(KernelIDs["ScatterBoundary"], boundaryThreadGroups);
    }

    /// <summary>Runs a body kernel with one thread group per body</summary>
    private void DispatchPerBody(string kernel) => compute.Dispatch(KernelIDs[kernel], numBodies, 1, 1);

    private void OnValidate()
    {
        if (Buffers.ContainsKey("Positions"))
        {
            if (Buffers["Positions"] != null)
            {
                Application.targetFrameRate = targetFrameRate;
                UpdateComputeSettings();
            }
        }
    }

    /// <summary>Builds the particles, bodies and buffers from scratch. Also runs when R is pressed</summary>
    private void Setup()
    {
        ReleaseBuffers();

        KernelIDs = ComputeHelper.GetKernels(compute);
        Buffers = ComputeHelper.GetBuffers(compute);

        Application.targetFrameRate = targetFrameRate;

        Tank tank = Tank;
        SP = new(-tank.halfSize, tank.halfSize, settings.interactionRadius);

        bodies = RigidBodies3D.Build(settings.bodies, spawn, settings.interactionRadius, tank);
        numBodies = bodies.NumBodies;
        numBoundaryParticles = bodies.NumBoundaryParticles;

        // Removes fluid spawned inside or too close to a body
        float4[] positions = RigidBodies3D.RemoveParticlesInsideBodies(spawn.InitializePositions(), bodies.settings, settings.interactionRadius * 0.5f);
        numParticles = positions.Length;
        Debug.Log($"Simulation manager: {numParticles} particles");

        CreateBuffers(positions);

        SetBuffers();
        SetComputeSettings();

        threadGroups = compute.GetThreadGroups(0, numParticles);
        gridThreadGroups = compute.GetThreadGroups(KernelIDs["ClearGrid"], SP.NumCells + 1);
        boundaryThreadGroups = compute.GetThreadGroups(0, numBoundaryParticles);

        // Number of passes needed to build the grid, rounded up to even
        numScanSteps = 0;
        while ((1 << numScanSteps) < SP.NumCells + 1) numScanSteps++;
        if (numScanSteps % 2 == 1) numScanSteps++;

        // The first step needs a grid before it builds its own
        BuildGrid();

        if (numBodies > 0)
        {
            BuildBoundaryGrid();
            compute.Dispatch(KernelIDs["CalculateBoundaryVolumes"], boundaryThreadGroups);
        }

        BuffersChanged?.Invoke(this);
    }

    /// <summary>Adds a body to the first free slot and returns the slot as its id</summary>
    /// <returns>The body's id</returns>
    internal int AddBody(RigidBodySettings3D body)
    {
        if (settings.bodies.Count(slot => !slot.IsEmpty) >= RigidBodies3D.MaxBodies)
            throw new InvalidOperationException($"At most {RigidBodies3D.MaxBodies} rigid bodies are supported.");

        int id = Array.FindIndex(settings.bodies, slot => slot.IsEmpty);
        if (id < 0)
        {
            id = settings.bodies.Length;
            Array.Resize(ref settings.bodies, id + 1);
        }

        settings.bodies[id] = body;

        if (bodies == null) return id;

        ReadBodyStates();
        RigidBodies3D.AddBody(bodies, body, id, spawn);
        RebuildBodies();
        return id;
    }

    /// <summary>Removes a body. The other bodies keep moving</summary>
    internal void RemoveBody(int id)
    {
        CheckBodyId(id);
        settings.bodies[id] = default;

        if (bodies == null) return;

        ReadBodyStates();
        RigidBodies3D.RemoveBody(bodies, id);
        RebuildBodies();
    }

    /// <summary>Changes a body's mass, in sim units</summary>
    internal void SetBodyMass(int id, float mass)
    {
        CheckBodyId(id);
        settings.bodies[id].boundaryBodyMass = mass;
    }

    /// <summary>Rebuilds a body with a new shape and size where it is now</summary>
    internal void ReshapeBody(int id, BodyShape3D shape, float radius)
    {
        CheckBodyId(id);
        ref RigidBodySettings3D body = ref settings.bodies[id];
        body.shape = shape;
        body.bodyRadius = radius;

        if (bodies == null) return;

        ReadBodyStates();
        RigidBodyState3D state = bodies.states[bodies.inspectorIndices.IndexOf(id)];

        body.bodyPosition = state.position.xyz;
        body.bodyRotation = ((Quaternion)new quaternion(state.rotation)).eulerAngles;

        RigidBodies3D.RemoveBody(bodies, id);
        RigidBodies3D.AddBody(bodies, body, id, spawn);
        RebuildBodies();
    }

    /// <summary>Places a scale. Only MoveBody moves it after that</summary>
    internal void AddScale(RigidBodySettings3D scale)
    {
        if (scaleSlots[scale.scale] >= 0)
            throw new InvalidOperationException($"Scale {scale.scale} is already there.");

        scaleSlots[scale.scale] = AddBody(scale);
    }

    /// <summary>Removes a scale</summary>
    internal void RemoveScale(int scale)
    {
        RemoveBody(ScaleSlot(scale));
        scaleSlots[scale] = -1;
    }

    /// <summary>Teleports a body to a new position and holds it still until ReleaseBody</summary>
    internal void MoveBody(int id, float3 position)
    {
        CheckBodyId(id);

        // The first move of a drag and moves while paused don't count towards a throw
        float3 frameVelocity = heldBody == id && !paused ? (position - settings.bodies[id].bodyPosition) / StepTime : float3.zero;
        heldVelocity = math.lerp(heldVelocity, frameVelocity, 1 - Mathf.Exp(-Time.deltaTime / throwSmoothingTime));
        heldBody = id;

        settings.bodies[id].bodyPosition = position;
        SetBodyState(id, position, float3.zero);
    }

    /// <summary>Turns a dragged cube towards lying flat on any wall it's pushed into</summary>
    /// <returns>The position, kept inside the walls</returns>
    internal float3 PressAgainstWalls(int id, float3 position)
    {
        CheckBodyId(id);
        float radius = settings.bodies[id].bodyRadius;

        // Wall contacts keep a body's surface this far from the centre
        float3 walls = Tank.innerHalfSize - Spacing;

        if (settings.bodies[id].shape != BodyShape3D.Cube || bodies == null)
            return math.clamp(position, radius - walls, walls - radius);

        int index = bodies.inspectorIndices.IndexOf(id);
        RigidBodyState3D state = bodies.states[index];
        Quaternion rotation = (Quaternion)new quaternion(state.rotation);

        for (int axis = 0; axis < 3; axis++)
        {
            float3x3 basis = new float3x3((quaternion)rotation);
            if (math.abs(position[axis]) <= walls[axis] - CubeReach(basis, radius)[axis]) continue;

            int nearest = 0;
            for (int k = 1; k < 3; k++)
                if (math.abs(basis[k][axis]) > math.abs(basis[nearest][axis])) nearest = k;

            Vector3 wall = Vector3.zero;
            wall[axis] = math.sign(position[axis]);
            Vector3 face = basis[nearest] * math.sign(basis[nearest][axis] * wall[axis]);
            rotation = Quaternion.RotateTowards(rotation, Quaternion.FromToRotation(face, wall) * rotation, wallAlignSpeed * Time.deltaTime);
        }

        state.rotation = ((quaternion)rotation).value;
        bodies.states[index] = state;

        float3 reach = CubeReach(new float3x3((quaternion)rotation), radius);
        return math.clamp(position, reach - walls, walls - reach);
    }

    /// <summary>How far a rotated cube extends from its centre along each world axis</summary>
    private static float3 CubeReach(float3x3 basis, float radius) => radius * (math.abs(basis.c0) + math.abs(basis.c1) + math.abs(basis.c2));

    /// <summary>Lets go of a dragged body, which keeps the velocity it was dragged at. Scales stay still</summary>
    internal void ReleaseBody(int id)
    {
        float3 velocity = heldVelocity;
        heldBody = -1;
        heldVelocity = float3.zero;

        // Its window may have closed mid-drag
        if (!HasBody(id) || bodies == null || bodies.properties[bodies.inspectorIndices.IndexOf(id)].invMass == 0) return;

        SetBodyState(id, settings.bodies[id].bodyPosition, math.normalizesafe(velocity) * math.min(math.length(velocity), maxThrowSpeed));
    }

    /// <summary>Writes a body's position and velocity to the GPU. It stops spinning, its rotation stays as last read back</summary>
    internal void SetBodyState(int id, float3 position, float3 velocity)
    {
        if (bodies == null) return;

        int index = bodies.inspectorIndices.IndexOf(id);
        float4 at = new(position, 0);
        float4 rotation = bodies.states[index].rotation;
        RigidBodyState3D state = new() { position = at, rotation = rotation, prevPosition = at, prevRotation = rotation, velocity = new float4(velocity, 0) };
        Buffers["BodyStates"].SetData(new[] { state }, 0, index, 1);
    }

    /// <summary>Where a body is now. Stalls for a GPU readback</summary>
    internal Vector3 BodyPosition(int id)
    {
        CheckBodyId(id);
        ReadBodyStates();
        return bodies.states[bodies.inspectorIndices.IndexOf(id)].position.xyz;
    }

    /// <summary>Where a scale is, if it's placed</summary>
    public bool TryGetScalePosition(int scale, out Vector3 position)
    {
        int slot = scale >= 0 && scale < scaleSlots.Length ? scaleSlots[scale] : -1;
        position = slot >= 0 ? settings.bodies[slot].bodyPosition : default;
        return slot >= 0;
    }

    /// <summary>How hard the bodies push down on a scale. Lags a frame or two behind</summary>
    internal float ScaleForce(int scale)
    {
        ScaleSlot(scale);
        scaleReadingWanted = true;
        if (scaleForces == null) return 0;

        float force = 0;
        for (int i = scale; i < scaleForces.Length; i += RigidBodies3D.MaxScales)
            force += scaleForces[i].y;
        return force;
    }

    /// <summary>Only bodies asked about each frame get measured. Lags a frame or two behind</summary>
    /// <returns>How much of the body is under the fluid, from 0 to 1</returns>
    internal float SubmergedFraction(int id)
    {
        CheckBodyId(id);
        submergedWanted.Add(id);

        int body = bodies.inspectorIndices.IndexOf(id);
        RigidBodyProperties3D properties = bodies.properties[body];

        // Bodies may have been added or removed since these were read back
        if (submergedUps == null || boundaryWet == null || body >= submergedUps.Length || properties.start + properties.count > boundaryWet.Length)
            return 0;

        return RigidBodies3D.SubmergedFraction(bodies, body, ((float4)submergedUps[body]).xyz, boundaryWet);
    }

    /// <summary>The fluid's push on a body, smoothed like its arrow, in sim force units. Lags a frame or two behind</summary>
    internal Vector3 BodyBuoyancy(int id)
    {
        CheckBodyId(id);
        buoyancyWanted = true;

        // Bodies may have been added or removed since this was read back
        int index = bodies.inspectorIndices.IndexOf(id) * RigidBodies3D.ForceCount + RigidBodies3D.ForceBuoyancy;
        if (smoothedForces == null || index >= smoothedForces.Length)
            return Vector3.zero;

        return smoothedForces[index];
    }

    /// <summary>The body slot of a scale. Throws if the scale isn't placed</summary>
    internal int ScaleSlot(int scale)
    {
        if (scale < 0 || scale >= scaleSlots.Length || scaleSlots[scale] < 0)
            throw new ArgumentException($"There is no scale {scale}.", nameof(scale));

        return scaleSlots[scale];
    }

    /// <summary>Whether there's a body with this id</summary>
    internal bool HasBody(int id) => id >= 0 && id < settings.bodies.Length && !settings.bodies[id].IsEmpty;

    /// <summary>Throws if there's no body with this id</summary>
    private void CheckBodyId(int id)
    {
        if (!HasBody(id))
            throw new ArgumentException($"There is no rigid body with id {id}.", nameof(id));
    }

    /// <summary>Copies the bodies' current positions back from the GPU</summary>
    private void ReadBodyStates()
    {
        if (numBodies == 0) return;

        RigidBodyState3D[] states = new RigidBodyState3D[numBodies];
        Buffers["BodyStates"].GetData(states);
        bodies.states.Clear();
        bodies.states.AddRange(states);
    }

    /// <summary>Recreates the body buffers after a body is added, removed or reshaped</summary>
    private void RebuildBodies()
    {
        // Old buffers are released next frame, since this frame's draws may still use them
        List<ComputeBuffer> previous = Buffers.Values.ToList();
        RigidBodies3D.CreateBuffers(Buffers, bodies, SP.NumCells);
        replacedBuffers.AddRange(previous.Except(Buffers.Values));

        numBodies = bodies.NumBodies;
        numBoundaryParticles = bodies.NumBoundaryParticles;
        boundaryThreadGroups = compute.GetThreadGroups(0, numBoundaryParticles);
        SetBuffers();
        UpdateComputeSettings();

        if (numBodies > 0)
        {
            // Places the boundary particles where the bodies are now
            compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
            BuildBoundaryGrid();
            compute.Dispatch(KernelIDs["CalculateBoundaryVolumes"], boundaryThreadGroups);

            // Pushes fluid out of any new body
            compute.Dispatch(KernelIDs["ResolveBodyCollisions"], threadGroups);
        }
    }

    #region Buffer helpers
    /// <summary>Sends the settings that can change while running to the GPU</summary>
    private void UpdateComputeSettings()
    {
        compute.SetFloat("interactionRadius", settings.interactionRadius);
        compute.SetFloat("interactionRadiusSq", settings.interactionRadius * settings.interactionRadius);
        compute.SetFloat("gravity", settings.gravity);
        compute.SetFloat("collisionDamp", settings.collisionDamping);

        if (settings.solverIterations < 1)
            Debug.LogWarning($"Simulation manager: solverIterations is {settings.solverIterations}, nothing will keep the fluid from compressing");

        // Each particle stands for one spawn-spacing cube of fluid
        float h = settings.interactionRadius;
        float particleVolume = math.pow(spawn.Spacing, 3);
        compute.SetFloat("particleVolume", particleVolume);
        compute.SetFloat("particleMass", settings.restDensity * particleVolume);
        compute.SetFloat("restSpacing", spawn.Spacing);
        compute.SetFloat("relaxation", settings.relaxation);
        compute.SetFloat("artificialPressure", settings.artificialPressure);
        compute.SetFloat("poly6Factor", 315 / (64 * math.PI * math.pow(h, 9)));
        compute.SetFloat("spikyGradFactor", 45 / (math.PI * math.pow(h, 6)));
        compute.SetFloat("cohesion", settings.cohesion);
        compute.SetFloat("cohesionFactor", 32 / (math.PI * math.pow(h, 9)));
        compute.SetFloat("wetDistanceSq", math.pow(wetDistance * spawn.Spacing, 2));

        compute.SetFloat("highViscosity", settings.highViscosity);
        compute.SetFloat("lowViscosity", settings.lowViscosity);

        if (numBodies > 0)
        {
            if (settings.boundaryFriction <= 0)
                Debug.LogWarning($"Simulation manager: boundaryFriction is {settings.boundaryFriction}, fluid particles won't be slowed near the rigid bodies and may tunnel through them");

            RigidBodies3D.UpdateMasses(bodies, settings.bodies, Buffers["BodyProperties"]);
        }

        compute.SetFloat("boundaryFriction", settings.boundaryFriction);
        compute.SetFloat("bodyFriction", settings.bodyFriction);

        compute.SetInt("numBoundaryParticles", numBoundaryParticles);
        compute.SetInt("numBodies", numBodies);
    }

    /// <summary>Sends the settings that only change on setup to the GPU</summary>
    private void SetComputeSettings()
    {
        compute.SetInt("numParticles", numParticles);
        UpdateComputeSettings();

        float3 innerHalfSize = Tank.innerHalfSize;
        compute.SetVector("realHalfBoundSize", new Vector4(innerHalfSize.x, innerHalfSize.y, innerHalfSize.z));
        compute.SetFloat("particleRadius", particleRadius);

        compute.SetInt("numCells", SP.NumCells);
        compute.SetVector("offset", new(SP.offset.x, SP.offset.y, SP.offset.z, 0));
        compute.SetFloat("cellLength", SP.length);
        compute.SetInt("columns", SP.columns);
        compute.SetInt("rows", SP.rows);
        compute.SetInt("layers", SP.layers);
    }

    /// <summary>Binds every buffer to every kernel</summary>
    private void SetBuffers()
    {
        foreach (var kernel in KernelIDs)
            foreach (var buffer in Buffers)
                compute.SetBuffer(kernel.Value, buffer.Key, buffer.Value);
    }

    /// <summary>Creates every GPU buffer for the fluid and the bodies</summary>
    private void CreateBuffers(float4[] positions)
    {
        if (numParticles == 0)
            Debug.LogWarning("Simulation manager: there are 0 particles. Creating non-existant buffers.");

        // A tiny interactionRadius makes more cells than an int can count
        if (SP.NumCells <= 0)
            Debug.LogError($"Simulation manager: grid has {SP.NumCells} cells. interactionRadius is too small for the bounding box, so every neighbour query will read out of range.");

        Buffers["Positions"] = ComputeHelper.CreateStructuredBufferWithData(positions);
        Buffers["PrevPositions"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);
        Buffers["ForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["ForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["ForceBuffersZ"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["Velocities"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);

        Buffers["Densities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        Buffers["Lambdas"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        Buffers["DeltaPositions"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);

        Buffers["CellStart"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells + 1);
        Buffers["ScanTemp"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells + 1);
        Buffers["CellCursor"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells + 1);
        Buffers["SortedIndices"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles);

        // Read-only copies, see Simulation.compute
        Buffers["PositionsRO"] = Buffers["Positions"];
        Buffers["VelocitiesRO"] = Buffers["Velocities"];
        Buffers["LambdasRO"] = Buffers["Lambdas"];
        Buffers["CellStartRO"] = Buffers["CellStart"];
        Buffers["SortedIndicesRO"] = Buffers["SortedIndices"];

        RigidBodies3D.CreateBuffers(Buffers, bodies, SP.NumCells);
    }

    /// <summary>Releases every GPU buffer</summary>
    private void ReleaseBuffers()
    {
        // Distinct, because the read-only copies share buffers
        foreach (var buffer in Buffers.Values.Concat(replacedBuffers).Distinct())
            ComputeHelper.Release(buffer);

        replacedBuffers.Clear();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;
    }

    private void OnDisable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
    }
#endif

    #endregion
}