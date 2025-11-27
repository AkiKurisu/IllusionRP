using UnityEditor;
using UnityEditor.Rendering;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(ScreenSpaceReflection))]
    public sealed class ScreenSpaceReflectionEditor : VolumeComponentEditor
    {
        private SerializedDataParameter _enable;
        
        private SerializedDataParameter _mode;
        
        private SerializedDataParameter _usedAlgorithm;
        
        // private SerializedDataParameter _downSample;
        
        private SerializedDataParameter _intensity;
        
        private SerializedDataParameter _thickness;
        
        private SerializedDataParameter _minSmoothness;
        
        private SerializedDataParameter _smoothnessFadeStart;
        
        private SerializedDataParameter _screenFadeDistance;
        
        private SerializedDataParameter _accumulationFactor;
        
        private SerializedDataParameter _biasFactor;
        
        private SerializedDataParameter _speedRejectionParam;
        
        private SerializedDataParameter _speedRejectionScalerFactor;
        
        private SerializedDataParameter _enableWorldSpeedRejection;
        
        private SerializedDataParameter _steps;
        
        private SerializedDataParameter _stepSize;
        
#if UNITY_EDITOR
        private SerializedDataParameter _fullScreenDebugMode;
#endif

        public override void OnEnable()
        {
            var propertyFetcher = new PropertyFetcher<ScreenSpaceReflection>(serializedObject);

            _enable = Unpack(propertyFetcher.Find(ssr => ssr.enable));
            _mode = Unpack(propertyFetcher.Find(ssr => ssr.mode));
            _usedAlgorithm = Unpack(propertyFetcher.Find(ssr => ssr.usedAlgorithm));
            // _downSample = Unpack(propertyFetcher.Find(ssr => ssr.downSample));
            _intensity = Unpack(propertyFetcher.Find(ssr => ssr.intensity));
            _thickness = Unpack(propertyFetcher.Find(ssr => ssr.thickness));
            _minSmoothness = Unpack(propertyFetcher.Find(ssr => ssr.minSmoothness));
            _smoothnessFadeStart = Unpack(propertyFetcher.Find(ssr => ssr.smoothnessFadeStart));
            _screenFadeDistance = Unpack(propertyFetcher.Find(ssr => ssr.screenFadeDistance));
            _accumulationFactor = Unpack(propertyFetcher.Find(ssr => ssr.accumulationFactor));
            _biasFactor = Unpack(propertyFetcher.Find(ssr => ssr.biasFactor));
            _speedRejectionParam = Unpack(propertyFetcher.Find(ssr => ssr.speedRejectionParam));
            _speedRejectionScalerFactor = Unpack(propertyFetcher.Find(ssr => ssr.speedRejectionScalerFactor));
            _enableWorldSpeedRejection = Unpack(propertyFetcher.Find(ssr => ssr.enableWorldSpeedRejection));
            _steps = Unpack(propertyFetcher.Find(ssr => ssr.steps));
            _stepSize = Unpack(propertyFetcher.Find(ssr => ssr.stepSize));
#if UNITY_EDITOR
            _fullScreenDebugMode = Unpack(propertyFetcher.Find(ssr => ssr.fullScreenDebugMode));
#endif
        }

        public override void OnInspectorGUI()
        {
            bool isEnabled = _enable.overrideState.boolValue && _enable.value.boolValue;

            PropertyField(_enable);

            if (!isEnabled) return;
            
            PropertyField(_mode);
            PropertyField(_usedAlgorithm);
            // PropertyField(_downSample);
            PropertyField(_intensity);
            PropertyField(_thickness);
            PropertyField(_minSmoothness);
            PropertyField(_smoothnessFadeStart);
            PropertyField(_screenFadeDistance);

            bool isPBR = (ScreenSpaceReflectionAlgorithm)_usedAlgorithm.value.intValue == ScreenSpaceReflectionAlgorithm.PBRAccumulation
                        && (ScreenSpaceReflectionMode)_mode.value.intValue == ScreenSpaceReflectionMode.HizSS;
            if (isPBR)
            {
                PropertyField(_accumulationFactor);
                PropertyField(_biasFactor);
                PropertyField(_speedRejectionParam);
                PropertyField(_speedRejectionScalerFactor);
                PropertyField(_enableWorldSpeedRejection);
            }

            PropertyField(_steps);

            bool isHizMode = ((ScreenSpaceReflectionMode)_mode.value.intValue) == ScreenSpaceReflectionMode.HizSS;
            using (new EditorGUI.DisabledScope(isHizMode))
            {
                PropertyField(_stepSize);
            }

#if UNITY_EDITOR
            PropertyField(_fullScreenDebugMode);
#endif
        }
    }
}
