using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingResources : IDisposable
    {
        private readonly List<RenderTexture> _textures = new();

        private readonly List<GraphicsBuffer> _buffers = new();

        private readonly PathTracingSampleConstants[] _constants = new PathTracingSampleConstants[1];

        private readonly PathTracingSampleMiniConstants[] _miniConstants = new PathTracingSampleMiniConstants[1];

        private readonly Shader _fgdShader;
        private Material _fgdMaterial;
        private RenderTexture _fgd;
        private bool _fgdReady;

        public RenderTexture GetFGD(CommandBuffer commandBuffer)
        {
            if (!_fgd)
            {
                var shader = _fgdShader;
                if (!shader) throw new InvalidOperationException("Path tracing FGD shader is missing.");
                _fgdMaterial = CoreUtils.CreateEngineMaterial(shader);
                _fgd = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "PathTracingFGD",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _fgd.Create();
                _textures.Add(_fgd);
            }
            if (!_fgdReady)
            {
                commandBuffer.SetRenderTarget(_fgd);
                commandBuffer.DrawProcedural(Matrix4x4.identity, _fgdMaterial, 0, MeshTopology.Triangles, 3);
                _fgdReady = true;
            }
            return _fgd;
        }

        public GraphicsBuffer Constants { get; }

        public GraphicsBuffer MiniConstants { get; }

        public RenderTexture DummyFloat4 { get; }

        public RenderTexture DummyFloat2 { get; }

        public RenderTexture DummyFloat { get; }

        public RenderTexture DummyUIntArray { get; }

        public GraphicsBuffer DummyStructured { get; }

        public PathTracingResources(Shader fgdShader)
        {
            _fgdShader = fgdShader;
            Constants = CreateBuffer(GraphicsBuffer.Target.Structured, 1, PathTracingSampleConstants.Stride);
            MiniConstants = CreateBuffer(GraphicsBuffer.Target.Structured, 1, PathTracingSampleMiniConstants.Stride);
            DummyStructured = CreateBuffer(GraphicsBuffer.Target.Structured, 1, 256);

            DummyFloat4 = CreateTexture(GraphicsFormat.R32G32B32A32_SFloat, TextureDimension.Tex2D);
            DummyFloat2 = CreateTexture(GraphicsFormat.R32G32_SFloat, TextureDimension.Tex2D);
            DummyFloat = CreateTexture(GraphicsFormat.R32_SFloat, TextureDimension.Tex2D);
            DummyUIntArray = CreateTexture(GraphicsFormat.R32_UInt, TextureDimension.Tex2DArray);
        }

        public void UploadConstants(CommandBuffer cmd, in PathTracingSampleConstants constants, in PathTracingSampleMiniConstants miniConstants)
        {
            _constants[0] = constants;
            _miniConstants[0] = miniConstants;
            cmd.SetBufferData(Constants, _constants);
            cmd.SetBufferData(MiniConstants, _miniConstants);
        }

        private GraphicsBuffer CreateBuffer(GraphicsBuffer.Target target, int count, int stride)
        {
            var buffer = new GraphicsBuffer(target, count, stride);
            buffer.SetData(new byte[count * stride]);
            _buffers.Add(buffer);
            return buffer;
        }

        private RenderTexture CreateTexture(GraphicsFormat format, TextureDimension dimension)
        {
            var texture = new RenderTexture(new RenderTextureDescriptor(1, 1, format, GraphicsFormat.None)
            {
                dimension = dimension,
                volumeDepth = 1,
                enableRandomWrite = true,
                msaaSamples = 1
            })
            {
                name = "PathTracingPlaceholder",
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            _textures.Add(texture);
            return texture;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_fgdMaterial);
            foreach (var buffer in _buffers)
                buffer.Release();
            _buffers.Clear();
            foreach (var texture in _textures)
                CoreUtils.Destroy(texture);
            _textures.Clear();
        }
    }
}
