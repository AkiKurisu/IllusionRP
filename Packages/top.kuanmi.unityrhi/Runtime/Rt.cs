using System;

namespace UnityRhi
{
    public static class Rt
    {
        [Flags]
        public enum GeometryFlags : uint
        {
            None = 0,
            Opaque = 1,
            NoDuplicateAnyHitInvocation = 2,
        }

        public enum GeometryType : uint
        {
            Triangles = 0,
            AABBs = 1,
        }

        [Flags]
        public enum InstanceFlags : uint
        {
            None = 0,
            TriangleCullDisable = 1,
            TriangleFrontCounterclockwise = 2,
            ForceOpaque = 4,
            ForceNonOpaque = 8,
        }

        [Flags]
        public enum AccelStructBuildFlags : uint
        {
            None = 0,
            AllowUpdate = 1,
            AllowCompaction = 2,
            PreferFastTrace = 4,
            PreferFastBuild = 8,
            MinimizeMemory = 0x10,
            PerformUpdate = 0x20,
            AllowEmptyInstances = 0x80,
        }

        public struct GeometryTriangles
        {
            public Buffer IndexBuffer;
            public Buffer VertexBuffer;
            public Format IndexFormat;
            public Format VertexFormat;
            public ulong IndexOffset;
            public ulong VertexOffset;
            public uint IndexCount;
            public uint VertexCount;
            public uint VertexStride;
        }

        public struct GeometryAABBs
        {
            public Buffer Buffer;
            public ulong Offset;
            public uint Count;
            public uint Stride;
        }

        public struct GeometryDesc
        {
            public GeometryTriangles Triangles;
            public GeometryAABBs AABBs;
            public bool UseTransform;
            public float[] Transform; // 3x4 row-major, 12 floats
            public GeometryFlags Flags;
            public GeometryType GeometryType;

            public static GeometryDesc TrianglesDesc(GeometryTriangles triangles, GeometryFlags flags = GeometryFlags.Opaque) =>
                new GeometryDesc { Triangles = triangles, GeometryType = GeometryType.Triangles, Flags = flags };

            public static GeometryDesc AABBsDesc(GeometryAABBs aabbs, GeometryFlags flags = GeometryFlags.None) =>
                new GeometryDesc { AABBs = aabbs, GeometryType = GeometryType.AABBs, Flags = flags };
        }

        /// <summary>Inline 3x4 row-major affine transform, matching nvrhi::rt::AffineTransform.</summary>
        public struct AffineTransform
        {
            public float M00, M01, M02, M03;
            public float M10, M11, M12, M13;
            public float M20, M21, M22, M23;

            public AffineTransform(
                float m00, float m01, float m02, float m03,
                float m10, float m11, float m12, float m13,
                float m20, float m21, float m22, float m23)
            {
                M00 = m00; M01 = m01; M02 = m02; M03 = m03;
                M10 = m10; M11 = m11; M12 = m12; M13 = m13;
                M20 = m20; M21 = m21; M22 = m22; M23 = m23;
            }

            // Keeps existing callers source-compatible while allowing hot paths
            // to construct the transform inline without allocating float[12].
            public static implicit operator AffineTransform(float[] values)
            {
                if (values == null || values.Length < 12)
                    return default;
                return new AffineTransform(
                    values[0], values[1], values[2], values[3],
                    values[4], values[5], values[6], values[7],
                    values[8], values[9], values[10], values[11]);
            }
        }

        public struct InstanceDesc
        {
            public AffineTransform Transform;
            public uint InstanceID;
            public uint InstanceMask;
            public uint InstanceContributionToHitGroupIndex;
            public InstanceFlags Flags;
            public AccelStruct BottomLevelAS;
        }

        public sealed class AccelStructDesc
        {
            public ulong TopLevelMaxInstances;
            public AccelStructBuildFlags BuildFlags = AccelStructBuildFlags.None;
            public bool IsTopLevel;
            public bool IsVirtual;
            public GeometryDesc[] BottomLevelGeometries = Array.Empty<GeometryDesc>();
            public string DebugName = "";
        }

        public struct PipelineShaderDesc
        {
            public string ExportName;
            public Shader Shader;
            public BindingLayout BindingLayout;
        }

        public struct PipelineHitGroupDesc
        {
            public string ExportName;
            public Shader ClosestHitShader;
            public Shader AnyHitShader;
            public Shader IntersectionShader;
            public BindingLayout BindingLayout;
            public bool IsProceduralPrimitive;
        }

        public sealed class PipelineDesc
        {
            public uint MaxPayloadSize;
            public uint MaxAttributeSize = 8;
            public uint MaxRecursionDepth = 1;
            public bool AllowOpacityMicromaps;
            public int HlslExtensionsUAV = -1;
            public PipelineShaderDesc[] Shaders = Array.Empty<PipelineShaderDesc>();
            public PipelineHitGroupDesc[] HitGroups = Array.Empty<PipelineHitGroupDesc>();
            public BindingLayout[] GlobalBindingLayouts = Array.Empty<BindingLayout>();
            public string DebugName = "";
        }

        public sealed class ShaderTableDesc
        {
            public bool IsCached;
            public uint MaxEntries;
            public string DebugName = "";
        }

        public struct State
        {
            public ShaderTable ShaderTable;
            public Resource[] Bindings;
        }

        public struct DispatchRaysArguments
        {
            public uint Width;
            public uint Height;
            public uint Depth;
        }
    }
}
