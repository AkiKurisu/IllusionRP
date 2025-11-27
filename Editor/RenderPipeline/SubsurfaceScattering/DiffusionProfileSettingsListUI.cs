using UnityEngine;
using UnityEditorInternal;
using System;
using UnityEditor;

namespace Illusion.Rendering.Editor
{
    internal class DiffusionProfileSettingsListUI
    {
        private ReorderableList _diffusionProfileList;

        private SerializedProperty _property;

        private readonly string _listName;

        private const string DefaultListName = "Diffusion Profile List";

        private const string MultiEditionUnsupported = "Diffusion Profile List: Multi-edition is not supported";


        public DiffusionProfileSettingsListUI(string listName = DefaultListName)
        {
            _listName = listName;
        }

        public void OnGUI(SerializedProperty parameter)
        {
            if (parameter.hasMultipleDifferentValues)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.LabelField(MultiEditionUnsupported);

                return;
            }

            if (_diffusionProfileList == null || _property != parameter)
                CreateReorderableList(parameter);

            EditorGUILayout.BeginVertical();
            _diffusionProfileList!.DoLayoutList();
            EditorGUILayout.EndVertical();
        }

        public Action<SerializedProperty, Rect, int> drawElement;

        private void CreateReorderableList(SerializedProperty parameter)
        {
            _property = parameter;
            _diffusionProfileList = new ReorderableList(parameter.serializedObject, parameter, true, true, true, true)
                {
                    drawHeaderCallback = (rect) =>
                    {
                        EditorGUI.LabelField(rect, _listName);
                    },
                    drawElementCallback = (rect, index, active, focused) =>
                    {
                        rect.y += 2;
                        rect.height = EditorGUIUtility.singleLineHeight;
                        drawElement?.Invoke(parameter.GetArrayElementAtIndex(index), rect, index);
                    },
                    onCanAddCallback = l => l.count < DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT - 1,
                    onAddCallback = (l) =>
                    {
                        if (parameter.arraySize >= DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT - 1)
                        {
                            Debug.LogError("Limit of 15 diffusion profiles reached.");
                            return;
                        }

                        parameter.InsertArrayElementAtIndex(parameter.arraySize);
                        parameter.serializedObject.ApplyModifiedProperties();
                    }
                };
        }
    }
}
