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
        internal readonly Material Material;
        internal readonly int SubmeshIndex;

        internal PRTGBufferCaptureDrawItem(Renderer renderer, Material material, int submeshIndex)
        {
            Renderer = renderer;
            Material = material;
            SubmeshIndex = submeshIndex;
        }
    }

    internal static class PRTGBufferCaptureBridge
    {
        private static Camera _camera;
        private static PRTGBufferCaptureDrawItem[] _drawItems;

        internal static IDisposable Begin(Camera camera, PRTGBufferCaptureDrawItem[] drawItems)
        {
            _camera = camera;
            _drawItems = drawItems;
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

        private sealed class PassData
        {
            internal PRTGBufferCaptureDrawItem[] DrawItems;
            internal Matrix4x4 ViewMatrix;
            internal Matrix4x4 ProjectionMatrix;
            internal Rect Viewport;
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

            passData.DrawItems = drawItems;
            passData.ViewMatrix = cameraData.GetViewMatrix();
            passData.ProjectionMatrix = cameraData.GetProjectionMatrix();
            passData.Viewport = new Rect(0.0f, 0.0f,
                cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);

            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                context.cmd.ClearRenderTarget(RTClearFlags.All, Color.clear, 1.0f, 0);
                context.cmd.SetViewProjectionMatrices(data.ViewMatrix, data.ProjectionMatrix);
                context.cmd.SetViewport(data.Viewport);

                foreach (var item in data.DrawItems)
                {
                    var renderer = item.Renderer;
                    if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    context.cmd.DrawRenderer(renderer, item.Material, item.SubmeshIndex, 0);
                }
            });
        }
    }
}
#endif
