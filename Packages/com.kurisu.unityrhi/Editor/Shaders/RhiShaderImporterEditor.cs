using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    [CustomEditor(typeof(RhiShaderImporter))]
    internal sealed class RhiShaderImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "Shader definitions are stored in this file's .meta. Apply only updates the " +
                "stable shader assets; variants compile on demand in the Editor and during a build.",
                MessageType.Info);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("includeDirectoryAssets"),
                new GUIContent("Include Directories"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defines"), true);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("keywordAsset"),
                new GUIContent("Keywords"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("sourceAsset"),
                new GUIContent("Source"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("entryPoint"),
                new GUIContent("Entry Point"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("targetProfile"),
                new GUIContent("Target Profile"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flags"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("programDefines"),
                new GUIContent("Program Defines"), true);
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }

    [CustomEditor(typeof(RhiShaderKeywordImporter))]
    internal sealed class RhiShaderKeywordImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "This asset defines the shader keyword space. Concrete variants are collected " +
                "from actual Editor usage and build variant providers.",
                MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("keywords"), true);
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
