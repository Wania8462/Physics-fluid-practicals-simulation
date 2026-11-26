using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>A SimulationAPI wired to a manager that never starts, so only its CPU side runs. Default 120 wide tank</summary>
internal sealed class SimulationRig : IDisposable
{
    public readonly SimulationAPI api;
    public readonly SimulationManager manager;
    public readonly Spawn3DParticles spawn;
    private readonly GameObject root;
    private readonly GameObject cameraObject;

    public SimulationRig()
    {
        // Behind the tank looking in, so new bodies spawn near its back wall
        cameraObject = new GameObject("Test camera") { tag = "MainCamera" };
        cameraObject.AddComponent<Camera>();
        cameraObject.transform.position = new Vector3(0, 0, -200);

        root = new GameObject("Test simulation");
        spawn = root.AddComponent<Spawn3DParticles>();
        manager = root.AddComponent<SimulationManager>();
        api = root.AddComponent<SimulationAPI>();
        SetReference(manager, "spawn", spawn);
        SetReference(api, "manager", manager);

        manager.settings.bodies = new RigidBodySettings3D[0];
    }

    /// <summary>Sets a private serialized reference, as the inspector would</summary>
    public static void SetReference(Object target, string field, Object value)
    {
        SerializedObject serialized = new(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public void Dispose()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(cameraObject);
    }
}
