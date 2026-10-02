using System;
using System.Collections.Generic;
using Illusion.Rendering.AreaLights;
using Illusion.Rendering.Shadows;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingLightCollector
    {
        public const int MaxDirectionalLights = 16;
        private readonly List<Light> _sceneLights = new();
        private readonly List<Light> _rectangles = new();
        private readonly HashSet<int> _reportedUnsupported = new();
        private readonly Dictionary<int, int> _targetIndices = new();
        private readonly Comparison<Light> _byInstanceId = (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID());
        public readonly List<PolymorphicLightInfo> Lights = new();
        public readonly List<PolymorphicLightInfoEx> LightsEx = new();
        public readonly List<int> LightIds = new();
        public readonly List<PathTracingDistantLight> DistantLights = new();
        public readonly List<PathTracingLightTarget> Targets = new();
        public IReadOnlyList<Light> RectangleLights => _rectangles;
        public Light Sun { get; private set; }
        public int Hash { get; private set; }
        private int _collectedFrame = -1;
        private float _collectedAngularDiameter;
        private Light _collectedPerObjectSelector;
        private uint _collectedPerObjectLayers;

        public void Collect(float directionalAngularDiameter, Light perObjectSelector, uint perObjectLayers)
        {
            if (_collectedFrame == PathTracingFrame.Index && _collectedAngularDiameter == directionalAngularDiameter
                && _collectedPerObjectSelector == perObjectSelector && _collectedPerObjectLayers == perObjectLayers)
                return;
            _collectedFrame = PathTracingFrame.Index;
            _collectedAngularDiameter = directionalAngularDiameter;
            _collectedPerObjectSelector = perObjectSelector;
            _collectedPerObjectLayers = perObjectLayers;
            _sceneLights.Clear();
            _sceneLights.AddRange(Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            _sceneLights.Sort(_byInstanceId);
            Lights.Clear(); LightsEx.Clear(); LightIds.Clear(); DistantLights.Clear(); _rectangles.Clear();
            Targets.Clear(); _targetIndices.Clear();
            Targets.Add(PathTracingLightTarget.Everything);
            bool renderingLayers = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset { useRenderingLayers: true };
            Light brightest = null;
            foreach (var light in _sceneLights)
            {
                if (!light.isActiveAndEnabled || light.intensity <= 0)
                    continue;
                ReportUnsupported(light);
                uint target = (uint)Targets.Count;
                switch (light.type)
                {
                    case LightType.Directional:
                        if (DistantLights.Count == MaxDirectionalLights)
                            throw new InvalidOperationException("Path tracing exceeds the 16-directional-light limit.");
                        AddTarget(light, renderingLayers);
                        var transform = light.transform;
                        DistantLights.Add(new PathTracingDistantLight
                        {
                            Forward = transform.forward, Right = transform.right, Up = transform.up,
                            Color = PhysicalColor(light),
                            AngularDiameter = Mathf.Max(0, directionalAngularDiameter * Mathf.Deg2Rad),
                            TargetIndex = target
                        });
                        if (!brightest || light.intensity > brightest.intensity) brightest = light;
                        break;
                    case LightType.Rectangle:
                        AddTarget(light, renderingLayers);
                        _rectangles.Add(light);
                        break;
                    case LightType.Point:
                    case LightType.Spot:
                        AddTarget(light, renderingLayers);
                        var info = ConvertPunctual(light, out var extended);
                        extended.UniqueID = (uint)light.GetInstanceID();
                        extended.TargetIndex = target;
                        Lights.Add(info); LightsEx.Add(extended); LightIds.Add(light.GetInstanceID());
                        break;
                }
            }
            var sun = RenderSettings.sun;
            Sun = sun && sun.isActiveAndEnabled && sun.type == LightType.Directional && sun.intensity > 0 ? sun : brightest;
            // Renderers on the per-object shadow layer also shadow the light that owns per-object shadows, as its per-object atlas does in raster.
            var perObjectSource = perObjectSelector ? perObjectSelector : Sun;
            if (perObjectLayers != 0 && PerObjectShadowLightData.IsUsableDirectional(perObjectSource)
                && _targetIndices.TryGetValue(perObjectSource.GetInstanceID(), out int sourceTarget))
            {
                var target = Targets[sourceTarget];
                target.ShadowLayers |= perObjectLayers;
                Targets[sourceTarget] = target;
            }
            int hash = HashCode.Combine(Lights.Count, DistantLights.Count, Targets.Count);
            foreach (var distant in DistantLights) hash = HashCode.Combine(hash, distant);
            foreach (var target in Targets) hash = HashCode.Combine(hash, target);
            for (int i = 0; i < Lights.Count; i++) hash = HashCode.Combine(hash, Lights[i], LightsEx[i]);
            foreach (var rectangle in _rectangles)
                hash = HashCode.Combine(hash, rectangle.GetInstanceID(), rectangle.transform.localToWorldMatrix, rectangle.areaSize, RectangleRadiance(rectangle));
            Hash = hash;
        }

        public bool HasDirectionalLights => DistantLights.Count > 0;

        public int GetTargetIndex(Light light) => _targetIndices.TryGetValue(light.GetInstanceID(), out int index) ? index : 0;

        private void AddTarget(Light light, bool renderingLayers)
        {
            var target = PathTracingLightTarget.Everything;
            if (renderingLayers)
            {
                bool hasData = light.TryGetComponent(out UniversalAdditionalLightData data);
                target.RenderingLayers = hasData ? data.renderingLayers : unchecked((uint)light.renderingLayerMask);
                target.ShadowLayers = hasData && data.customShadowLayers ? data.shadowRenderingLayers : target.RenderingLayers;
            }
            _targetIndices.Add(light.GetInstanceID(), Targets.Count);
            Targets.Add(target);
        }

        private void ReportUnsupported(Light light)
        {
            bool pointOrSpot = light.type == LightType.Point || light.type == LightType.Spot;
            bool supported = pointOrSpot || light.type == LightType.Directional || light.type == LightType.Rectangle;
            if (!supported) return;
            bool cookie = light.cookie || (light.TryGetComponent(out IllusionAdditionalLightData data) && data.areaLightCookie);
            if ((pointOrSpot || cookie) && _reportedUnsupported.Add(light.GetInstanceID()))
                Debug.LogWarning($"[PathTracing] Light '{light.name}' ignores Unity range attenuation and cookies in path tracing.", light);
        }

        private static PolymorphicLightInfo ConvertPunctual(Light light, out PolymorphicLightInfoEx shaping)
        {
            float radius = light.TryGetComponent(out IllusionAdditionalLightData data) ? data.shapeRadius : 0;
            var intensity = PhysicalColor(light);
            var info = new PolymorphicLightInfo { Center = light.transform.position };
            if (radius > 0)
            {
                info.ColorTypeAndFlags = PathTracingLightPacking.TypeBits(PolymorphicLightType.Sphere);
                info.Scalars = PathTracingLightPacking.Half(radius);
                PathTracingLightPacking.PackColor(intensity / (Mathf.PI * radius * radius), ref info);
            }
            else
            {
                info.ColorTypeAndFlags = PathTracingLightPacking.TypeBits(PolymorphicLightType.Point);
                info.Direction1 = PathTracingLightPacking.PackDirection(light.transform.forward);
                info.Direction2 = PathTracingLightPacking.Halves(Mathf.PI, 0);
                PathTracingLightPacking.PackColor(intensity, ref info);
            }
            shaping = default;
            if (light.type == LightType.Spot)
            {
                float outer = 0.5f * light.spotAngle * Mathf.Deg2Rad;
                float inner = 0.5f * light.innerSpotAngle * Mathf.Deg2Rad;
                info.Direction2 = PathTracingLightPacking.Halves(outer, inner);
                info.ColorTypeAndFlags |= PathTracingLightPacking.ShapingBit;
                shaping.PrimaryAxis = PathTracingLightPacking.PackDirection(light.transform.forward);
                shaping.CosConeAngleAndSoftness = PathTracingLightPacking.Halves(Mathf.Cos(outer), Mathf.Max(0, Mathf.Cos(inner) - Mathf.Cos(outer)));
            }
            return info;
        }

        internal static Vector3 RectangleRadiance(Light light)
        {
            float dimmer = light.TryGetComponent(out IllusionAdditionalLightData data) ? data.lightDimmer : 1;
            return PhysicalColor(light) * dimmer;
        }

        internal static Vector3 PhysicalColor(Light light)
        {
            Color color = light.color.linear * light.intensity;
            if (GraphicsSettings.lightsUseColorTemperature && light.useColorTemperature)
                color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            return new Vector3(color.r, color.g, color.b) * Mathf.PI;
        }
    }
}
