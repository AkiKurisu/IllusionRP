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
        private SerializedDataParameter _penumbraMaskDilation;
        private SerializedDataParameter _penumbraMaskMinDilation;
        private SerializedDataParameter _penumbraMaskDilationFadeStart;
        private SerializedDataParameter _penumbraMaskDilationFadeEnd;
        private SerializedDataParameter _usePenumbraMask;
        private SerializedDataParameter _shadowTemporalAccumulation;
        private SerializedDataParameter _shadowSpatialDenoise;
        private SerializedDataParameter _shadowDenoiseKernelSize;

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
            _penumbraMaskDilation = Unpack(o.Find(x => x.penumbraMaskDilation));
            _penumbraMaskMinDilation = Unpack(o.Find(x => x.penumbraMaskMinDilation));
            _penumbraMaskDilationFadeStart = Unpack(o.Find(x => x.penumbraMaskDilationFadeStart));
            _penumbraMaskDilationFadeEnd = Unpack(o.Find(x => x.penumbraMaskDilationFadeEnd));
            _usePenumbraMask = Unpack(o.Find(x => x.usePenumbraMask));
            _shadowTemporalAccumulation = Unpack(o.Find(x => x.shadowTemporalAccumulation));
            _shadowSpatialDenoise = Unpack(o.Find(x => x.shadowSpatialDenoise));
            _shadowDenoiseKernelSize = Unpack(o.Find(x => x.shadowDenoiseKernelSize));
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
            PropertyField(_penumbraMaskDilation, EditorGUIUtility.TrTextContent("Penumbra Mask Max Dilation", "Conservative dilation radius for near penumbra mask pixels in mask texels. Larger values avoid PCSS edge artifacts at the cost of running PCSS on more pixels."));
            PropertyField(_penumbraMaskMinDilation, EditorGUIUtility.TrTextContent("Penumbra Mask Min Dilation", "Conservative dilation radius for distant penumbra mask pixels in mask texels."));
            PropertyField(_penumbraMaskDilationFadeStart, EditorGUIUtility.TrTextContent("Dilation Fade Start", "Camera-space distance where penumbra mask dilation starts fading down from the maximum value."));
            PropertyField(_penumbraMaskDilationFadeEnd, EditorGUIUtility.TrTextContent("Dilation Fade End", "Camera-space distance where penumbra mask dilation reaches the minimum value."));
            PropertyField(_usePenumbraMask, EditorGUIUtility.TrTextContent("Use Penumbra Mask", "Use the screen-space penumbra mask to skip PCSS work outside detected shadow edges. Disable this to match HDRP directional PCSS more closely."));
            PropertyField(_shadowTemporalAccumulation, EditorGUIUtility.TrTextContent("Shadow Temporal Accumulation", "Enable temporal accumulation for screen-space shadows."));
            PropertyField(_shadowSpatialDenoise, EditorGUIUtility.TrTextContent("Shadow Spatial Denoise", "Enable spatial bilateral denoising after temporal accumulation."));
            PropertyField(_shadowDenoiseKernelSize, EditorGUIUtility.TrTextContent("Shadow Denoise Kernel Size", "Filter radius for spatial bilateral shadow denoiser."));
        }
    }
}

