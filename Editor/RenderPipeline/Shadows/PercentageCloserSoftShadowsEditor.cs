using Illusion.Rendering.Shadows;
using UnityEditor;
using UnityEditor.Rendering;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PercentageCloserSoftShadows))]
    internal sealed class PercentageCloserSoftShadowsEditor : VolumeComponentEditor
    {
        private SerializedDataParameter _angularDiameter;
        private SerializedDataParameter _blockerSearchAngularDiameter;
        private SerializedDataParameter _minFilterMaxAngularDiameter;
        private SerializedDataParameter _maxPenumbraSize;
        private SerializedDataParameter _maxSamplingDistance;
        private SerializedDataParameter _minFilterSizeTexels;
        private SerializedDataParameter _findBlockerSampleCount;
        private SerializedDataParameter _pcfSampleCount;
        private SerializedDataParameter _penumbraMaskScale;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<PercentageCloserSoftShadows>(serializedObject);
            
            _angularDiameter = Unpack(o.Find(x => x.angularDiameter));
            _blockerSearchAngularDiameter = Unpack(o.Find(x => x.blockerSearchAngularDiameter));
            _minFilterMaxAngularDiameter = Unpack(o.Find(x => x.minFilterMaxAngularDiameter));
            _maxPenumbraSize = Unpack(o.Find(x => x.maxPenumbraSize));
            _maxSamplingDistance = Unpack(o.Find(x => x.maxSamplingDistance));
            _minFilterSizeTexels = Unpack(o.Find(x => x.minFilterSizeTexels));
            _findBlockerSampleCount = Unpack(o.Find(x => x.findBlockerSampleCount));
            _pcfSampleCount = Unpack(o.Find(x => x.pcfSampleCount));
            _penumbraMaskScale = Unpack(o.Find(x => x.penumbraMaskScale));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(_angularDiameter, EditorGUIUtility.TrTextContent("Light Angular Diameter", "The angular diameter of the light source in degrees. Affects the penumbra size."));
            PropertyField(_blockerSearchAngularDiameter, EditorGUIUtility.TrTextContent("Blocker Search Diameter", "The angular diameter for blocker search in degrees. Larger values search a wider area."));
            PropertyField(_minFilterMaxAngularDiameter, EditorGUIUtility.TrTextContent("Min Filter Max Diameter", "The minimum filter max angular diameter in degrees."));
            PropertyField(_maxPenumbraSize, EditorGUIUtility.TrTextContent("Max Penumbra Size", "Maximum penumbra size in world units."));
            PropertyField(_maxSamplingDistance, EditorGUIUtility.TrTextContent("Max Sampling Distance", "Maximum sampling distance for PCSS."));
            PropertyField(_minFilterSizeTexels, EditorGUIUtility.TrTextContent("Min Filter Size", "Minimum filter size in texels."));
            PropertyField(_findBlockerSampleCount, EditorGUIUtility.TrTextContent("Blocker Search Samples", "Number of samples for blocker search. Higher values give better quality but lower performance."));
            PropertyField(_pcfSampleCount, EditorGUIUtility.TrTextContent("PCF Samples", "Number of samples for PCF filtering. Higher values give smoother shadows but lower performance."));
            PropertyField(_penumbraMaskScale, EditorGUIUtility.TrTextContent("Penumbra Mask Scale", "Scale factor for the penumbra mask texture. Higher values use smaller textures (better performance, lower quality)."));
        }
    }
}

