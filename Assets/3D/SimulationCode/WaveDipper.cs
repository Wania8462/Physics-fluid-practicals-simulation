using Unity.Mathematics;
using UnityEngine;

/// <summary>A full-width bar against the -x wall that moves up and down to make waves. Only scenes with waves need it</summary>
[RequireComponent(typeof(SimulationManager))]
public class WaveDipper : MonoBehaviour
{
    [Tooltip("Half its length along the tank, in sim units. Waves form best when its full length is about a quarter of their wavelength, much longer cancels out short waves")]
    [SerializeField] private float halfLength = 10;
    [Tooltip("Half its height, in sim units. This limits the amplitude")]
    [SerializeField] private float halfHeight = 15;
    [Tooltip("How far under the still water its bottom stays at the top of its stroke, in sim units. Raise it if it still comes out of the water, lower it for stronger short waves")]
    [SerializeField] private float clearance = 6;
    [Tooltip("Its highest acceleration, as a fraction of gravity. Above about 1 the water can't keep up with it and it splashes")]
    [SerializeField] private float maxAcceleration = 0.5f;
    [Tooltip("Sim units per second that its amplitude and its lowering change at, so it never jumps")]
    [SerializeField] private float easeSpeed = 5;
    [Tooltip("Spacing between the boundary particles on its surface. Keep it under the fluid's spacing")]
    [SerializeField] private float sampleDensity = 2;

    private SimulationManager manager;

    /// <summary>Body id of the bar, -1 if it isn't out</summary>
    private int body = -1;
    private bool on;
    private float frequency;
    /// <summary>The amplitude asked for, and the one it has eased to so far</summary>
    private float targetAmplitude, amplitude;
    private float phase;
    /// <summary>Height of the still water, and how far above its working height the bar still has to lower</summary>
    private float surface, lift;

    private void Awake()
    {
        manager = GetComponent<SimulationManager>();
    }

    /// <summary>Adds the bar, which then lowers until its bottom is in the water</summary>
    public void Add()
    {
        float3 inner = manager.Tank.innerHalfSize;
        surface = manager.SurfaceHeight;

        // Starts clear of the water, since appearing in it would shove the fluid away
        lift = clearance + manager.InteractionRadius;

        body = manager.AddBody(new RigidBodySettings3D
        {
            shape = BodyShape3D.Cube,
            bodyRadius = halfLength,  // Unused by a box, but a slot with radius 0 is empty
            boxHalfExtents = new float3(halfLength, halfHeight, inner.z),
            sampleDensity = sampleDensity,
            bodyPosition = new float3(halfLength - inner.x, surface + halfHeight + manager.InteractionRadius, 0),
            boundaryBodyMass = 1,  // Unused by a fixed body, but 0 logs a warning
            fixedInPlace = true
        });

        manager.StepStarting += Move;
    }

    /// <summary>Removes the bar</summary>
    public void Remove()
    {
        manager.StepStarting -= Move;
        manager.RemoveBody(body);
        body = -1;
        amplitude = 0;
    }

    /// <summary>Sets how the bar moves</summary>
    /// <param name="frequency">In Hz of simulation time</param>
    /// <param name="amplitude">In sim units</param>
    public void Set(bool on, float frequency, float amplitude)
    {
        this.on = on;
        this.frequency = frequency;
        targetAmplitude = amplitude;
    }

    /// <summary>Moves the bar up and down by one step. The phase carries on, so changing the frequency doesn't make it jump</summary>
    private void Move(float dt)
    {
        // The stroke has to fit the bar's height, and be slow enough for the water to follow it
        float limit = math.min(halfHeight - clearance / 2, FluidMath3D.AmplitudeForAcceleration(maxAcceleration * math.abs(manager.settings.gravity), frequency));
        float target = on ? math.min(targetAmplitude, limit) : 0;
        amplitude = Mathf.MoveTowards(amplitude, target, easeSpeed * dt);
        lift = Mathf.MoveTowards(lift, 0, easeSpeed * dt);
        phase = FluidMath3D.AdvancePhase(phase, frequency, dt);

        // Sits deeper for a bigger stroke, so its bottom never comes out of the water
        float draft = clearance + amplitude;

        float3 position = manager.settings.bodies[body].bodyPosition;
        float height = surface + halfHeight - draft + lift + amplitude * math.sin(phase);
        float3 velocity = new(0, (height - position.y) / dt, 0);
        position.y = height;

        manager.settings.bodies[body].bodyPosition = position;
        manager.SetBodyState(body, position, velocity);
    }
}
