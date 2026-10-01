using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Illusion.Rendering.AreaLights
{
    public partial class IllusionAdditionalLightData
    {
        private const string EmissiveMeshName = "EmissiveMesh";

        private const HideFlags EmissiveMeshHideFlags = HideFlags.NotEditable | HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;

        private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");

        private static readonly int EmissiveColorMapId = Shader.PropertyToID("_EmissiveColorMap");

        private GameObject _emissiveMesh;

        private MeshRenderer _emissiveMeshRenderer;

        private Material _emissiveMeshMaterial;

        private void OnEnable()
        {
            UpdateEmissiveMesh();
        }

        private void OnDisable()
        {
            if (_emissiveMeshRenderer)
                _emissiveMeshRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            CoreUtils.Destroy(_emissiveMeshMaterial);
        }

        private void Update()
        {
            UpdateEmissiveMesh();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EditorApplication.delayCall += () =>
            {
                if (this)
                    UpdateEmissiveMesh();
            };
        }
#endif

        private void UpdateEmissiveMesh()
        {
            var light = attachedLight;
            if (!m_DisplayAreaLightEmissiveMesh || !light || light.type != LightType.Rectangle)
            {
                DestroyEmissiveMesh();
                return;
            }
            if (!FindOrCreateEmissiveMesh())
                return;

            var meshTransform = _emissiveMesh.transform;
            var lossyScale = transform.lossyScale;
            meshTransform.localPosition = Vector3.zero;
            meshTransform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
            meshTransform.localScale = new Vector3(light.areaSize.x / lossyScale.x, light.areaSize.y / lossyScale.y, 1.0f);
            _emissiveMesh.layer = gameObject.layer;
            _emissiveMeshRenderer.enabled = isActiveAndEnabled && light.enabled;

            _emissiveMeshMaterial.SetColor(EmissiveColorId, Radiance(light) * m_LightDimmer);
            _emissiveMeshMaterial.SetTexture(EmissiveColorMapId, m_AreaLightCookie ? m_AreaLightCookie : Texture2D.whiteTexture);
        }

        private static Color Radiance(Light light)
        {
            Color color = light.color.linear * (light.intensity * Mathf.PI);
            if (GraphicsSettings.lightsUseColorTemperature && light.useColorTemperature)
                color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            color.a = 1.0f;
            return color;
        }

        private bool FindOrCreateEmissiveMesh()
        {
            if (_emissiveMesh && _emissiveMeshMaterial)
                return true;
#if UNITY_EDITOR
            if (PrefabUtility.IsPartOfPrefabAsset(this))
                return false;
#endif
            var shader = Resources.Load<IllusionRenderPipelineResources>(nameof(IllusionRenderPipelineResources))?.areaLightEmissiveMeshShader;
            if (!shader)
                return false;

            if (!_emissiveMesh)
            {
                foreach (Transform child in transform)
                {
                    if (child.name == EmissiveMeshName && child.hideFlags == EmissiveMeshHideFlags && child.TryGetComponent(out MeshRenderer _))
                        _emissiveMesh = child.gameObject;
                }
            }
            if (!_emissiveMesh)
            {
                _emissiveMesh = new GameObject(EmissiveMeshName, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = EmissiveMeshHideFlags };
                _emissiveMesh.transform.SetParent(transform, false);
            }

            _emissiveMesh.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            _emissiveMeshRenderer = _emissiveMesh.GetComponent<MeshRenderer>();
            _emissiveMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _emissiveMeshRenderer.receiveShadows = false;
            _emissiveMeshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _emissiveMeshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var material = _emissiveMeshRenderer.sharedMaterial;
            _emissiveMeshMaterial = material && material.shader == shader ? material : new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            _emissiveMeshRenderer.sharedMaterial = _emissiveMeshMaterial;
            _emissiveMeshRenderer.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
            return true;
        }

        private void DestroyEmissiveMesh()
        {
            CoreUtils.Destroy(_emissiveMesh);
            CoreUtils.Destroy(_emissiveMeshMaterial);
            _emissiveMesh = null;
            _emissiveMeshRenderer = null;
            _emissiveMeshMaterial = null;
        }
    }
}
