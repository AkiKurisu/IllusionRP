using AIChara;
using UnityEditor;
using UnityEngine;

namespace AIChara.Editor
{
    [CustomEditor(typeof(ChaDemoControl))]
    public sealed class ChaDemoControlEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var control = (ChaDemoControl)target;
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate"))
                {
                    control.ValidateContent();
                    EditorUtility.SetDirty(control);
                }

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Reload"))
                    {
                        control.Reload();
                    }

                    if (GUILayout.Button("Unload"))
                    {
                        control.Unload();
                    }
                }
            }

            DrawReport(control.LastReport);
        }

        private static void DrawReport(ChaDemoLoadReport report)
        {
            if (report == null || string.IsNullOrEmpty(report.contentPath))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Load Report", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", report.succeeded ? "Succeeded" : "Failed or not loaded");
            EditorGUILayout.LabelField("Content", report.contentPath);
            EditorGUILayout.LabelField("Address", report.characterAddress);
            EditorGUILayout.LabelField("Renderers", report.rendererCount.ToString());
            EditorGUILayout.LabelField("Skinned", report.skinnedRendererCount.ToString());
            EditorGUILayout.LabelField("Static", report.staticRendererCount.ToString());
            EditorGUILayout.LabelField("Attached Objects", report.attachedObjectCount.ToString());
            EditorGUILayout.LabelField("Remapped Bones", report.remappedBoneCount.ToString());
            EditorGUILayout.LabelField("Preserved Bones", report.preservedBoneCount.ToString());

            foreach (string error in report.errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            foreach (string warning in report.warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            if (report.missingBones.Count > 0)
            {
                EditorGUILayout.HelpBox($"Missing target bones: {report.missingBones.Count}", MessageType.Warning);
            }
        }
    }
}
