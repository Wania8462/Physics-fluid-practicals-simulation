using System;
using Unity.Mathematics;
using UnityEngine;

// Real units: m/s², kg/m³, mPa·s, m³ and kg
public class SimulationAPI : MonoBehaviour
{
    private const float BodySampleDensity = 1;
    private const float SpawnDistance = 0.6f;   // Of the tank's largest side, in front of the camera

    [SerializeField] private SimulationManager manager;

    // ponytail: hand-picked so the 3DSandbox values read as water on Earth, not measured. Each scale maps one quantity on
    // its own, so together they aren't a consistent unit system. Calibrate them against what the practicals should show
    [Header("Unit scales")]
    [SerializeField] private float lengthScale = 200;           // Sim units per metre, the 250 unit tank is 1.25 m wide
    [SerializeField] private float densityScale = 1f / 1000;    // restDensity (sim mass per sim unit³) per kg/m³, 1 is water
    [SerializeField] private float gravityScale = 20 / 9.81f;   // Sim gravity per m/s², -20 is Earth
    [SerializeField] private float viscosityScale = 1;          // lowViscosity per mPa·s, 1 is water

    // Sim mass of 1 kg. restDensity is sim mass per sim unit³ and each fluid particle weighs restDensity * spacing³, so a
    // body as dense as the fluid weighs as much as the particles it displaces. An ice cube floating about 92% under is
    // the check
    private float MassScale => densityScale * math.pow(lengthScale, 3);

    // Sim force of 1 N. Accelerations are scaled like gravity, so a body's weight comes out as m g
    public float ForceScale => MassScale * gravityScale;

    public void SetPaused(bool paused)
    {
        manager.paused = paused;
    }

    public float GetGravity()
    {
        return -manager.settings.gravity / gravityScale;
    }

    public void SetGravity(float gravity)
    {
        manager.settings.gravity = -gravity * gravityScale;
    }

    public float GetDensity()
    {
        return manager.settings.restDensity / densityScale;
    }

    public void SetDensity(float density)
    {
        manager.settings.restDensity = density * densityScale;
    }

    public float GetViscosity()
    {
        return manager.settings.lowViscosity / viscosityScale;
    }

    public void SetViscosity(float viscosity)
    {
        manager.settings.lowViscosity = viscosity * viscosityScale;
    }

    // Places the body 60% of the tank's largest side in front of the camera, pushed back inside the tank if that spot
    // isn't, and returns its id
    public int AddBody(BodyShape3D shape, float volume, float mass)
    {
        float radius = ToSimRadius(shape, volume);

        if (mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass has to be above 0.");

        Tank tank = manager.Tank;
        if (math.any(radius > tank.innerHalfSize))
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "The body has to fit inside the tank.");

        // The simulation runs in world space, and a body that isn't rotated reaches radius along each axis
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

    public void SetBodyMass(int id, float mass)
    {
        if (mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass has to be above 0.");

        manager.SetBodyMass(id, mass * MassScale);
    }

    // Rebuilds the body with a new shape or volume where it is now
    public void SetBodyShape(int id, BodyShape3D shape, float volume)
    {
        manager.ReshapeBody(id, shape, ToSimRadius(shape, volume));
    }

    public void RemoveBody(int id)
    {
        manager.RemoveBody(id);
    }

    // Scales are numbered 0 to RigidBodies3D.MaxScales - 1. Neither the fluid nor the bodies move them, only MoveScale.
    // A new one goes where AddBody puts bodies
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

    public void RemoveScale(int scale)
    {
        manager.RemoveScale(scale);
    }

    // Moves the scale to the position, or as close as it fits inside the tank. The position is the base's centre
    public void MoveScale(int scale, Vector3 position)
    {
        manager.MoveScale(scale, ClampScale(position));
    }

    // The base's centre
    public Vector3 GetScalePosition(int scale)
    {
        if (!manager.TryGetScalePosition(scale, out Vector3 position))
            throw new ArgumentException($"There is no scale {scale}.", nameof(scale));

        return position;
    }

    // The base and the pan
    public Bounds GetScaleBounds(int scale)
    {
        Vector3 position = GetScalePosition(scale);
        float3 halfExtents = RigidBodies3D.ScaleHalfExtents;
        Bounds bounds = new(position, 2 * (Vector3)halfExtents);
        bounds.Encapsulate(position + Vector3.up * (halfExtents.y + 2 * RigidBodies3D.ScalePanHalfHeight));
        return bounds;
    }

    // In N, how hard the bodies on the scale push it, so in fluid it reads their weight minus their buoyancy
    public float GetScaleReading(int scale)
    {
        return manager.ScaleForce(scale) / ForceScale;
    }

    // While true, right mouse doesn't pull the bodies, so dragging a scale doesn't drag them along
    public void SetDraggingScale(bool dragging)
    {
        manager.draggingScale = dragging;
    }

    // The whole scale, base and pan, inside the tank, with the base centred on the position
    private float3 ClampScale(float3 position)
    {
        float3 halfExtents = RigidBodies3D.ScaleHalfExtents;
        float3 innerHalfSize = manager.Tank.innerHalfSize;
        float3 min = -innerHalfSize + halfExtents;
        float3 max = innerHalfSize - halfExtents - new float3(0, 2 * RigidBodies3D.ScalePanHalfHeight, 0);
        return math.clamp(position, min, max);
    }

    private float ToSimRadius(BodyShape3D shape, float volume)
    {
        if (volume <= 0)
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "The volume has to be above 0.");

        return RigidBodies3D.RadiusFromVolume(shape, volume * math.pow(lengthScale, 3));
    }
}
