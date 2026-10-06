using Illusion.Rendering.PRTGI;
using UnityEditor;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    internal partial class PRTProbeVolumeEditor
    {
        private SerializedProperty _asset, _bakeSampleCount, _bakeSeed, _surfelMergeDistance, _sectorWidth;

        private void InitializeBakeProperties()
        {
            _asset = Properties.Find(volume => volume.asset);
            _bakeSampleCount = Properties.Find(volume => volume.bakeSampleCount);
            _bakeSeed = Properties.Find(volume => volume.bakeSeed);
            _surfelMergeDistance = Properties.Find(volume => volume.surfelMergeDistance);
            _sectorWidth = Properties.Find(volume => volume.sectorWidth);
        }

        private void DrawBakeSettings()
        {
            if (Foldout("Baking", true))
            {
                EditorGUILayout.PropertyField(_asset, Styles.Asset);
                EditorGUILayout.PropertyField(_bakeSampleCount, Styles.SampleCount);
                EditorGUILayout.PropertyField(_bakeSeed, Styles.SampleSeed);
                EditorGUILayout.PropertyField(_surfelMergeDistance, Styles.SurfelMergeDistance);
                EditorGUILayout.PropertyField(_sectorWidth, Styles.SectorWidth);
            }

            EditorGUILayout.Space();
        }

        private static void DrawActionButtons()
        {
            if (PRTVolumeManager.IsBaking)
            {
                if (GUILayout.Button("Cancel Baking")) PRTBakeManager.StopBaking();
                return;
            }
            if (ButtonWithDropdownList(Styles.GenerateLighting, Styles.DetailActions, OnActionDropDown))
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
