#if !UNITY_EDITOR
#define NOT_UNITY_EDITOR
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public static class IllusionRenderingUtils
    {
        private static Mesh _triangleMesh;

        private static Mesh TriangleMesh
        {
            get
            {
                if (_triangleMesh == null)
                {
                    float nearClipZ = SystemInfo.usesReversedZBuffer ? 1 : -1;
                    _triangleMesh = new Mesh
                    {
                        vertices = GetFullScreenTriangleVertexPosition(nearClipZ),
                        uv = GetFullScreenTriangleTexCoord(),
                        triangles = new[] { 0, 1, 2 }
                    };
                }

                return _triangleMesh;
            }
        }
        
        // Should match Common.hlsl
        private static Vector3[] GetFullScreenTriangleVertexPosition(float z /*= UNITY_NEAR_CLIP_VALUE*/)
        {
            var r = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                Vector2 uv = new Vector2((i << 1) & 2, i & 2);
                r[i] = new Vector3(uv.x * 2.0f - 1.0f, uv.y * 2.0f - 1.0f, z);
            }
            return r;
        }
        
        // Should match Common.hlsl
        private static Vector2[] GetFullScreenTriangleTexCoord()
        {
            var r = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                if (SystemInfo.graphicsUVStartsAtTop)
                    r[i] = new Vector2((i << 1) & 2, 1.0f - (i & 2));
                else
                    r[i] = new Vector2((i << 1) & 2, i & 2);
            }
            return r;
        }
        
        public static void SetMaterialProperties(Renderer renderer, Lazy<MaterialPropertyBlock> propertyBlock, List<(int, float)> floats)
        {
            SetPropertiesViaPropertyBlock(renderer, propertyBlock, floats);
            SetPropertiesViaMaterial(renderer, floats);
        }

        [Conditional("UNITY_EDITOR")]
        private static void SetPropertiesViaPropertyBlock(Renderer renderer, Lazy<MaterialPropertyBlock> propertyBlock, List<(int, float)> floats)
        {
            MaterialPropertyBlock properties = propertyBlock.Value;
            renderer.GetPropertyBlock(properties);

            for (int i = 0; i < floats.Count; i++)
            {
                properties.SetFloat(floats[i].Item1, floats[i].Item2);
            }

            renderer.SetPropertyBlock(properties);
        }

        [Conditional("NOT_UNITY_EDITOR")]
        private static void SetPropertiesViaMaterial(Renderer renderer, List<(int, float)> floats)
        {
            List<Material> materials = ListPool<Material>.Get();

            try
            {
                renderer.GetMaterials(materials);

                foreach (var material in materials)
                {
                    for (int i = 0; i < floats.Count; i++)
                    {
                        material.SetFloat(floats[i].Item1, floats[i].Item2);
                    }
                }
            }
            finally
            {
                ListPool<Material>.Release(materials);
            }
        }

        public static bool MultisampleDepthResolveSupported()
        {
            // Temporarily disabling depth resolve a driver bug on OSX when using some AMD graphics cards.
            // Temporarily disabling depth resolve on that platform
            // TODO: re-enable once the issue is investigated/fixed
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
                return false;

            // Should we also check if the format has stencil and check stencil resolve capability only in that case?
            return SystemInfo.supportsMultisampleResolveDepth && SystemInfo.supportsMultisampleResolveStencil;
        }

        public static int DivRoundUp(int x, int y) => (x + y - 1) / y;

        public static void SetKeyword(CommandBuffer cmd, ComputeShader cs, string keyword, bool state)
        {
            var kw = new LocalKeyword(cs, keyword);
            if (state)
                cmd.EnableKeyword(cs, kw);
            else
                cmd.DisableKeyword(cs, kw);
        }

        public static bool IsValid(this RTHandle rtHandle)
        {
            return rtHandle != null && rtHandle.rt;
        }

        public static float4x4 CalculateViewProjMatrix(UniversalCameraData cameraData, bool yFlip)
        {
            float4x4 viewMat = cameraData.GetViewMatrix();
            float4x4 projMat = GetGPUProjectionMatrix(cameraData, yFlip);
            return math.mul(projMat, viewMat);
        }

        public static Matrix4x4 GetGPUProjectionMatrix(UniversalCameraData cameraData, bool yFlip, int viewIndex = 0)
        {
            TemporalAA.JitterFunc jitterFunc;
            if (cameraData.IsSTPEnabled())
            {
                jitterFunc = StpUtils.s_JitterFunc;
            }
            else
            {
                jitterFunc = TemporalAA.s_JitterFunc;
            }
            Matrix4x4 jitterMat = TemporalAA.CalculateJitterMatrix(cameraData, jitterFunc);
            // GetGPUProjectionMatrix takes a projection matrix and returns a GfxAPI adjusted version, does not set or get any state.
            return jitterMat * GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrixNoJitter(viewIndex), yFlip);
        }

        public static float ComputeViewportScale(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Scale by (vp_dim / buf_dim).
            return viewportSize * rcpBufferSize;
        }

        public static float ComputeViewportLimit(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Clamp to (vp_dim - 0.5) / buf_dim.
            return (viewportSize - 0.5f) * rcpBufferSize;
        }

        public static Vector4 ComputeViewportScaleAndLimit(Vector2Int viewportSize, Vector2Int bufferSize)
        {
            return new Vector4(ComputeViewportScale(viewportSize.x, bufferSize.x),  // Scale(x)
                ComputeViewportScale(viewportSize.y, bufferSize.y),                 // Scale(y)
                ComputeViewportLimit(viewportSize.x, bufferSize.x),                 // Limit(x)
                ComputeViewportLimit(viewportSize.y, bufferSize.y));                // Limit(y)
        }
        
        public static void ValidateComputeBuffer(ref ComputeBuffer cb, int size, int stride, 
            ComputeBufferType type = ComputeBufferType.Default)
        {
            if (cb == null || cb.count < size)
            {
                CoreUtils.SafeRelease(cb);
                cb = new ComputeBuffer(size, stride, type);
            }
        }
        
        // Returns mouse coordinates: (x,y) in pixels and (z,w) normalized inside the render target (not the viewport)
        public static Vector4 GetMouseCoordinates(UniversalCameraData cameraData)
        {
            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            // We request the mouse post based on the type of the camera
            Vector2 mousePixelCoord = MousePositionDebug.instance.GetMousePosition(height, cameraData.camera.cameraType == CameraType.SceneView);
            return new Vector4(mousePixelCoord.x, mousePixelCoord.y, RTHandles.rtHandleProperties.rtHandleScale.x * mousePixelCoord.x / width, RTHandles.rtHandleProperties.rtHandleScale.y * mousePixelCoord.y / height);
        }
        
        public static void FinalBlit(CommandBuffer cmd, ref RenderingData renderingData, RTHandle source)
        {
            var cameraTarget = RenderingUtils.GetCameraTargetIdentifier(ref renderingData);
            RTHandleStaticHelpers.SetRTHandleStaticWrapper(cameraTarget);
            var cameraTargetHandle = RTHandleStaticHelpers.s_RTHandleWrapper;
            var cameraData = renderingData.cameraData;
            bool isRenderToBackBufferTarget = !cameraData.isSceneViewCamera;
            // We y-flip if
            // 1) we are blitting from render texture to back buffer(UV starts at bottom) and
            // 2) renderTexture starts UV at top
            bool yflip = isRenderToBackBufferTarget && cameraData.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
            Vector2 viewportScale = Vector2.one;
            Vector4 scaleBias = yflip ? new Vector4(viewportScale.x, -viewportScale.y, 0, viewportScale.y) 
                : new Vector4(viewportScale.x, viewportScale.y, 0, 0);
            CoreUtils.SetRenderTarget(cmd, cameraTargetHandle);
            Blitter.BlitTexture(cmd, source, scaleBias, 0.0f, false);
        }
        
        public static bool GetPrecomputedRadianceTransferFeatureEnabled()
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset == null)
            {
                return false;
            }

            if (asset.m_RendererDataList == null)
            {
                return false;
            }

            foreach (ScriptableRendererData rendererData in asset.m_RendererDataList)
            {
                if (UniversalRenderingUtility.TryGetRendererFeature<IllusionRendererFeature>(rendererData, out var rendererFeature)
                    && rendererFeature.isActive
                    && rendererFeature.precomputedRadianceTransferGI)
                {
                    return IllusionRuntimeRenderingConfig.Get().EnablePrecomputedRadianceTransferGlobalIllumination;
                }
            }
            return false;
        }
        
        public static float AsFloat(uint val) { unsafe { return *((float*)&val); } }
        
        public static uint AsUInt(float val) { unsafe { return *((uint*)&val); } }
        
        // These two convertion functions are used to store GUID assets inside materials,
        // a unity asset GUID is exactly 16 bytes long which is also a Vector4 so by adding a
        // Vector4 field inside the shader we can store references of an asset inside the material
        // which is actually used to store the reference of the diffusion profile asset
        internal static Vector4 ConvertGUIDToVector4(string guid)
        {
            Vector4 vector;
            byte[] bytes = new byte[16];

            for (int i = 0; i < 16; i++)
                bytes[i] = byte.Parse(guid.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);

            unsafe
            {
                fixed(byte* b = bytes)
                    vector = *(Vector4*)b;
            }

            return vector;
        }
        
        internal static string ConvertVector4ToGUID(Vector4 vector)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            unsafe
            {
                byte* v = (byte*)&vector;
                for (int i = 0; i < 16; i++)
                    sb.Append(v[i].ToString("x2"));
                var guidBytes = new byte[16];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)v, guidBytes, 0, 16);
            }

            return sb.ToString();
        }
        
        internal static void DrawFullScreenTriangle(CommandBuffer cmd, Material material, int shaderPass)
        {
            if (SystemInfo.graphicsShaderLevel < 30)
            {
                cmd.DrawMesh(TriangleMesh, Matrix4x4.identity, material, 0, shaderPass);
            }
            else
            {
                //When the command buffer executes, this will do a draw call on the GPU, without any vertex or index buffers. This is mainly useful on Shader Model 4.5 level hardware where shaders can read arbitrary data from ComputeBuffer buffers.
                cmd.DrawProcedural(Matrix4x4.identity, material, shaderPass, MeshTopology.Triangles, 3, 1);
            }
        }
    }
}
