using Illusion.Rendering.PRTGI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    internal static class PRTProbeVolumeMenuItems
    {
        [MenuItem("GameObject/Light/PRT Probe Volume", priority = CoreUtils.Sections.section8)]
        private static void CreateProbeVolumeGameObject(MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;
            var probeVolume = CoreEditorUtils.CreateGameObject("PRT Probe Volume", parent);
            probeVolume.AddComponent<PRTProbeVolume>();
        }

        [MenuItem("GameObject/Light/PRT Probe Adjustment Volume", priority = CoreUtils.Sections.section8 + 1)]
        private static void CreateProbeAdjustmentVolumeGameObject(MenuCommand menuCommand)
        {
            var parent = menuCommand.context as GameObject;
            var probeVolume = CoreEditorUtils.CreateGameObject("PRT Probe Adjustment Volume", parent);
            probeVolume.AddComponent<PRTProbeAdjustmentVolume>();
        }
    }
}