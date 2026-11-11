using System;
using UnityEngine;
using UnityEngine.Rendering;

// DXR ambient contact occlusion over the static, opaque district collision geometry.
internal sealed class StreetRayContact : IDisposable
{
    private readonly ComputeShader compute;
    private readonly RayTracingAccelerationStructure geometry;
    private readonly int kernel;
    private RenderTexture mask;
    public int InstanceCount { get; }

    public static bool IsSupported(ComputeShader shader) =>
        shader && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 &&
        SystemInfo.supportsInlineRayTracing &&
        SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat);

    public StreetRayContact(ComputeShader shader, Camera camera, Material opaqueMaterial)
    {
        compute = shader;
        kernel = compute.FindKernel("TraceContacts");
        if (!compute.IsSupported(kernel))
            throw new NotSupportedException("The DXR contact kernel is unsupported.");
        geometry = new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings(
            RayTracingAccelerationStructure.ManagementMode.Manual, RayTracingAccelerationStructure.RayTracingModeMask.Everything, -1));
        try
        {
            foreach (var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>())
            {
                var mesh = collider.sharedMesh;
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody ||
                    collider.gameObject.scene != camera.gameObject.scene || !mesh)
                    continue;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles || mesh.GetIndexCount(submesh) < 3)
                        continue;
                    var config = new RayTracingMeshInstanceConfig(mesh, (uint)submesh, opaqueMaterial)
                    {
                        subMeshFlags = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                        enableTriangleCulling = false,
                        mask = 0xff
                    };
                    geometry.AddInstance(in config, collider.transform.localToWorldMatrix);
                }
            }
            InstanceCount = (int)geometry.GetInstanceCount();
            if (InstanceCount == 0)
                throw new InvalidOperationException("No district geometry was available for DXR.");
            geometry.Build();
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    public RenderTexture Render(Camera camera, RenderTexture depthNormals, float radius)
    {
        int width = depthNormals.width, height = depthNormals.height;
        if (!mask || mask.width != width || mask.height != height)
        {
            ReleaseMask();
            mask = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = "Street DXR contact mask",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!mask.Create())
                throw new InvalidOperationException("Could not allocate the DXR contact mask.");
        }
        compute.SetTexture(kernel, "_PackedDepthNormals", depthNormals);
        compute.SetTexture(kernel, "_ContactMask", mask);
        compute.SetRayTracingAccelerationStructure(kernel, "_District", geometry);
        compute.SetMatrix("_StreetCameraToWorld", camera.cameraToWorldMatrix);
        var projection = camera.projectionMatrix;
        compute.SetVector("_StreetProjection", new Vector4(1 / projection.m00, 1 / projection.m11, camera.farClipPlane, radius));
        compute.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
        return mask;
    }

    internal int CountOccludedPixels()
    {
        if (!mask || !SystemInfo.supportsAsyncGPUReadback)
            return 0;
        var request = AsyncGPUReadback.Request(mask);
        request.WaitForCompletion();
        if (request.hasError)
            return 0;
        int count = 0;
        foreach (float pixel in request.GetData<float>())
            if (pixel >= 0 && pixel < .96f)
                count++;
        return count;
    }

    private void ReleaseMask()
    {
        if (!mask)
            return;
        mask.Release();
        if (Application.isPlaying) UnityEngine.Object.Destroy(mask);
        else UnityEngine.Object.DestroyImmediate(mask);
        mask = null;
    }

    public void Dispose()
    {
        ReleaseMask();
        geometry.Dispose();
    }
}
