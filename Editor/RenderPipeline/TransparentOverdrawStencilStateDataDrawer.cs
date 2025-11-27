using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering.Universal;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    [CustomPropertyDrawer(typeof(TransparentOverdrawStencilStateData))]
    internal class TransparentOverdrawStencilStateDataDrawer : PropertyDrawer
    {
        private static class Styles
        {
            public static readonly GUIContent StencilValue = EditorGUIUtility.TrTextContent("Value",
                "For each pixel, the Compare function compares this value with the value in the Stencil buffer. The function writes this value to the buffer if the Pass property is set to Replace.");

            public static readonly GUIContent StencilReadMask = EditorGUIUtility.TrTextContent("Read Mask",
                "For each pixel, the Compare function use this mask to compare the value with the value in the Stencil buffer.");

            public static readonly GUIContent StencilFunction = EditorGUIUtility.TrTextContent("Compare Function",
                "For each pixel, Unity uses this function to compare the value in the Value property with the value in the Stencil buffer.");

            public static readonly GUIContent StencilPass =
                EditorGUIUtility.TrTextContent("Pass", "What happens to the stencil value when passing.");

            public static readonly GUIContent StencilFail =
                EditorGUIUtility.TrTextContent("Fail", "What happens to the stencil value when failing.");

            public static readonly GUIContent StencilZFail =
                EditorGUIUtility.TrTextContent("Z Fail", "What happens to the stencil value when failing Z testing.");
        }

        //Stencil rendering
        private const int StencilBits = 4;
        private const int MinStencilValue = 0;
        private const int MaxStencilValue = (1 << StencilBits) - 1;

        //Stencil props
        private SerializedProperty _overrideStencil;
        private SerializedProperty _stencilIndex;
        private SerializedProperty _stencilReadMask;
        private SerializedProperty _stencilFunction;
        private SerializedProperty _stencilPass;
        private SerializedProperty _stencilFail;
        private SerializedProperty _stencilZFail;
        private readonly List<SerializedObject> _properties = new();

        private void Init(SerializedProperty property)
        {
            //Stencil
            _overrideStencil = property.FindPropertyRelative("overrideStencilState");
            _stencilIndex = property.FindPropertyRelative("stencilReference");
            _stencilReadMask = property.FindPropertyRelative("stencilReadMask");
            _stencilFunction = property.FindPropertyRelative("stencilCompareFunction");
            _stencilPass = property.FindPropertyRelative("passOperation");
            _stencilFail = property.FindPropertyRelative("failOperation");
            _stencilZFail = property.FindPropertyRelative("zFailOperation");

            _properties.Add(property.serializedObject);
        }

        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            if (!_properties.Contains(property.serializedObject))
                Init(property);

            rect.height = EditorGUIUtility.singleLineHeight;

            EditorGUI.PropertyField(rect, _overrideStencil, label);
            if (_overrideStencil.boolValue)
            {
                EditorGUI.indentLevel++;
                rect.y += EditorUtils.Styles.defaultLineSpace;
                //Stencil value
                EditorGUI.BeginChangeCheck();
                var stencilVal = _stencilIndex.intValue;
                stencilVal = EditorGUI.IntSlider(rect, Styles.StencilValue, stencilVal, MinStencilValue, MaxStencilValue);
                if (EditorGUI.EndChangeCheck())
                    _stencilIndex.intValue = stencilVal;
                rect.y += EditorUtils.Styles.defaultLineSpace;
                stencilVal = _stencilReadMask.intValue;
                stencilVal = EditorGUI.IntSlider(rect, Styles.StencilReadMask, stencilVal, MinStencilValue, MaxStencilValue);
                if (EditorGUI.EndChangeCheck())
                    _stencilReadMask.intValue = stencilVal;
                rect.y += EditorUtils.Styles.defaultLineSpace;
                //Stencil compare options
                EditorGUI.PropertyField(rect, _stencilFunction, Styles.StencilFunction);
                rect.y += EditorUtils.Styles.defaultLineSpace;
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(rect, _stencilPass, Styles.StencilPass);
                rect.y += EditorUtils.Styles.defaultLineSpace;
                EditorGUI.PropertyField(rect, _stencilFail, Styles.StencilFail);
                rect.y += EditorUtils.Styles.defaultLineSpace;
                EditorGUI.indentLevel--;
                //Stencil compare options
                EditorGUI.PropertyField(rect, _stencilZFail, Styles.StencilZFail);
                EditorGUI.indentLevel--;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (_properties.Contains(property.serializedObject))
            {
                if (_overrideStencil != null && _overrideStencil.boolValue)
                    return EditorUtils.Styles.defaultLineSpace * 7;
            }
            return EditorUtils.Styles.defaultLineSpace * 1;
        }
    }
}