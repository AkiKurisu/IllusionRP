using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering
{
    public enum WetSurfaceLayerMode { None = 0, Single = 1, Triplanar = 2 }
    public enum WetSurfaceProjectionMode { Local = 0, World = 1 }

    public readonly struct WetSurfaceDecalLayerData
    {
        public readonly Texture2D LayerMask;
        public readonly Vector4 LayerScaleOffset;
        public readonly Vector4 InputStart;
        public readonly Vector4 InputExtent;
        public readonly Vector4 OutputStart;
        public readonly Vector4 OutputEnd;

        public WetSurfaceDecalLayerData(Texture2D layerMask, Vector4 layerScaleOffset, Vector4 inputStart,
            Vector4 inputExtent, Vector4 outputStart, Vector4 outputEnd)
        {
            LayerMask = layerMask;
            LayerScaleOffset = layerScaleOffset;
            InputStart = inputStart;
            InputExtent = inputExtent;
            OutputStart = outputStart;
            OutputEnd = outputEnd;
        }
    }

    public readonly struct WetSurfaceDecalData
    {
        public readonly Matrix4x4 WorldToLocal;
        public readonly Matrix4x4 LocalToWorld;
        public readonly WetSurfaceLayerMode LayerMode;
        public readonly WetSurfaceProjectionMode ProjectionMode;
        public readonly WetSurfaceDecalLayerData XLayer;
        public readonly WetSurfaceDecalLayerData YLayer;
        public readonly WetSurfaceDecalLayerData ZLayer;
        public readonly float Saturation;
        public readonly float EdgeFadeoff;
        public readonly float FaceSharpness;
        public readonly bool IsDry;
        public readonly bool IsSphere;
        public readonly bool EnableJitter;
        public readonly float SampleJitter;
        public readonly float WorldProjectionScale;

        public WetSurfaceDecalData(Matrix4x4 worldToLocal, Matrix4x4 localToWorld,
            WetSurfaceLayerMode layerMode, WetSurfaceProjectionMode projectionMode,
            WetSurfaceDecalLayerData xLayer, WetSurfaceDecalLayerData yLayer, WetSurfaceDecalLayerData zLayer,
            float saturation, float edgeFadeoff, float faceSharpness, bool enableJitter, float sampleJitter,
            bool isDry, bool isSphere, float worldProjectionScale = 1f)
        {
            WorldToLocal = worldToLocal;
            LocalToWorld = localToWorld;
            LayerMode = layerMode;
            ProjectionMode = projectionMode;
            XLayer = xLayer;
            YLayer = yLayer;
            ZLayer = zLayer;
            Saturation = saturation;
            EdgeFadeoff = edgeFadeoff;
            FaceSharpness = faceSharpness;
            IsDry = isDry;
            IsSphere = isSphere;
            EnableJitter = enableJitter;
            SampleJitter = sampleJitter;
            WorldProjectionScale = worldProjectionScale;
        }
    }

    public interface IWetSurfaceDecal
    {
        bool TryGetWetSurfaceData(out WetSurfaceDecalData data);
    }

    public static class WetSurfaceDecalRegistry
    {
        private static readonly HashSet<IWetSurfaceDecal> Decals = new();

        public static void Register(IWetSurfaceDecal decal) => Decals.Add(decal);

        public static void Unregister(IWetSurfaceDecal decal) => Decals.Remove(decal);

        public static void Collect(List<WetSurfaceDecalData> output)
        {
            output.Clear();
            foreach (IWetSurfaceDecal decal in Decals)
            {
                if (decal is Object owner && !owner)
                    continue;
                if (decal != null && decal.TryGetWetSurfaceData(out WetSurfaceDecalData data))
                    output.Add(data);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Decals.Clear();
    }
}
