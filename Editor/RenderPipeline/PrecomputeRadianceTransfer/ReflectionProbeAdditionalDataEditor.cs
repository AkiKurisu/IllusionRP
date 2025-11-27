using Illusion.Rendering.PRTGI;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(ReflectionProbeAdditionalData))]
    internal class ReflectionProbeAdditionalDataEditor : PropertyFetchEditor<ReflectionProbeAdditionalData>
    {
        private SerializedProperty _hasValidSHForNormalization;
        private SerializedProperty _shForNormalization;

        protected override void OnEnable()
        {
            base.OnEnable();
            _hasValidSHForNormalization = Properties.Find(data => data.hasValidSHForNormalization);
            _shForNormalization = Properties.Find(data => data.shForNormalization);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_hasValidSHForNormalization.boolValue)
            {
                EditorGUILayout.HelpBox("This reflection probe has valid spherical harmonics coefficients for normalization.", MessageType.Info);

                // Display SH coefficients in a read-only format
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Spherical Harmonics Coefficients", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.PropertyField(_shForNormalization, new GUIContent("SH Coefficients"), true);
                }

                EditorGUILayout.Space();
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear SH Coefficients"))
                {
                    if (EditorUtility.DisplayDialog("Clear SH Coefficients",
                        "Are you sure you want to clear the spherical harmonics coefficients? This action cannot be undone.",
                        "Clear", "Cancel"))
                    {
                        Target.ClearSHCoefficients();
                        EditorUtility.SetDirty(Target);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("This reflection probe does not have valid spherical harmonics coefficients for normalization.", MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Bake SH Coefficients"))
                {
                    PRTBakeManager.BakeReflectionProbe(Target);
                }
                EditorGUILayout.EndHorizontal();
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}