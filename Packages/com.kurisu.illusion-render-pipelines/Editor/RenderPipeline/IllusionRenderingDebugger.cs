using UnityEditor;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    public static class IllusionRenderingDebugger
    {
        [MenuItem("Window/Analysis/Illusion Rendering Debugger")]
        public static void ShowWindow()
        {
            EditorApplication.ExecuteMenuItem("Window/Analysis/Rendering Debugger");
            int index = DebugManager.instance.PanelIndex(IllusionDebugPanels.FeaturesPanelName);
            if (index >= 0)
                DebugManager.instance.RequestEditorWindowPanelIndex(index);
        }
    }
}
