#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    internal readonly struct PRTGBufferCaptureDrawItem
    {
        internal readonly Renderer Renderer;
        internal readonly Mesh Mesh;
        internal readonly Matrix4x4 LocalToWorld;
        internal readonly Material Material;
        internal readonly MaterialPropertyBlock Properties;
        internal readonly Vector4 Metadata;
        internal readonly Bounds Bounds;
        internal readonly int SubmeshIndex;
        internal readonly int PassIndex;

        internal PRTGBufferCaptureDrawItem(Renderer renderer, Mesh mesh, Matrix4x4 localToWorld,
            Material material, MaterialPropertyBlock properties, int submeshIndex, int passIndex, Bounds bounds)
        {
            Renderer = renderer;
            Mesh = mesh;
            LocalToWorld = localToWorld;
            Material = material;
            Properties = properties;
            Metadata = properties.GetVector("_PRTMetadata");
            Bounds = bounds;
            SubmeshIndex = submeshIndex;
            PassIndex = passIndex;
        }
    }

    internal static class PRTGBufferCaptureBridge
    {
        private static Camera _camera;
        private static PRTGBufferCaptureDrawItem[] _drawItems;
        private static float _sceneTime;

        internal static IDisposable Begin(Camera camera, PRTGBufferCaptureDrawItem[] drawItems, float sceneTime)
        {
            _camera = camera;
            _drawItems = drawItems;
            _sceneTime = sceneTime;
            return new Scope(camera);
        }

        internal static bool TryGet(Camera camera, out PRTGBufferCaptureDrawItem[] drawItems)
        {
            if (_camera == camera && _drawItems != null)
            {
                drawItems = _drawItems;
                return true;
            }

            drawItems = null;
            return false;
        }
        internal static float SceneTime => _sceneTime;

        private sealed class Scope : IDisposable
        {
            private readonly Camera _scopeCamera;

            internal Scope(Camera camera)
            {
                _scopeCamera = camera;
            }

            public void Dispose()
            {
                if (_camera != _scopeCamera)
                {
                    return;
                }

                _camera = null;
                _drawItems = null;
            }
        }
    }

    internal sealed class PRTGBufferCapturePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new("PRT GBuffer Capture");
        private static readonly int MetadataId = Shader.PropertyToID("_PRTMetadata");
        private static readonly int[] TimeIds = { Shader.PropertyToID("_Time"), Shader.PropertyToID("_SinTime"),
            Shader.PropertyToID("_CosTime"), Shader.PropertyToID("_TimeParameters"), Shader.PropertyToID("_LastTimeParameters") };

        private sealed class PassData
        {
            internal PRTGBufferCaptureDrawItem[] DrawItems;
            internal Matrix4x4 ViewMatrix;
            internal Matrix4x4 ProjectionMatrix;
            internal Rect Viewport;
            internal float SceneTime;
        }

        internal PRTGBufferCapturePass()
        {
            renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            if (!PRTGBufferCaptureBridge.TryGet(cameraData.camera, out var drawItems))
            {
                return;
            }

            var resourceData = frameData.Get<UniversalResourceData>();
            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                "PRT GBuffer Capture", out var passData, ProfilingSampler);

            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cameraData.GetProjectionMatrix() * cameraData.GetViewMatrix());
            var visible = new System.Collections.Generic.List<PRTGBufferCaptureDrawItem>(drawItems.Length);
            foreach (PRTGBufferCaptureDrawItem item in drawItems)
                if (GeometryUtility.TestPlanesAABB(planes, item.Bounds)) visible.Add(item);
            passData.DrawItems = visible.ToArray();
            passData.SceneTime = PRTGBufferCaptureBridge.SceneTime;
            passData.ViewMatrix = cameraData.GetViewMatrix();
            passData.ProjectionMatrix = cameraData.GetProjectionMatrix();
            passData.Viewport = new Rect(0.0f, 0.0f,
                cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                context.cmd.ClearRenderTarget(RTClearFlags.All, Color.clear, 1.0f, 0);
                context.cmd.SetViewProjectionMatrices(data.ViewMatrix, data.ProjectionMatrix);
                context.cmd.SetViewport(data.Viewport);
                int[] timeIds = TimeIds;
                var original = new Vector4[timeIds.Length];
                for (int i = 0; i < timeIds.Length; i++) original[i] = Shader.GetGlobalVector(timeIds[i]);
                float t = data.SceneTime;
                context.cmd.SetGlobalVector(timeIds[0], new Vector4(t / 20f, t, t * 2f, t * 3f));
                context.cmd.SetGlobalVector(timeIds[1], new Vector4(Mathf.Sin(t / 8f), Mathf.Sin(t / 4f), Mathf.Sin(t / 2f), Mathf.Sin(t)));
                context.cmd.SetGlobalVector(timeIds[2], new Vector4(Mathf.Cos(t / 8f), Mathf.Cos(t / 4f), Mathf.Cos(t / 2f), Mathf.Cos(t)));
                Vector4 parameters = new(t, Mathf.Sin(t), Mathf.Cos(t), 0);
                context.cmd.SetGlobalVector(timeIds[3], parameters);
                context.cmd.SetGlobalVector(timeIds[4], parameters);
                Vector4 originalMetadata = Shader.GetGlobalVector(MetadataId);
                try
                {
                    foreach (var item in data.DrawItems)
                    {
                        context.cmd.SetGlobalVector(MetadataId, item.Metadata);
                        context.cmd.DrawMesh(item.Mesh, item.LocalToWorld, item.Material,
                            item.SubmeshIndex, item.PassIndex, item.Properties);
                    }
                }
                finally
                {
                    for (int i = 0; i < timeIds.Length; i++) context.cmd.SetGlobalVector(timeIds[i], original[i]);
                    context.cmd.SetGlobalVector(MetadataId, originalMetadata);
                }
            });
        }
    }
}
#endif
