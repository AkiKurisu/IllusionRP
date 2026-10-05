using Illusion.Rendering.PRTGI;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    internal partial class PRTProbeVolumeEditor
    {
        private SerializedProperty _asset, _bakeResolution, _bakeSampleCount, _bakeSeed;
        private void InitializeBakeProperties()
        {
            _asset = Properties.Find(volume => volume.asset);
            _bakeResolution = Properties.Find(volume => volume.bakeResolution);
            _bakeSampleCount = Properties.Find(volume => volume.bakeSampleCount);
            _bakeSeed = Properties.Find(volume => volume.bakeSeed);
        }
        private void DrawBakeSettingsSection()
        {
            if (Foldout("Bake Settings", true))
            {
                EditorGUILayout.PropertyField(_asset, Styles.ProbeVolumeAssetLabel);
                EditorGUILayout.PropertyField(_bakeResolution, Styles.BakeResolutionLabel);
                EditorGUILayout.PropertyField(_bakeSampleCount, new GUIContent("Samples Per Probe"));
                EditorGUILayout.PropertyField(_bakeSeed, new GUIContent("Sampling Seed"));
                EditorGUILayout.PropertyField(_sectorWidth, new GUIContent("Sector Width (probe columns)"));
            }
            if (Target.asset && !Target.asset.TryValidate(out string reason))
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
        }
        private static void DrawActionButtons()
        {
            EditorGUILayout.Space();
            if (PRTVolumeManager.IsBaking)
            {
                if (GUILayout.Button("Cancel Baking")) PRTBakeManager.StopBaking();
                return;
            }
            if (ButtonWithDropdownList(Styles.GenerateLightingLabel, Styles.DetailActionLabels, OnActionDropDown))
            {
                PRTBakeManager.GenerateLighting();
                GUIUtility.ExitGUI();
            }
        }
        private static void OnActionDropDown(object data)
        {
            switch ((int)data)
            {
                case 0: PRTBakeManager.BakeAllReflectionProbes(); break;
                case 1:
                    if (EditorUtility.DisplayDialog("Clear Baked Data", "Clear the current PRT transport and normalization data?", "Clear", "Cancel"))
                        PRTBakeManager.ClearBakedData();
                    break;
            }
        }
    }
}
