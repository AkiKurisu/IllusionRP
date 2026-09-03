using Illusion.Rendering.PostProcessing;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(AdvancedTonemapping))]
    internal sealed class AdvancedTonemappingEditor : VolumeComponentEditor
    {
        private SerializedDataParameter _mode;

        private SerializedDataParameter _slope;
        private SerializedDataParameter _toe;
        private SerializedDataParameter _shoulder;
        private SerializedDataParameter _blackClip;
        private SerializedDataParameter _whiteClip;

        private SerializedDataParameter _contrast;
        private SerializedDataParameter _linearSectionStart;
        private SerializedDataParameter _linearSectionLength;
        private SerializedDataParameter _maxBrightness;

        public override bool hasAdditionalProperties => true;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AdvancedTonemapping>(serializedObject);

            _mode = Unpack(o.Find(x => x.mode));
            
            _slope = Unpack(o.Find(x => x.slope));
            _toe = Unpack(o.Find(x => x.toe));
            _shoulder = Unpack(o.Find(x => x.shoulder));
            _blackClip = Unpack(o.Find(x => x.blackClip));
            _whiteClip = Unpack(o.Find(x => x.whiteClip));
            
            _maxBrightness = Unpack(o.Find(x => x.maxBrightness));
            _contrast = Unpack(o.Find(x => x.contrast));
            _linearSectionStart = Unpack(o.Find(x => x.linearSectionStart));
            _linearSectionLength = Unpack(o.Find(x => x.linearSectionLength));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(_mode);

            // Display a warning if the user is trying to use a tonemap while rendering in LDR
            var asset = UniversalRenderPipeline.asset;
            if (asset != null && !asset.supportsHDR)
            {
                EditorGUILayout.HelpBox("Tonemapping should only be used when working with High Dynamic Range (HDR). Please enable HDR through the active Render Pipeline Asset.", MessageType.Warning);
                return;
            }
            
            if ( _mode.value.intValue == (int)AdvancedTonemappingMode.GranTurismo)
            {
                PropertyField(_maxBrightness);
                PropertyField(_contrast);
                PropertyField(_linearSectionStart);
                PropertyField(_linearSectionLength);
            }

            if (_mode.value.intValue == (int)AdvancedTonemappingMode.Filmic_ACES)
            {
                PropertyField(_slope);
                PropertyField(_toe);
                PropertyField(_shoulder);
                PropertyField(_blackClip);
                PropertyField(_whiteClip);
            }
        }
    }
}
