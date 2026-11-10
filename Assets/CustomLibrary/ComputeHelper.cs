using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Helpers for GPU buffers, dispatches and reading shaders</summary>
public static class ComputeHelper
{
    private const int CommandCount = 1;
    /// <summary>Suffix of the read-only views of a buffer in the compute shaders, e.g. PositionsRO</summary>
    private const string ReadOnlySuffix = "RO";

    private static readonly string[] BufferTypes = { "RWStructuredBuffer<", "StructuredBuffer<", "RWBuffer<", "Buffer<" };

    #region Creating
    /// <summary>Buffer of count zeroed elements. Never empty, since Unity can't make an empty buffer</summary>
    public static ComputeBuffer CreateBuffer<T>(int count) where T : struct
    {
        ComputeBuffer buffer = new(math.max(count, 1), GetStride<T>());
        buffer.SetData(new T[buffer.count]);
        return buffer;
    }

    /// <summary>Buffer filled with data, or one zeroed element if there's none</summary>
    public static ComputeBuffer CreateBuffer<T>(IReadOnlyList<T> data) where T : struct
    {
        if (data.Count == 0)
            return CreateBuffer<T>(1);

        ComputeBuffer buffer = new(data.Count, GetStride<T>());
        buffer.SetData(data as T[] ?? data.ToArray());
        return buffer;
    }

    /// <summary>Arguments for drawing instances of mesh with one indirect draw</summary>
    public static GraphicsBuffer CreateDrawArgs(Mesh mesh, int instances)
    {
        GraphicsBuffer buffer = new(GraphicsBuffer.Target.IndirectArguments, CommandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        buffer.SetData(new[]
        {
            new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = mesh.GetIndexCount(0),
                instanceCount = (uint)instances
            }
        });
        return buffer;
    }

    /// <summary>Render params with huge bounds, so the draw is never culled</summary>
    public static RenderParams CreateRenderParams(Material material)
    {
        return new(material)
        {
            worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one),
            matProps = new MaterialPropertyBlock()
        };
    }
    #endregion

    #region Reading and writing
    /// <summary>Overwrites one element</summary>
    public static void SetElement<T>(this ComputeBuffer buffer, int index, T value) where T : struct
        => buffer.SetData(new[] { value }, 0, index, 1);

    /// <summary>The whole buffer. Stalls until the GPU catches up</summary>
    public static T[] Read<T>(this ComputeBuffer buffer) where T : struct
    {
        T[] data = new T[buffer.count];
        buffer.GetData(data);
        return data;
    }

    /// <summary>Reads the buffer without stalling. The data is only valid inside onRead, so copy what you keep</summary>
    /// <param name="onError">Runs instead of onRead if the read fails</param>
    public static void ReadAsync<T>(this ComputeBuffer buffer, Action<NativeArray<T>> onRead, Action onError = null) where T : struct
    {
        AsyncGPUReadback.Request(buffer, request =>
        {
            if (request.hasError) onError?.Invoke();
            else onRead(request.GetData<T>());
        });
    }
    #endregion

    #region Binding
    /// <summary>Binds every buffer to every kernel. Each must be declared in the shader</summary>
    public static void SetBuffers(this ComputeShader compute, IEnumerable<int> kernels, IReadOnlyDictionary<string, ComputeBuffer> buffers)
    {
        foreach (int kernel in kernels)
            foreach (var buffer in buffers)
                compute.SetBuffer(kernel, buffer.Key, buffer.Value);
    }

    /// <summary>Points each read-only view (a name ending in RO) at the buffer it views</summary>
    public static void LinkReadOnlyViews(Dictionary<string, ComputeBuffer> buffers)
    {
        foreach (string name in buffers.Keys.Where(name => name.EndsWith(ReadOnlySuffix)).ToList())
        {
            if (buffers.TryGetValue(name[..^ReadOnlySuffix.Length], out ComputeBuffer viewed))
                buffers[name] = viewed;
        }
    }
    #endregion

    #region Releasing
    /// <summary>Releases the buffer if there is one</summary>
    public static void Release(ComputeBuffer buffer) => buffer?.Release();

    /// <summary>Releases the buffer if there is one</summary>
    public static void Release(GraphicsBuffer buffer) => buffer?.Release();

    /// <summary>Releases each buffer once, even if it's listed more than once</summary>
    public static void Release(IEnumerable<ComputeBuffer> buffers)
    {
        foreach (ComputeBuffer buffer in buffers.Distinct())
            Release(buffer);
    }
    #endregion

    #region Dispatching
    public static void Dispatch(this ComputeShader compute, int kernelIndex, int3 threadGroups)
        => compute.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, threadGroups.z);

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int2 threadGroups)
        => compute.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, 1);

    /// <summary>Runs a kernel on at least this many threads</summary>
    public static void DispatchThreads(this ComputeShader compute, int kernelIndex, int threadsX, int threadsY = 1, int threadsZ = 1)
        => compute.Dispatch(kernelIndex, compute.GetThreadGroups(kernelIndex, threadsX, threadsY, threadsZ));

    /// <summary>Thread groups needed to cover this many iterations</summary>
    public static int3 GetThreadGroups(this ComputeShader compute, int kernelIndex, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1)
    {
        compute.GetKernelThreadGroupSizes(kernelIndex, out uint xt, out uint yt, out uint zt);
        return new int3(
            Mathf.CeilToInt(numIterationsX / (float)xt),
            Mathf.CeilToInt(numIterationsY / (float)yt),
            Mathf.CeilToInt(numIterationsZ / (float)zt));
    }

    /// <summary>Size of T in bytes</summary>
    public static int GetStride<T>()
        => System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
    #endregion

    // Editor only
    #region Shader reflection
    /// <summary>Kernel ids by name, read from the #pragma kernel lines</summary>
    public static Dictionary<string, int> GetKernels(ComputeShader compute)
    {
        string[] lines = ReadSource(compute).Where(l => l.Contains("#pragma kernel")).ToArray();
        Dictionary<string, int> result = new();

        for (int i = 0; i < lines.Length; i++)
            result.Add(lines[i].Split(' ').Last().Trim(), i);

        return result;
    }

    /// <summary>Every buffer the shader declares, by name, set to null for the caller to fill</summary>
    public static Dictionary<string, ComputeBuffer> GetBuffers(ComputeShader compute)
    {
        Dictionary<string, ComputeBuffer> result = new();

        foreach (string line in ReadSource(compute).Where(line => BufferTypes.Any(type => line.Contains(type))))
            result.Add(line.Split(' ').Last()[..^1], null);

        return result;
    }

    private static string[] ReadSource(ComputeShader compute) => File.ReadAllText(AssetDatabase.GetAssetPath(compute)).Split('\n');
    #endregion
}
