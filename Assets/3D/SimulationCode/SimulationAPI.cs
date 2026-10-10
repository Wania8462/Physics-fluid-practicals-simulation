using System;
using Unity.Mathematics;

public class SimulationAPI : ISImulationAPI
{
    private const float BodySampleDensity = 1;
    private const float SpawnDistance = 0.6f;   // Of the tank's largest side, in front of the camera

    // ponytail: hand-picked so the 3DSandbox values read as water on Earth, not measured. Each scale maps one quantity on
    // its own, so together they aren't a consistent unit system. Calibrate them against what the practicals should show
    private const float LengthScale = 200;          // Sim units per metre, the 250 unit tank is 1.25 m wide
    private const float DensityScale = 5f / 1000;   // restDensity per kg/m³, 5 is water
    private const float GravityScale = 20 / 9.81f;  // Sim gravity per m/s², -20 is Earth
    private const float ViscosityScale = 1;         // lowViscosity per mPa·s, 1 is water

    private SimulationManager manager;

    public SimulationAPI(SimulationManager _manager)
    {
        manager = _manager;
    }

    // Sim mass of 1 kg. Fluid particles weigh 1 and, for the (1 - q)² density kernel, rest at restDensity * 30 / (4π h³)
    // to a sim unit³, so a body as dense as the fluid weighs as much as the particles it displaces. Ignores near pressure
    // and compression, so an ice cube floating about 90% under is the check
    private float MassScale => DensityScale * 30 / (4 * math.PI * math.pow(manager.InteractionRadius, 3)) * math.pow(LengthScale, 3);

    public float GetGravity()
    {
        return manager.settings.gravity / GravityScale;
    }

    public void SetGravity(float gravity)
    {
        ThrowIfRunning();
        manager.settings.gravity = gravity * GravityScale;
    }

    public float GetDensity()
    {
        return manager.settings.restDensity / DensityScale;
    }

    public void SetDensity(float density)
    {
        ThrowIfRunning();
        manager.settings.restDensity = density * DensityScale;
    }

    public float GetViscosity()
    {
        return manager.settings.lowViscosity / ViscosityScale;
    }

    public void SetViscosity(float viscosity)
    {
        ThrowIfRunning();
        manager.settings.lowViscosity = viscosity * ViscosityScale;
    }

    // Places the body 60% of the tank's largest side in front of the camera, pushed back inside the tank if that spot
    // isn't, and returns its id
    public int AddBody(BodyShape3D shape, float volume, float mass)
    {
        ThrowIfRunning();

        float radius = ToSimRadius(shape, volume);

        if (mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass has to be above 0.");

        Tank tank = manager.Tank;
        if (math.any(radius > tank.innerHalfSize))
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "The body has to fit inside the tank.");

        // The simulation runs in world space, and a body that isn't rotated reaches radius along each axis
        UnityEngine.Transform camera = UnityEngine.Camera.main.transform;
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
        ThrowIfRunning();

        if (mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass has to be above 0.");

        manager.SetBodyMass(id, mass * MassScale);
    }

    // Rebuilds the body with a new shape or volume where it is now
    public void SetBodyShape(int id, BodyShape3D shape, float volume)
    {
        ThrowIfRunning();
        manager.ReshapeBody(id, shape, ToSimRadius(shape, volume));
    }

    public void RemoveBody(int id)
    {
        ThrowIfRunning();
        manager.RemoveBody(id);
    }

    private float ToSimRadius(BodyShape3D shape, float volume)
    {
        if (volume <= 0)
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "The volume has to be above 0.");

        return RigidBodies3D.RadiusFromVolume(shape, volume * math.pow(LengthScale, 3));
    }

    private void ThrowIfRunning()
    {
        if (manager.Running)
            throw new InvalidOperationException("Cannot change simulation properties while the simulation is running.");
    }
}
