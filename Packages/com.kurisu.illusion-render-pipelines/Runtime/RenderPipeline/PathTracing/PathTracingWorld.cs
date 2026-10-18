using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingWorld : IDisposable
    {
        private int _builtFrame = -1;

        private int _motionFrame = -1;

        private bool _gatherPending;

        private bool _historyAssigned;

        public PathTracingScene Scene { get; } = new();

        public PathTracingEmissiveTable Emissive { get; }

        public PathTracingMotionHistory Motion { get; }

        public PathTracingInlineScene Inline { get; } = new();

        public int LastUsedFrame { get; private set; }

        public PathTracingWorld(ComputeShader emissiveShader, ComputeShader motionShader)
        {
            Emissive = new PathTracingEmissiveTable(emissiveShader);
            Motion = new PathTracingMotionHistory(motionShader);
        }

        public void Update(Camera camera, int layerMask, bool trackMotion)
        {
            LastUsedFrame = PathTracingFrame.Index;
            bool collected = Scene.Update(camera, layerMask);
            if (collected)
            {
                Emissive.BeginFrame();
                if (Scene.Instances.Changed)
                {
                    Emissive.Update(Scene.Instances);
                    _historyAssigned = false;
                }
            }
            bool assign = false;
            if (trackMotion)
            {
                int motionFrame = Application.isPlaying ? Time.frameCount : PathTracingFrame.Index;
                if (_motionFrame != motionFrame)
                {
                    _motionFrame = motionFrame;
                    Motion.Advance(Scene.Instances);
                    _gatherPending = true;
                    assign = true;
                }
                else if (!_historyAssigned)
                {
                    Motion.Assign(Scene.Instances);
                    assign = true;
                }
                _historyAssigned = true;
            }
            if (assign)
                Scene.Instances.Upload();
        }

        public void Build(CommandBuffer cmd, int frame)
        {
            if (_builtFrame != frame)
            {
                _builtFrame = frame;
                cmd.BuildRayTracingAccelerationStructure(Scene.AccelerationStructure);
            }
            if (_gatherPending)
            {
                _gatherPending = false;
                Motion.Gather(cmd);
            }
        }

        public void UpdateInline()
        {
            Inline.Update(Scene.Instances, PathTracingFrame.Index);
        }

        public void BindInline(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            Inline.Bind(cmd, shader, kernel);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._PathTracingInstanceData, Scene.Instances.Buffer);
            cmd.SetComputeIntParam(shader, ShaderIDs._PathTracingOpaqueShadowLayers, unchecked((int)Scene.Instances.OpaqueShadowLayers));
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._PathTracingCulledSubMeshes, Scene.Instances.CulledSubMeshBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._PathTracingPreviousPositions, Motion.PreviousPositions);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._PathTracingMaterialRanges, Scene.Instances.MaterialTable.RangeBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs._PathTracingSubMeshMaterials, Scene.Instances.MaterialTable.SubMeshBuffer);
            Scene.Instances.MaterialTable.Bind(cmd, shader, kernel);
        }

        public void BindHitShaderGlobals(CommandBuffer cmd)
        {
            cmd.SetGlobalBuffer(ShaderIDs._PathTracingInstanceData, Scene.Instances.Buffer);
            cmd.SetGlobalInteger(ShaderIDs._PathTracingOpaqueShadowLayers, unchecked((int)Scene.Instances.OpaqueShadowLayers));
            cmd.SetGlobalBuffer(ShaderIDs._PathTracingCulledSubMeshes, Scene.Instances.CulledSubMeshBuffer);
            cmd.SetGlobalBuffer(ShaderIDs._PathTracingPreviousPositions, Motion.PreviousPositions);
            cmd.SetGlobalBuffer(ShaderIDs._PathTracingMaterialRanges, Scene.Instances.MaterialTable.RangeBuffer);
            cmd.SetGlobalBuffer(ShaderIDs._PathTracingSubMeshMaterials, Scene.Instances.MaterialTable.SubMeshBuffer);
            Scene.Instances.MaterialTable.BindHitShaderGlobals(cmd);
        }

        public void Dispose()
        {
            Scene.Dispose();
            Inline.Dispose();
            Emissive.Dispose();
            Motion.Dispose();
        }

        private static class ShaderIDs
        {
            public static readonly int _PathTracingInstanceData = Shader.PropertyToID("_PathTracingInstanceData");
            public static readonly int _PathTracingOpaqueShadowLayers = Shader.PropertyToID("_PathTracingOpaqueShadowLayers");
            public static readonly int _PathTracingCulledSubMeshes = Shader.PropertyToID("_PathTracingCulledSubMeshes");
            public static readonly int _PathTracingPreviousPositions = Shader.PropertyToID("_PathTracingPreviousPositions");
            public static readonly int _PathTracingMaterialRanges = Shader.PropertyToID("_PathTracingMaterialRanges");
            public static readonly int _PathTracingSubMeshMaterials = Shader.PropertyToID("_PathTracingSubMeshMaterials");
        }
    }
}
