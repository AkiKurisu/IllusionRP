using UnityEditor;

namespace UnityRhi.EditorTools
{
    internal static class RhiShaderAssetMenu
    {
        [MenuItem("Assets/Create/UnityRHI/RHI Shader")]
        private static void CreateShaderModule() =>
            ProjectWindowUtil.CreateAssetWithContent("New RHI Shader.rhishader", "");

        [MenuItem("Assets/Create/UnityRHI/Shader Keywords")]
        private static void CreateShaderKeywords() =>
            ProjectWindowUtil.CreateAssetWithContent(
                "New RHI Shader Keywords.rhikeywords", "");
    }
}
