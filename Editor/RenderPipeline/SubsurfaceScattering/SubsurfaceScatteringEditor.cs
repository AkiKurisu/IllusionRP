using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(SubsurfaceScattering))]
    public sealed class SubsurfaceScatteringEditor : VolumeComponentEditor
    {
        private SerializedDataParameter _enable;
        
        private SerializedDataParameter _sampleBudget;
        
        private SerializedDataParameter _diffusionProfiles;
        
        private readonly DiffusionProfileSettingsListUI _listUI = new();

        public override void OnEnable()
        {
            var propertyFetcher = new PropertyFetcher<SubsurfaceScattering>(serializedObject);

            _enable = Unpack(propertyFetcher.Find(ss => ss.enable));
            _diffusionProfiles = Unpack(propertyFetcher.Find(x => x.diffusionProfiles));
            _sampleBudget = Unpack(propertyFetcher.Find(ss => ss.sampleBudget));
        }

        public override void OnInspectorGUI()
        {
            bool isEnabled = _enable.overrideState.boolValue && _enable.value.boolValue;

            PropertyField(_enable);

            if (!isEnabled) return;
            
            EditorGUILayout.Space();
            _listUI.drawElement = DrawDiffusionProfileElement;
            _listUI.OnGUI(_diffusionProfiles.value);
            
            using (new EditorGUI.DisabledScope(!IsPreferComputeShader()))
            {
                PropertyField(_sampleBudget);
            }
        }

        private void DrawDiffusionProfileElement(SerializedProperty element, Rect rect, int index)
        {
            EditorGUI.BeginDisabledGroup(!_diffusionProfiles.overrideState.boolValue);
            EditorGUI.ObjectField(rect, element, new GUIContent("Profile " + index));
            EditorGUI.EndDisabledGroup();
        }

        private static bool IsPreferComputeShader()
        {
            return IllusionRendererData.Active != null ? IllusionRendererData.Active.PreferComputeShader : SystemInfo.supportsComputeShaders;
        }
    }
}
