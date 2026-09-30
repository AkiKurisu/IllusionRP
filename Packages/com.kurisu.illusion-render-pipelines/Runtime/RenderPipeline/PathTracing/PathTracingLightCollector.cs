using System;
using System.Collections.Generic;
using Illusion.Rendering.AreaLights;
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
        private readonly Comparison<Light> _byInstanceId = (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID());
        public readonly List<PolymorphicLightInfo> Lights = new();
        public readonly List<PolymorphicLightInfoEx> LightsEx = new();
        public readonly List<int> LightIds = new();
        public readonly List<PathTracingDirectionalLight> DirectionalLights = new();
        public IReadOnlyList<Light> RectangleLights => _rectangles;
        public Light Sun { get; private set; }
        public int Hash { get; private set; }
        public int DirectionalHash { get; private set; }
        private int _collectedFrame = -1;
        private float _collectedAngularDiameter;

        public void Collect(float directionalAngularDiameter)
        {
            if (_collectedFrame == PathTracingFrame.Index && _collectedAngularDiameter == directionalAngularDiameter)
                return;
            _collectedFrame = PathTracingFrame.Index;
            _collectedAngularDiameter = directionalAngularDiameter;
            _sceneLights.Clear();
            _sceneLights.AddRange(Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            _sceneLights.Sort(_byInstanceId);
            Lights.Clear(); LightsEx.Clear(); LightIds.Clear(); DirectionalLights.Clear(); _rectangles.Clear();
            Light brightest = null;
            foreach (var light in _sceneLights)
            {
                if (!light.isActiveAndEnabled || light.intensity <= 0)
                    continue;
                ReportUnsupported(light);
                switch (light.type)
                {
                    case LightType.Directional:
                        if (DirectionalLights.Count == MaxDirectionalLights)
                            throw new InvalidOperationException("Path tracing environment exceeds the 16-directional-light limit.");
                        var color = PhysicalColor(light);
                        DirectionalLights.Add(new PathTracingDirectionalLight
                        {
                            ColorIntensity = new Vector4(color.x, color.y, color.z, 1),
                            Direction = light.transform.forward,
                            AngularSize = Mathf.Max(directionalAngularDiameter * Mathf.Deg2Rad, Mathf.PI / (PathTracingEnvironment.CubeSize / 2.0f))
                        });
                        if (!brightest || light.intensity > brightest.intensity) brightest = light;
                        break;
                    case LightType.Rectangle:
                        _rectangles.Add(light);
                        break;
                    case LightType.Point:
                    case LightType.Spot:
                        var info = ConvertPunctual(light, out var extended);
                        extended.UniqueID = (uint)light.GetInstanceID();
                        Lights.Add(info); LightsEx.Add(extended); LightIds.Add(light.GetInstanceID());
                        break;
                }
            }
            var sun = RenderSettings.sun;
            Sun = sun && sun.isActiveAndEnabled && sun.type == LightType.Directional && sun.intensity > 0 ? sun : brightest;
            int directionalHash = DirectionalLights.Count;
            foreach (var directional in DirectionalLights) directionalHash = HashCode.Combine(directionalHash, directional);
            DirectionalHash = directionalHash;
            int hash = HashCode.Combine(Lights.Count, DirectionalHash);
            for (int i = 0; i < Lights.Count; i++) hash = HashCode.Combine(hash, Lights[i], LightsEx[i]);
            foreach (var rectangle in _rectangles)
                hash = HashCode.Combine(hash, rectangle.GetInstanceID(), rectangle.transform.localToWorldMatrix, rectangle.areaSize, RectangleRadiance(rectangle));
            Hash = hash;
        }

        private void ReportUnsupported(Light light)
        {
            bool pointOrSpot = light.type == LightType.Point || light.type == LightType.Spot;
            bool supported = pointOrSpot || light.type == LightType.Directional || light.type == LightType.Rectangle;
            if (!supported) return;
            bool layers = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset { useRenderingLayers: true } || light.cullingMask != -1;
            bool cookie = light.cookie || (light.TryGetComponent(out IllusionAdditionalLightData data) && data.areaLightCookie);
            if ((pointOrSpot || layers || cookie) && _reportedUnsupported.Add(light.GetInstanceID()))
                Debug.LogWarning($"[PathTracing] Light '{light.name}' ignores Unity range attenuation, cookies and light-layer masks in path tracing.", light);
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
