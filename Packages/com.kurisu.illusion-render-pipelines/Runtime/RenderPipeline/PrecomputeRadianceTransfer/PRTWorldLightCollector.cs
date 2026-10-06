using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTWorldLightCollector
    {
        private readonly Dictionary<int, (int hash, uint epoch)> _epochs = new();
        private readonly HashSet<int> _reportedCookies = new();
        private uint _nextEpoch;

        internal PRTRelightLightingSnapshot Capture(PRTProbeVolumeAsset asset, bool enableShadows, bool fragmentBias)
        {
            var bounds = asset.GeometryBounds;
            var sceneLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);
            var lights = new List<PRTWorldLightSnapshot>();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool useLayers = pipeline && pipeline.useRenderingLayers;
            foreach (var light in sceneLights)
            {
                if (!light.isActiveAndEnabled || !light.gameObject.scene.IsValid()) continue;
                if (light.type != LightType.Directional && light.type != LightType.Point && light.type != LightType.Spot) continue;
                if (light.type != LightType.Directional && (light.range <= 0 || bounds.SqrDistance(light.transform.position) > light.range * light.range)) continue;
                if (light.cookie && _reportedCookies.Add(light.GetInstanceID()))
                    Debug.LogWarning($"[PRTGI] World relight does not evaluate the cookie on '{light.name}'.", light);
                var matrix = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one);
                UniversalRenderPipeline.GetLightAttenuationAndSpotDirection(light.type, light.range, matrix,
                    light.spotAngle, light.innerSpotAngle, out var attenuation, out _);
                uint renderingLayers = uint.MaxValue;
                uint shadowLayers = uint.MaxValue;
                bool hasData = light.TryGetComponent(out UniversalAdditionalLightData additional);
                if (useLayers)
                {
                    renderingLayers = hasData ? additional.renderingLayers : unchecked((uint)light.renderingLayerMask);
                    shadowLayers = hasData && additional.customShadowLayers ? additional.shadowRenderingLayers : renderingLayers;
                }
                Color color = FinalColor(light) * light.bounceIntensity;
                float biasScale = 1;
                if (pipeline.supportsSoftShadows && light.shadows == LightShadows.Soft)
                {
                    var quality = hasData ? additional.softShadowQuality : SoftShadowQuality.Medium;
                    biasScale = quality == SoftShadowQuality.High ? 3.5f : quality == SoftShadowQuality.Low ? 1.5f : 2.5f;
                }
                Vector3 position = light.transform.position;
                Vector3 direction = -light.transform.forward;
                lights.Add(new PRTWorldLightSnapshot
                {
                    Source = light,
                    LocalToWorld = matrix,
                    ShadowLayers = shadowLayers,
                    ShadowNear = light.shadowNearPlane,
                    DepthBias = (hasData && !additional.usePipelineSettings ? light.shadowBias : pipeline.shadowDepthBias) * biasScale,
                    NormalBias = (hasData && !additional.usePipelineSettings ? light.shadowNormalBias : pipeline.shadowNormalBias) * biasScale,
                    SpotAngle = light.spotAngle,
                    CastsShadows = enableShadows && light.shadows != LightShadows.None && light.shadowStrength > 0,
                    Gpu = new PRTWorldLightGpu
                    {
                        PositionType = new Vector4(position.x, position.y, position.z, Type(light.type)),
                        DirectionRange = new Vector4(direction.x, direction.y, direction.z, light.range),
                        ColorShadowStrength = new Vector4(color.r, color.g, color.b, light.shadowStrength),
                        Attenuation = attenuation,
                        RenderingLayers = renderingLayers,
                        ObjectLayers = unchecked((uint)light.cullingMask),
                        LightId = unchecked((uint)light.GetInstanceID()),
                        Flags = (enableShadows && light.shadows != LightShadows.None && light.shadowStrength > 0 ? 1u : 0u)
                            | (useLayers ? 4u : 0u)
                    }
                });
            }
            for (int i = 0; i < lights.Count; i++)
            {
                var item = lights[i];
                int id = item.Source.GetInstanceID();
                int shadowHash = HashCode.Combine(fragmentBias, item.LocalToWorld, item.ShadowLayers,
                    item.Gpu.ObjectLayers, item.ShadowNear, item.DepthBias, item.NormalBias,
                    HashCode.Combine(item.Gpu.PositionType.w, item.SpotAngle, item.Gpu.DirectionRange.w, item.CastsShadows));
                if (!_epochs.TryGetValue(id, out var previous) || previous.hash != shadowHash)
                {
                    // Cached visibility stores 24 epoch bits and reserves 0 for empty entries.
                    _nextEpoch = (_nextEpoch + 1) & 0xFFFFFFu;
                    if (_nextEpoch == 0) _nextEpoch = 1;
                    previous = (shadowHash, _nextEpoch);
                    _epochs[id] = previous;
                }
                item.Gpu.VisibilityEpoch = previous.epoch;
                lights[i] = item;
            }
            float sceneTime = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            return new PRTRelightLightingSnapshot(sceneTime, lights.ToArray());
        }

        private static float Type(LightType type) => type == LightType.Directional ? 0 : type == LightType.Point ? 1 : 2;

        private static Color FinalColor(Light light)
        {
            Color color;
            if (GraphicsSettings.lightsUseLinearIntensity)
            {
                color = light.color.linear * light.intensity;
                if (light.useColorTemperature)
                    color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            }
            else color = (light.color * light.intensity).linear;
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color : color.gamma;
        }
    }
}
