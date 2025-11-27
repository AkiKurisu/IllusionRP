using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Illusion.Rendering.Editor
{
    [FilePath("ProjectSettings/IllusionRenderPipelineSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class IllusionRenderPipelineSettings : ScriptableSingleton<IllusionRenderPipelineSettings>
    {
        public bool stripUnusedVariants = true;

        public static void SaveSettings()
        {
            instance.Save(true);
        }
    }

    internal class IllusionRenderPipelineSettingsProvider : SettingsProvider
    {
        private SerializedObject _settingsObject;
        
        private class Styles
        {
            public static readonly GUIContent StripUnusedVariantsLabel =
                EditorGUIUtility.TrTextContent("Strip Unused Variants", "Controls whether strip disabled keyword variants if the feature is enabled.");
        }

        private IllusionRenderPipelineSettingsProvider(string path, SettingsScope scope = SettingsScope.User) : base(path, scope) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _settingsObject = new SerializedObject(IllusionRenderPipelineSettings.instance);
        }

        public override void OnGUI(string searchContext)
        {
            var titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            GUILayout.Label("Shader Stripping", titleStyle);
            GUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.PropertyField(_settingsObject.FindProperty(nameof(IllusionRenderPipelineSettings.stripUnusedVariants)), Styles.StripUnusedVariantsLabel);
            if (_settingsObject.ApplyModifiedPropertiesWithoutUndo())
            {
                IllusionRenderPipelineSettings.SaveSettings();
            }
            GUILayout.EndVertical();
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new IllusionRenderPipelineSettingsProvider("Project/Graphics/IllusionRP Global Settings", SettingsScope.Project)
            {
                keywords = GetSearchKeywordsFromGUIContentProperties<Styles>()
            };
            return provider;
        }
    }
}