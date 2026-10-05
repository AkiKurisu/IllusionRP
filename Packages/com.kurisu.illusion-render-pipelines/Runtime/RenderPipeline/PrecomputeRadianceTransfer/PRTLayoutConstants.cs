using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PRTGI
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PRTLayoutConstants
    {
        public Vector4 origin;
        public int4 gridMinimum;
        public int4 gridCount;
        public int4 windowMinimum;
        public int4 windowCount;
        public float spacing;
        public uint generation;
        public uint enabled;
        public uint padding;
        public const int Stride = 96;
        public static readonly int ShaderId = Shader.PropertyToID("PRTProbeVolumeConstants");

        public static PRTLayoutConstants Create(PRTProbeGrid grid, Vector3Int minimum, Vector3Int count, uint generation)
        {
            return new PRTLayoutConstants
            {
                origin = new Vector4(grid.origin.x, grid.origin.y, grid.origin.z, 0),
                gridMinimum = new int4(grid.min.x, grid.min.y, grid.min.z, 0),
                gridCount = new int4(grid.count.x, grid.count.y, grid.count.z, 0),
                windowMinimum = new int4(minimum.x, minimum.y, minimum.z, 0),
                windowCount = new int4(count.x, count.y, count.z, 0),
                spacing = grid.spacing,
                generation = generation,
                enabled = 1
            };
        }

        public static GraphicsBuffer Allocate()
        {
            return new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, Stride);
        }

        public static void Bind(CommandBuffer command, ComputeShader shader, GraphicsBuffer buffer)
        {
            command.SetComputeConstantBufferParam(shader, ShaderId, buffer, 0, Stride);
        }
    }

    internal static class PRTRelightFrame
    {
        private static Camera _driver;
        private static int _playerFrame = -1;
        private static uint _lastGameTick;
        private static bool _hasGameTick;
        public static uint Index { get; private set; }
        public static bool IsDriver(Camera camera) => camera == _driver;
        static PRTRelightFrame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += () => { if (!Application.isPlaying) Index++; };
#endif
            RenderPipelineManager.beginContextRendering += (_, cameras) =>
            {
                if (Application.isPlaying && _playerFrame != Time.frameCount)
                { _playerFrame = Time.frameCount; Index++; }
                _driver = null;
                Camera main = Camera.main;
                bool hasPrimary = main && main.isActiveAndEnabled && main.cameraType == CameraType.Game;
                foreach (var camera in cameras)
                    if (camera.cameraType == CameraType.Game && (!hasPrimary || camera == main) && !_driver) _driver = camera;
                if (_driver) { _lastGameTick = Index; _hasGameTick = true; }
                else if (!_hasGameTick || Index - _lastGameTick > 2)
                    foreach (var camera in cameras)
                        if (camera.cameraType == CameraType.SceneView) { _driver = camera; break; }
            };
        }
    }
}
