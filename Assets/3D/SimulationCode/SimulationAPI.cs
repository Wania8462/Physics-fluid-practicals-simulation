using System;
using Unity.Mathematics;
using UnityEngine;

/// <summary>The UI's way into the simulation, in real units: m/s², kg/m³, mPa·s, m³ and kg</summary>
public class SimulationAPI : MonoBehaviour
{
    private const float BodySampleDensity = 1;
    private const float SpawnDistance = 0.6f;  // Of the tank's largest side, in front of the camera

    [SerializeField] private SimulationManager manager;

    /// <summary>Only scenes with waves have one, on the same object as the manager</summary>
    private WaveDipper Dipper => manager.GetComponent<WaveDipper>();

    // ponytail: hand-picked to look like water on Earth, not measured
    [Header("Unit scales")]
    [SerializeField] private float lengthScale = 200;          // Sim units per metre
    [SerializeField] private float densityScale = 1f / 1000;   // restDensity per kg/m³
    [SerializeField] private float gravityScale = 20 / 9.81f;  // Sim gravity per m/s²
    [SerializeField] private float viscosityScale = 1;         // lowViscosity per mPa·s

    /// <summary>Sim mass of 1 kg</summary>
    private float MassScale => densityScale * math.pow(lengthScale, 3);

    /// <summary>Sim force of 1 N</summary>
    public float ForceScale => MassScale * gravityScale;

    /// <summary>Simulation seconds run so far, which stop while paused</summary>
    public float GetSimulationTime()
    {
        return manager.SimulationTime;
    }

    /// <summary>Pauses or resumes the simulation</summary>
    public void SetPaused(bool paused)
    {
        manager.paused = paused;
    }

    /// <summary>Gravity in m/s², positive downwards</summary>
    public float GetGravity()
    {
        return -manager.settings.gravity / gravityScale;
    }

    /// <summary>Sets gravity in m/s², positive downwards</summary>
    public void SetGravity(float gravity)
    {
        manager.settings.gravity = -gravity * gravityScale;
    }

    /// <summary>Fluid density in kg/m³</summary>
    public float GetDensity()
    {
        return manager.settings.restDensity / densityScale;
    }

    /// <summary>Sets the fluid density in kg/m³</summary>
    public void SetDensity(float density)
    {
        manager.settings.restDensity = density * densityScale;
    }

    /// <summary>Fluid viscosity in mPa·s</summary>
    public float GetViscosity()
    {
        return manager.settings.lowViscosity / viscosityScale;
    }

    /// <summary>Sets the fluid viscosity in mPa·s</summary>
    public void SetViscosity(float viscosity)
    {
        manager.settings.lowViscosity = viscosity * viscosityScale;
    }

    /// <summary>Spawns a body in front of the camera, inside the tank</summary>
    /// <param name="volume">In m³</param>
    /// <param name="mass">In kg</param>
    /// <returns>The body's id</returns>
    public int AddBody(BodyShape3D shape, float volume, float mass)
    {
        float radius = ToSimRadius(shape, volume);

        if (mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass has to be above 0.");

        Tank tank = manager.Tank;
        if (math.any(radius > tank.innerHalfSize))
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "The body has to fit inside the tank.");

        Transform camera = Camera.main.transform;
        float3 position = (float3)camera.position + (float3)camera.forward * (SpawnDistance * math.cmax(tank.size));
        float3 limit = tank.innerHalfSize - radius;

        return manager.AddBody(new RigidBodySettings3D
        {
            shape = shape,
            bodyRadius = radius,
            sampleDensity = BodySampleDensity,
            bodyPosition = math.clamp(position, -limit, limit),
            boundaryBodyMass = mass * MassScale
        });
    }

    /// <summary>Changes a body's mass</summary>
    /// <param name="mass">In kg</param>
    public void SetBodyMass(int id, float mass)
    {
        if (mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass has to be above 0.");

        manager.SetBodyMass(id, mass * MassScale);
    }

    /// <summary>Rebuilds a body with a new shape or volume where it is now</summary>
    /// <param name="volume">In m³</param>
    public void SetBodyShape(int id, BodyShape3D shape, float volume)
    {
        manager.ReshapeBody(id, shape, ToSimRadius(shape, volume));
    }

    /// <summary>Removes a body</summary>
    public void RemoveBody(int id)
    {
        manager.RemoveBody(id);
    }

    /// <summary>Places a scale in front of the camera</summary>
    /// <param name="scale">0 to RigidBodies3D.MaxScales - 1</param>
    public void AddScale(int scale)
    {
        if (scale < 0 || scale >= RigidBodies3D.MaxScales)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, $"Scales are numbered 0 to {RigidBodies3D.MaxScales - 1}.");

        Transform camera = Camera.main.transform;
        float3 position = (float3)camera.position + (float3)camera.forward * (SpawnDistance * math.cmax(manager.Tank.size));

        manager.AddScale(new RigidBodySettings3D
        {
            shape = BodyShape3D.Scale,
            bodyRadius = RigidBodies3D.ScaleHalfExtents.x,  // Unused by scales, but a slot with radius 0 is empty
            sampleDensity = BodySampleDensity,
            bodyPosition = ClampScale(position),
            scale = scale
        });
    }

    /// <summary>Removes a scale</summary>
    public void RemoveScale(int scale)
    {
        manager.RemoveScale(scale);
    }

    /// <summary>Lowers the wave dipper into the end of the tank, switched off</summary>
    public void AddDipper()
    {
        Dipper.Add();
    }

    /// <summary>Removes the wave dipper</summary>
    public void RemoveDipper()
    {
        Dipper.Remove();
    }

