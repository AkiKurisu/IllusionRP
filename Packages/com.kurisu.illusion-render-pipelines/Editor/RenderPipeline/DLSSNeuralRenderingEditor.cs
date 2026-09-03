using UnityEditor;
using UnityEditor.Rendering;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(DLSSNeuralRendering))]
    internal sealed class DLSSNeuralRenderingEditor : VolumeComponentEditor
    {
        private SerializedDataParameter _enable;
        private SerializedDataParameter _preset;
        private SerializedDataParameter _style;
        private SerializedDataParameter _intensity;
        private SerializedDataParameter _localToneStrength;
        private SerializedDataParameter _localStructureStrength;
        private SerializedDataParameter _skinStructureStrength;
        private SerializedDataParameter _useAutoMask;
        private SerializedDataParameter _uiCorrection;
        private SerializedDataParameter _motionVectorScale;
        private SerializedDataParameter _cameraCutDistance;
        private SerializedDataParameter _cameraCutAngle;

        public override bool hasAdditionalProperties => true;

        public override void OnEnable()
        {
            var properties = new PropertyFetcher<DLSSNeuralRendering>(serializedObject);
            _enable = Unpack(properties.Find(volume => volume.enable));
            _preset = Unpack(properties.Find(volume => volume.preset));
            _style = Unpack(properties.Find(volume => volume.style));
            _intensity = Unpack(properties.Find(volume => volume.intensity));
            _localToneStrength = Unpack(properties.Find(volume => volume.localToneStrength));
            _localStructureStrength = Unpack(properties.Find(volume => volume.localStructureStrength));
            _skinStructureStrength = Unpack(properties.Find(volume => volume.skinStructureStrength));
            _useAutoMask = Unpack(properties.Find(volume => volume.useAutoMask));
            _uiCorrection = Unpack(properties.Find(volume => volume.uiCorrection));
            _motionVectorScale = Unpack(properties.Find(volume => volume.motionVectorScale));
            _cameraCutDistance = Unpack(properties.Find(volume => volume.cameraCutDistance));
            _cameraCutAngle = Unpack(properties.Find(volume => volume.cameraCutAngle));
        }

        public override void OnInspectorGUI()
        {
            bool isEnabled = _enable.overrideState.boolValue && _enable.value.boolValue;
            PropertyField(_enable);
            if (!isEnabled)
                return;

            PropertyField(_preset);
            PropertyField(_style);
            PropertyField(_intensity);
            PropertyField(_localToneStrength);
            PropertyField(_localStructureStrength);
            PropertyField(_skinStructureStrength);
            PropertyField(_useAutoMask);
            PropertyField(_uiCorrection);
            PropertyField(_motionVectorScale);
            PropertyField(_cameraCutDistance);
            PropertyField(_cameraCutAngle);
        }
    }
}
