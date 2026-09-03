using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UEditor = UnityEditor.Editor;
using UObject = UnityEngine.Object;

namespace Illusion.Rendering.Editor
{
    internal class PropertyFetchEditor<T> : UEditor where T : UObject
    {
        internal PropertyFetcher<T> Properties { get; private set; }

        protected T Target => target as T;

        private readonly Dictionary<string, bool> _status = new();

        protected virtual void OnEnable()
        {
            Properties = new PropertyFetcher<T>(serializedObject);
        }
        
        /// <summary>
        /// Draws the built-in Inspector without showing Script field.
        /// </summary>
        /// <returns></returns>
        protected bool DrawDefaultInspectorWithoutScript()
        {
            EditorGUI.BeginChangeCheck();
            serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty iterator = serializedObject.GetIterator();
            for (bool enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
            {
                if ("m_Script" == iterator.propertyPath) continue;
                EditorGUILayout.PropertyField(iterator, true);
            }
            serializedObject.ApplyModifiedProperties();
            return EditorGUI.EndChangeCheck();
        }

        /// <summary>
        /// Draws a foldout
        /// </summary>
        /// <param name="content"></param>
        /// <param name="fromEditorPref"></param>
        /// <returns></returns>
        protected bool Foldout(string content, bool fromEditorPref = false)
        {
            bool status;
            if (fromEditorPref && !_status.ContainsKey(content))
            {
                status = EditorPrefs.GetBool($"{typeof(T).Name}_{content}", false);
                _status[content] = status;
            }
            else
            {
                status = _status.GetValueOrDefault(content);
            }
            var color = GUI.backgroundColor;
            GUI.backgroundColor = Color.white;
            
            var rect = GUILayoutUtility.GetRect(new GUIContent(content), Styles.FoldoutStyle);
            GUI.Box(rect, content, Styles.FoldoutStyle);
            GUI.backgroundColor = color;

            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                var arrowRect = new Rect(rect.x + 4f, rect.y + 2f, 13f, 13f);
                EditorStyles.foldout.Draw(arrowRect, false, false, status, false);
            }

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                status = !status;
                _status[content] = status;
                if (fromEditorPref)
                {
                    EditorPrefs.SetBool($"{typeof(T).Name}_{content}", status);
                }
                e.Use();
            }

            return status;
        }
        
        protected static bool ButtonWithDropdownList(
            GUIContent content,
            string[] buttonNames,
            GenericMenu.MenuFunction2 callback,
            params GUILayoutOption[] options)
        {
            Rect rect1 = GUILayoutUtility.GetRect(content, Styles.DropDownListStyle, options);
            Rect rect2 = rect1;
            rect2.xMin = rect2.xMax - 20f;
            if (Event.current.type != EventType.MouseDown || !rect2.Contains(Event.current.mousePosition))
                return GUI.Button(rect1, content, Styles.DropDownListStyle);
            GenericMenu genericMenu = new GenericMenu();
            for (int userData = 0; userData != buttonNames.Length; ++userData)
                genericMenu.AddItem(new GUIContent(buttonNames[userData]), false, callback, userData);
            genericMenu.DropDown(rect1);
            Event.current.Use();
            return false;
        }
                
        private static class Styles
        {
            public static readonly GUIStyle FoldoutStyle = new("ShurikenModuleTitle")
            {
                font = new GUIStyle(EditorStyles.label).font,
                border = new RectOffset(15, 7, 4, 4),
                fixedHeight = 22,
                contentOffset = new Vector2(20f, -2f)
            };

            public static readonly GUIStyle DropDownListStyle;

            static Styles()
            {
                DropDownListStyle = GetStyle("DropDownButton");
            }
            
            private static GUIStyle GetStyle(string styleName)
            {
                GUIStyle style = GUI.skin.FindStyle(styleName) ?? EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle(styleName);
                return style;
            }
        }
    }
}