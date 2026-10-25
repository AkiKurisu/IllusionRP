using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Illusion.Rendering.Editor
{
    [FilePath("ProjectSettings/IllusionRenderPipelineSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class IllusionRenderPipelineSettings : ScriptableSingleton<IllusionRenderPipelineSettings>
    {
        public bool stripUnusedVariants = true;

        public bool autoGenerateInlineEvaluators = true;

        public bool verboseLogging;

        [InitializeOnLoadMethod]
        private static void ApplyVerboseLogging()
        {
            IllusionLog.VerboseEnabled = instance.verboseLogging;
        }

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

            public static readonly GUIContent AutoGenerateInlineEvaluatorsLabel =
                EditorGUIUtility.TrTextContent("Auto Generate Evaluators", "Regenerate inline evaluators when shaders or materials change. When off, use Tools > IllusionRP > Generate Inline Evaluators.");

            public static readonly GUIContent VerboseLoggingLabel =
                EditorGUIUtility.TrTextContent("Verbose Logging", "Write IllusionRP and UnityRHI lifecycle, timing and diagnostic details to the Console.");
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
            GUILayout.EndVertical();

            GUILayout.Label("Inline Ray Tracing", titleStyle);
            GUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.PropertyField(_settingsObject.FindProperty(nameof(IllusionRenderPipelineSettings.autoGenerateInlineEvaluators)), Styles.AutoGenerateInlineEvaluatorsLabel);
            GUILayout.EndVertical();

            GUILayout.Label("Diagnostics", titleStyle);
            GUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.PropertyField(_settingsObject.FindProperty(nameof(IllusionRenderPipelineSettings.verboseLogging)), Styles.VerboseLoggingLabel);
            GUILayout.EndVertical();

            bool autoGenerate = IllusionRenderPipelineSettings.instance.autoGenerateInlineEvaluators;
            if (_settingsObject.ApplyModifiedPropertiesWithoutUndo())
            {
                IllusionRenderPipelineSettings.SaveSettings();
                IllusionLog.VerboseEnabled = IllusionRenderPipelineSettings.instance.verboseLogging;
                if (!autoGenerate && IllusionRenderPipelineSettings.instance.autoGenerateInlineEvaluators)
                    PathTracingInlinePostprocessor.Schedule();
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new IllusionRenderPipelineSettingsProvider("Project/Graphics/IllusionRP", SettingsScope.Project)
            {
                keywords = GetSearchKeywordsFromGUIContentProperties<Styles>()
            };
            return provider;
        }
    }
}