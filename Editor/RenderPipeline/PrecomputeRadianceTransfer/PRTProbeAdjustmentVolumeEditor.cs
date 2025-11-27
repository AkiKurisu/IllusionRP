using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(PRTProbeAdjustmentVolume))]
    internal class PrtProbeAdjustmentVolumeEditor : PropertyFetchEditor<PRTProbeAdjustmentVolume>
    {
        private SerializedProperty _shape;
        
        private SerializedProperty _size;
        
        private SerializedProperty _radius;
        
        private SerializedProperty _mode;
        
        private SerializedProperty _virtualOffsetRotation;
        
        private SerializedProperty _virtualOffsetDistance;
        
        private SerializedProperty _geometryBias;
        
        private SerializedProperty _rayOriginBias;
        
        private SerializedProperty _intensityScale;
        
        private SerializedProperty _showVolumeBounds;

        protected override void OnEnable()
        {
            base.OnEnable();
            _shape = Properties.Find(volume => volume.shape);
            _size = Properties.Find(volume => volume.size);
            _radius = Properties.Find(volume => volume.radius);
            _mode = Properties.Find(volume => volume.mode);
            _virtualOffsetRotation = Properties.Find(volume => volume.virtualOffsetRotation);
            _virtualOffsetDistance = Properties.Find(volume => volume.virtualOffsetDistance);
            _geometryBias = Properties.Find(volume => volume.geometryBias);
            _rayOriginBias = Properties.Find(volume => volume.rayOriginBias);
            _intensityScale = Properties.Find(volume => volume.intensityScale);
            _showVolumeBounds = Properties.Find(volume => volume.showVolumeBounds);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Influence Volume Section
            EditorGUILayout.LabelField(Styles.VolumeHeader, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_shape, Styles.Shape);

            if (_shape.intValue == (int)PRTProbeAdjustmentShape.Box)
            {
                EditorGUILayout.PropertyField(_size, Styles.Size);
            }
            else
            {
                EditorGUILayout.PropertyField(_radius, Styles.Radius);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // Adjustment Section
            EditorGUILayout.LabelField(Styles.AdjustmentHeader, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_mode, Styles.Mode);

            if (_mode.intValue == (int)PRTProbeAdjustmentMode.ApplyVirtualOffset)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(_virtualOffsetRotation, Styles.VoDirection);

                var editMode = Styles.VirtualOffsetEditMode;
                EditorGUI.BeginChangeCheck();
                GUILayout.Toggle(editMode == EditMode.editMode, Styles.VoRotateTool, EditorStyles.miniButton, GUILayout.Width(28f));
                if (EditorGUI.EndChangeCheck())
                {
                    EditMode.SceneViewEditMode targetMode = EditMode.editMode == editMode ? EditMode.SceneViewEditMode.None : editMode;
                    EditMode.ChangeEditMode(targetMode, GetBounds(), this);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(_virtualOffsetDistance, Styles.VoDistance);
            }
            else if (_mode.intValue == (int)PRTProbeAdjustmentMode.OverrideVirtualOffsetSettings)
            {
                EditorGUILayout.PropertyField(_geometryBias, Styles.GeometryBias);
                EditorGUILayout.PropertyField(_rayOriginBias, Styles.RayOriginBias);
            }
            else if (_mode.intValue == (int)PRTProbeAdjustmentMode.IntensityScale)
            {
                EditorGUILayout.PropertyField(_intensityScale, Styles.IntensityScale);
                EditorGUILayout.HelpBox("Overriding the intensity of probes can break the physical plausibility of lighting. This may result in unwanted visual inconsistencies.", MessageType.Info, wide: true);
            }
            else if (_mode.intValue == (int)PRTProbeAdjustmentMode.InvalidateProbes)
            {
                EditorGUILayout.HelpBox("This mode will mark all probes within this volume as invalid during baking.", MessageType.Info, wide: true);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // Debug Section
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_showVolumeBounds, Styles.ShowVolumeBounds);
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }

        private Bounds GetBounds()
        {
            var position = ((Component)target).transform.position;
            if (_shape.intValue == (int)PRTProbeAdjustmentShape.Box)
                return new Bounds(position, _size.vector3Value);
            if (_shape.intValue == (int)PRTProbeAdjustmentShape.Sphere)
                return new Bounds(position, _radius.floatValue * Vector3.one);
            return default;
        }

        [DrawGizmo(GizmoType.InSelectionHierarchy)]
        private static void DrawGizmosSelected(PRTProbeAdjustmentVolume adjustmentVolume, GizmoType gizmoType)
        {
            if (!adjustmentVolume.showVolumeBounds) return;

            Gizmos.color = new Color32(222, 132, 144, 45);
            Gizmos.matrix = Matrix4x4.TRS(adjustmentVolume.transform.position, adjustmentVolume.transform.rotation, Vector3.one);

            if (adjustmentVolume.shape == PRTProbeAdjustmentShape.Box)
            {
                Gizmos.DrawWireCube(Vector3.zero, adjustmentVolume.size);
                Gizmos.DrawCube(Vector3.zero, adjustmentVolume.size);
            }
            else if (adjustmentVolume.shape == PRTProbeAdjustmentShape.Sphere)
            {
                Gizmos.DrawWireSphere(Vector3.zero, adjustmentVolume.radius);
                Gizmos.DrawSphere(Vector3.zero, adjustmentVolume.radius);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }

        protected void OnSceneGUI()
        {
            PRTProbeAdjustmentVolume adjustmentVolume = (PRTProbeAdjustmentVolume)target;

            if (adjustmentVolume.mode == PRTProbeAdjustmentMode.ApplyVirtualOffset && EditMode.editMode == Styles.VirtualOffsetEditMode)
            {
                EditorGUI.BeginChangeCheck();
                Quaternion rotation = Handles.RotationHandle(Quaternion.Euler(0, adjustmentVolume.virtualOffsetRotation, 0), adjustmentVolume.transform.position);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(adjustmentVolume, "Change Virtual Offset Direction");
                    adjustmentVolume.virtualOffsetRotation = rotation.eulerAngles.y;
                }
            }
        }
        private static class Styles
        {
            internal static readonly GUIContent VoRotateTool = EditorGUIUtility.TrIconContent("RotateTool", "The virtual offset direction for probes falling in this volume.");
            
            internal static readonly GUIContent VolumeHeader = EditorGUIUtility.TrTextContent("Influence Volume");
            
            internal static readonly GUIContent AdjustmentHeader = EditorGUIUtility.TrTextContent("Probe Volume Overrides");

            internal static readonly GUIContent Mode = new("Mode", "Choose which type of adjustment to apply to probes covered by this volume.");
            
            internal static readonly GUIContent Shape = new("Shape", "Set the shape of the Adjustment Volume to either Box or Sphere.");
            
            internal static readonly GUIContent Size = new("Size", "Set the size of the Adjustment Volume (Box only).");
            
            internal static readonly GUIContent Radius = new("Radius", "Set the radius of the Adjustment Volume (Sphere only).");
            
            internal static readonly GUIContent VoDirection = new("Rotation", "Rotate the axis along which probes will be pushed when applying Virtual Offset.");
            
            internal static readonly GUIContent VoDistance = new("Distance", "Determines how far probes are pushed in the direction of the Virtual Offset.");
            
            internal static readonly GUIContent GeometryBias = new("Geometry Bias", "How far to push a probe's capture point out of geometry.");
            
            internal static readonly GUIContent RayOriginBias = new("Ray Origin Bias", "Distance between a probe's center and the point used for sampling ray origin.");
            
            internal static readonly GUIContent IntensityScale = new("Intensity Scale", "Change the brightness of all probes covered by this volume.");
            
            internal static readonly GUIContent ShowVolumeBounds = new("Show Volume Bounds", "Show volume bounds in scene view");

            internal const EditMode.SceneViewEditMode VirtualOffsetEditMode = (EditMode.SceneViewEditMode)110;
        }
    }
}