    /// <summary>Sets how the wave dipper moves. Turning it off eases it to rest</summary>
    /// <param name="frequency">In Hz, of simulation time</param>
    /// <param name="amplitude">In m</param>
    public void SetDipper(bool on, float frequency, float amplitude)
    {
        Dipper.Set(on, frequency, amplitude * lengthScale);
    }

    /// <summary>Created when the first meter is added, so scenes without meters pay nothing</summary>
    private DepthMeters depthMeters;
    private DepthMeters Meters => depthMeters ??= new DepthMeters(manager);

    /// <summary>Goes up whenever a depth meter's reading changes</summary>
    public int DepthMeterRevision => Meters.Revision;

    /// <summary>Adds a depth meter in the middle of the tank</summary>
    /// <param name="meter">0 to DepthMeters.MaxMeters - 1</param>
    public void AddDepthMeter(int meter)
    {
        Meters.Add(meter);
    }

    /// <summary>Removes a depth meter</summary>
    public void RemoveDepthMeter(int meter)
    {
        Meters.Remove(meter);
    }

    /// <summary>Whether this depth meter is out</summary>
    public bool HasDepthMeter(int meter)
    {
        return Meters.Has(meter);
    }

    /// <summary>Moves a depth meter across the tank floor, to this x and z in sim units</summary>
    public void MoveDepthMeter(int meter, float x, float z)
    {
        Meters.Move(meter, x, z);
    }

    /// <summary>The middle of the meter's ray, halfway up the tank, in sim units</summary>
    public Vector3 GetDepthMeterPosition(int meter)
    {
        return Meters.Position(meter);
    }

    /// <summary>Length of a meter's ray, from the tank's floor to its top, in sim units</summary>
    public float GetDepthRayLength()
    {
        return manager.Tank.size.y;
    }

    /// <summary>Box around the meter's ray for grabbing it</summary>
    /// <param name="grabSize">Width and height of the box, in sim units</param>
    public Bounds GetDepthMeterBounds(int meter, float grabSize)
    {
        return new Bounds(GetDepthMeterPosition(meter), new Vector3(grabSize, GetDepthRayLength(), grabSize));
    }

    /// <summary>Depth of the fluid under the meter, in m</summary>
    public float GetDepthMeterReading(int meter)
    {
        return Meters.Depth(meter) / lengthScale;
    }

    /// <summary>Readings from the last seconds of simulation time</summary>
    /// <returns>Seconds before the newest reading (at most 0) and the depth in m</returns>
    public Vector2[] GetDepthMeterHistory(int meter, float seconds)
    {
        Vector2[] history = Meters.History(meter, seconds);
        for (int i = 0; i < history.Length; i++)
            history[i].y /= lengthScale;
        return history;
    }

    /// <summary>The body id of a scale</summary>
    public int GetScaleBodyId(int scale)
    {
        return manager.ScaleSlot(scale);
    }

    /// <summary>Whether there's a body with this id</summary>
    public bool HasBody(int id)
    {
        return manager.HasBody(id);
    }

    /// <summary>Lets go of a dragged body, throwing it at the speed it was dragged</summary>
    public void ReleaseBody(int id)
    {
        manager.ReleaseBody(id);
    }

    /// <summary>Moves a body, clamped inside the tank, and holds it there. A scale's position is its base's centre</summary>
    public void MoveBody(int id, Vector3 position)
    {
        bool isScale = manager.settings.bodies[id].shape == BodyShape3D.Scale;
        manager.MoveBody(id, isScale ? ClampScale(position) : manager.PressAgainstWalls(id, position));
    }

    /// <summary>Where a body is now. Stalls for a GPU readback</summary>
    public Vector3 GetBodyPosition(int id)
    {
        return manager.BodyPosition(id);
    }

    /// <summary>Box around a body, ignoring its rotation. Stalls for a GPU readback</summary>
    public Bounds GetBodyBounds(int id)
    {
        RigidBodySettings3D body = manager.settings.bodies[id];
        Vector3 position = GetBodyPosition(id);
        if (body.shape != BodyShape3D.Scale)
            return new(position, 2 * body.bodyRadius * Vector3.one);

        float3 halfExtents = RigidBodies3D.ScaleHalfExtents;
        Bounds bounds = new(position, 2 * (Vector3)halfExtents);
        bounds.Encapsulate(position + Vector3.up * (halfExtents.y + 2 * RigidBodies3D.ScalePanHalfHeight));
        return bounds;
    }

    /// <summary>Only bodies asked about each frame get measured, so don't ask while the reading is hidden</summary>
    /// <returns>How much of the body is under the fluid, from 0 to 100</returns>
    public float GetSubmergedPercent(int id)
    {
        return 100 * manager.SubmergedFraction(id);
    }

    /// <summary>In N. In fluid it reads the bodies' weight minus their buoyancy. Only read back while something asks for it</summary>
    public float GetScaleReading(int scale)
    {
        return manager.ScaleForce(scale) / ForceScale;
    }

    /// <summary>Keeps the whole scale inside the tank</summary>
    private float3 ClampScale(float3 position)
    {
        float3 halfExtents = RigidBodies3D.ScaleHalfExtents;
        float3 innerHalfSize = manager.Tank.innerHalfSize;
        float3 min = -innerHalfSize + halfExtents;
        float3 max = innerHalfSize - halfExtents - new float3(0, 2 * RigidBodies3D.ScalePanHalfHeight, 0);
        return math.clamp(position, min, max);
    }

    /// <summary>Turns a volume in m³ into a sim body radius</summary>
    internal float ToSimRadius(BodyShape3D shape, float volume)
    {
        if (volume <= 0)
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "The volume has to be above 0.");

        return RigidBodies3D.RadiusFromVolume(shape, volume * math.pow(lengthScale, 3));
    }
}
