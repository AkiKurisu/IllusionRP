using UnityEngine;

namespace Illusion.Rendering
{
    internal static class WetSurfaceDecalProperties
    {
        private static readonly int WorldToLocalId = Shader.PropertyToID("_WetWorldToLocal");
        private static readonly int LocalToWorldId = Shader.PropertyToID("_WetLocalToWorld");
        private static readonly int LayerModeId = Shader.PropertyToID("_WetLayerMode");
        private static readonly int ProjectionModeId = Shader.PropertyToID("_WetProjectionMode");
        private static readonly int JitterId = Shader.PropertyToID("_WetSampleJitter");
        private static readonly int NoiseId = Shader.PropertyToID("_WetBlueNoise");
        private static readonly int WorldScaleId = Shader.PropertyToID("_WetWorldProjectionScale");
        private static readonly int SaturationId = Shader.PropertyToID("_WetSaturation");
        private static readonly int FadeoffId = Shader.PropertyToID("_WetEdgeFadeoff");
        private static readonly int SharpnessId = Shader.PropertyToID("_WetFaceSharpness");
        private static readonly int[][] LayerIds = { CreateLayerIds("X"), CreateLayerIds("Y"), CreateLayerIds("Z") };

        private static int[] CreateLayerIds(string axis) => new[]
        {
            Shader.PropertyToID("_Wet" + axis + "Layer"), Shader.PropertyToID("_Wet" + axis + "ScaleOffset"),
            Shader.PropertyToID("_Wet" + axis + "InputStart"), Shader.PropertyToID("_Wet" + axis + "InputExtent"),
            Shader.PropertyToID("_Wet" + axis + "OutputStart"), Shader.PropertyToID("_Wet" + axis + "OutputEnd")
        };

        internal static void Apply(MaterialPropertyBlock properties, in WetSurfaceDecalData decal,
            Texture2D blueNoise, float worldProjectionScale)
        {
            properties.SetMatrix(WorldToLocalId, decal.WorldToLocal);
            properties.SetMatrix(LocalToWorldId, decal.LocalToWorld);
            properties.SetInteger(LayerModeId, (int)decal.LayerMode);
            properties.SetInteger(ProjectionModeId, (int)decal.ProjectionMode);
            properties.SetFloat(WorldScaleId, worldProjectionScale);
            properties.SetFloat(SaturationId, decal.Saturation);
            properties.SetFloat(FadeoffId, decal.EdgeFadeoff);
            properties.SetFloat(SharpnessId, decal.FaceSharpness);
            properties.SetTexture(NoiseId, blueNoise);
            ApplyLayer(properties, LayerIds[0], decal.XLayer);
            ApplyLayer(properties, LayerIds[1], decal.YLayer);
            ApplyLayer(properties, LayerIds[2], decal.ZLayer);

            Vector2? jitter = null;
            if (decal.LayerMode == WetSurfaceLayerMode.Triplanar)
            {
                AccumulateJitter(ref jitter, decal.SampleJitter, decal.XLayer.LayerMask);
                AccumulateJitter(ref jitter, decal.SampleJitter, decal.ZLayer.LayerMask);
            }
            if (decal.LayerMode != WetSurfaceLayerMode.None)
                AccumulateJitter(ref jitter, decal.SampleJitter, decal.YLayer.LayerMask);
            properties.SetVector(JitterId, decal.EnableJitter && jitter.HasValue
                ? new Vector4(jitter.Value.x, jitter.Value.y, 0f, 0f) : Vector4.zero);
        }

        private static void AccumulateJitter(ref Vector2? jitter, float amount, Texture2D texture)
        {
            if (!texture) return;
            Vector2 value = new(amount / texture.width, amount / texture.height);
            jitter = jitter.HasValue ? Vector2.Min(jitter.Value, value) : value;
        }

        private static void ApplyLayer(MaterialPropertyBlock properties, int[] ids, in WetSurfaceDecalLayerData layer)
        {
            properties.SetTexture(ids[0], layer.LayerMask ? layer.LayerMask : Texture2D.whiteTexture);
            properties.SetVector(ids[1], layer.LayerScaleOffset);
            properties.SetVector(ids[2], layer.InputStart);
            properties.SetVector(ids[3], layer.InputExtent);
            properties.SetVector(ids[4], layer.OutputStart);
            properties.SetVector(ids[5], layer.OutputEnd);
        }
    }
}
