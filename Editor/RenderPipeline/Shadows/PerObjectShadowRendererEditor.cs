using Illusion.Rendering.Shadows;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering.Universal;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PerObjectShadowRenderer))]
    [CanEditMultipleObjects]
    internal class PerObjectShadowRendererEditor : PropertyFetchEditor<PerObjectShadowRenderer>
    {
        private SerializedProperty _isCastingShadow;
        private SerializedProperty _renderingLayerMask;
        private SerializedProperty _clusterData;
        
        protected override void OnEnable()
        {
            base.OnEnable();
            _isCastingShadow = Properties.Find(renderer => renderer.isCastingShadow);
            _renderingLayerMask = Properties.Find(renderer => renderer.renderingLayerMask);
            _clusterData = Properties.Find(renderer => renderer.clusterData);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_isCastingShadow, Styles.IsCastingShadowContent);
            EditorGUILayout.PropertyField(_clusterData, Styles.ClusterDataContent);
            EditorUtils.DrawRenderingLayerMask(_renderingLayerMask, Styles.RenderingLayerMaskContent);

            serializedObject.ApplyModifiedProperties();
        }
        
        private static class Styles
        {
            public static readonly GUIContent IsCastingShadowContent = new(
                "Cast Shadow", 
                "Enable/disable shadow casting for this renderer.");
            
            public static readonly GUIContent ClusterDataContent = new(
                "Cluster Data", 
                "Configure shadow caster cluster data.");
            
            public static readonly GUIContent RenderingLayerMaskContent = new(
                "Rendering Layer Mask", 
                "Set rendering layers for all renderers.");
        }
    }
}
