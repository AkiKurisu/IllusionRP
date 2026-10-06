using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PRTGI
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PRTLayoutConstants
    {
        public const int MaxCascades = 4;
        public Vector4 origin;
        public int4 gridMinimum;
        public int4 gridCount;
        public int4 slotCount;
        public float spacing;
        public uint generation;
        public uint enabled;
        public uint cascadeCount;
        public Vector4 cascadeCenter;
        public int4 cascadeMinimum0, cascadeMinimum1, cascadeMinimum2, cascadeMinimum3;
        public int4 cascadeCount0, cascadeCount1, cascadeCount2, cascadeCount3;
        public const int Stride = 224;
        public static readonly int ShaderId = Shader.PropertyToID("PRTProbeVolumeConstants");

        // Cascade windows are in level-local node coordinates; level c has spacing * 2^c. The count's w holds a mask of
        // the axes along which the window is smaller than its level and therefore scrolls with the camera.
        public static PRTLayoutConstants Create(PRTProbeGrid grid, Vector3Int slots, uint generation,
            Vector3 center = default, Vector3Int[] minimum = null, Vector3Int[] count = null, Vector3Int[] levels = null)
        {
            var constants = new PRTLayoutConstants
            {
                origin = new Vector4(grid.origin.x, grid.origin.y, grid.origin.z, 0),
                gridMinimum = new int4(grid.min.x, grid.min.y, grid.min.z, 0),
                gridCount = new int4(grid.count.x, grid.count.y, grid.count.z, 0),
                slotCount = new int4(slots.x, slots.y, slots.z, 0),
                spacing = grid.spacing,
                generation = generation,
                enabled = 1,
                cascadeCenter = center
            };
            if (minimum == null) return constants;
            constants.cascadeCount = (uint)minimum.Length;
            for (int i = 0; i < minimum.Length; i++)
            {
                var min = new int4(minimum[i].x, minimum[i].y, minimum[i].z, 0);
                int scrolls = (count[i].x < levels[i].x ? 1 : 0) | (count[i].y < levels[i].y ? 2 : 0) | (count[i].z < levels[i].z ? 4 : 0);
                var size = new int4(count[i].x, count[i].y, count[i].z, scrolls);
                switch (i)
                {
                    case 0: constants.cascadeMinimum0 = min; constants.cascadeCount0 = size; break;
                    case 1: constants.cascadeMinimum1 = min; constants.cascadeCount1 = size; break;
                    case 2: constants.cascadeMinimum2 = min; constants.cascadeCount2 = size; break;
                    default: constants.cascadeMinimum3 = min; constants.cascadeCount3 = size; break;
                }
            }
            return constants;
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
