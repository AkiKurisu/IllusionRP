using UnityEditor;
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
                "For each pixel, the Compare function uses this mask to compare the value with the value in the Stencil buffer.");

            public static readonly GUIContent StencilFunction = EditorGUIUtility.TrTextContent("Compare Function",
                "For each pixel, Unity uses this function to compare the value in the Value property with the value in the Stencil buffer.");

            public static readonly GUIContent StencilPass =
                EditorGUIUtility.TrTextContent("Pass", "What happens to the stencil value when passing.");

            public static readonly GUIContent StencilFail =
                EditorGUIUtility.TrTextContent("Fail", "What happens to the stencil value when failing.");

            public static readonly GUIContent StencilZFail =
                EditorGUIUtility.TrTextContent("Z Fail", "What happens to the stencil value when failing Z testing.");
        }

        private const int StencilBits = 4;
        private const int MinStencilValue = 0;
        private const int MaxStencilValue = (1 << StencilBits) - 1;

        private static float LineSpace => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            var overrideStencil = property.FindPropertyRelative("overrideStencilState");
            var stencilIndex = property.FindPropertyRelative("stencilReference");
            var stencilReadMask = property.FindPropertyRelative("stencilReadMask");
            var stencilFunction = property.FindPropertyRelative("stencilCompareFunction");
            var stencilPass = property.FindPropertyRelative("passOperation");
            var stencilFail = property.FindPropertyRelative("failOperation");
            var stencilZFail = property.FindPropertyRelative("zFailOperation");

            using (new EditorGUI.PropertyScope(rect, label, property))
            {
                rect.height = EditorGUIUtility.singleLineHeight;

                EditorGUI.PropertyField(rect, overrideStencil, label);
                if (!overrideStencil.boolValue)
                    return;

                EditorGUI.indentLevel++;
                rect.y += LineSpace;

                DrawIntSlider(rect, stencilIndex, Styles.StencilValue);
                rect.y += LineSpace;

                DrawIntSlider(rect, stencilReadMask, Styles.StencilReadMask);
                rect.y += LineSpace;

                EditorGUI.PropertyField(rect, stencilFunction, Styles.StencilFunction);
                rect.y += LineSpace;

                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(rect, stencilPass, Styles.StencilPass);
                rect.y += LineSpace;

                EditorGUI.PropertyField(rect, stencilFail, Styles.StencilFail);
                rect.y += LineSpace;
                EditorGUI.indentLevel--;

                EditorGUI.PropertyField(rect, stencilZFail, Styles.StencilZFail);
                EditorGUI.indentLevel--;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var overrideStencil = property.FindPropertyRelative("overrideStencilState");
            return overrideStencil != null && overrideStencil.boolValue ? LineSpace * 7 : LineSpace;
        }

        private static void DrawIntSlider(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            int value = EditorGUI.IntSlider(rect, label, property.intValue, MinStencilValue, MaxStencilValue);
            if (EditorGUI.EndChangeCheck())
                property.intValue = value;
        }
    }
}
