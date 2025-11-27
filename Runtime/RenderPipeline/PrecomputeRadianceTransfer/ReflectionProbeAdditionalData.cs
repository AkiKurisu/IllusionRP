using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PRTGI
{
    [ExecuteAlways]
    [RequireComponent(typeof(ReflectionProbe))]
    public class ReflectionProbeAdditionalData : MonoBehaviour
    {
        [SerializeField] 
        internal bool hasValidSHForNormalization;
        
        [SerializeField] 
        internal SphericalHarmonicsL2 shForNormalization;

        private ReflectionProbe _reflectionProbe;

        private void Awake()
        {
            _reflectionProbe = GetComponent<ReflectionProbe>();
        }

        private void OnEnable()
        {
            PRTVolumeManager.RegisterReflectionProbeAdditionalData(_reflectionProbe, this);
        }

        private void OnDisable()
        {
            PRTVolumeManager.UnregisterReflectionProbeAdditionalData(_reflectionProbe, this);
        }

        public void SetSHCoefficients(Vector3[] coefficients)
        {
            hasValidSHForNormalization = true;
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 0, coefficients[0]); // Y_0_0
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 1, coefficients[1]); // Y_1_-1
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 2, coefficients[2]); // Y_1_0
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 3, coefficients[3]); // Y_1_1
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 4, coefficients[4]); // Y_2_-2
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 5, coefficients[5]); // Y_2_-1
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 6, coefficients[6]); // Y_2_0
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 7, coefficients[7]); // Y_2_1
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 8, coefficients[8]); // Y_2_2
        }
        
        public void SetSHCoefficients(float[] coefficients)
        {
            hasValidSHForNormalization = true;
            for (int i = 0; i < 9; i++)
            {
                shForNormalization[0, i] = coefficients[i * 3 + 0];
                shForNormalization[1, i] = coefficients[i * 3 + 1];
                shForNormalization[2, i] = coefficients[i * 3 + 2];
            }
        }
        
#if UNITY_EDITOR
        internal void ClearSHCoefficients()
        {
            hasValidSHForNormalization = false;
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 0, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 1, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 2, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 3, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 4, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 5, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 6, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 7, Vector3.zero);
            SphericalHarmonicsL2Utils.SetCoefficient(ref shForNormalization, 8, Vector3.zero);
        }
#endif
        
        // Return luma of coefficients
        public bool TryGetSHForNormalization(out Vector4 outL0L1, out Vector4 outL2_1, out float outL2_2)
        {
            if (!hasValidSHForNormalization)
            {
                // No valid data, so we disable the feature.
                outL0L1 = outL2_1 = Vector4.zero; outL2_2 = 0f;
                return false;
            }

            if (shForNormalization[0, 0] == float.MaxValue)
            {
                // Valid data, but probe is fully black. Setup coefficients so that light loop cancels out reflection probe contribution.
                outL0L1 = new Vector4(float.MaxValue, 0f, 0f, 0f);
                outL2_1 = Vector4.zero;
                outL2_2 = 0f;
                return true;
            }

            var L0 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 0);
            var L1_0 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 1);
            var L1_1 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 2);
            var L1_2 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 3);
            var L2_0 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 4);
            var L2_1 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 5);
            var L2_2 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 6);
            var L2_3 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 7);
            var L2_4 = SphericalHarmonicsL2Utils.GetCoefficient(shForNormalization, 8);

            // To evaluate L2, we need to fixup the coefficients.
            L0 -= L2_2;
            L2_2 *= 3.0f;

            outL0L1.x = ColorUtils.Luminance(new Color(L0.x, L0.y, L0.z));
            outL0L1.y = ColorUtils.Luminance(new Color(L1_0.x, L1_0.y, L1_0.z));
            outL0L1.z = ColorUtils.Luminance(new Color(L1_1.x, L1_1.y, L1_1.z));
            outL0L1.w = ColorUtils.Luminance(new Color(L1_2.x, L1_2.y, L1_2.z));
            outL2_1.x = ColorUtils.Luminance(new Color(L2_0.x, L2_0.y, L2_0.z));
            outL2_1.y = ColorUtils.Luminance(new Color(L2_1.x, L2_1.y, L2_1.z));
            outL2_1.z = ColorUtils.Luminance(new Color(L2_2.x, L2_2.y, L2_2.z));
            outL2_1.w = ColorUtils.Luminance(new Color(L2_3.x, L2_3.y, L2_3.z));
            outL2_2 = ColorUtils.Luminance(new Color(L2_4.x, L2_4.y, L2_4.z));

            return true;
        }
    }
}
