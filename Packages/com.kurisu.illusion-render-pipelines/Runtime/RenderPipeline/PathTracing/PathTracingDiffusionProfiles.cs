using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    // @IllusionRP: the material pass and HDRP random walk share the raster diffusion-profile constant layout.
    internal unsafe struct PathTracingDiffusionProfiles
    {
        public fixed float ShapeParamsAndMaxScatterDists[64];
        public fixed float TransmissionTintsAndFresnel0[64];
        public fixed float WorldScalesAndFilterRadiiAndThicknessRemaps[64];
        public fixed uint DiffusionProfileHashTable[64];
        public uint Count;
        public Vector3 Padding;

        public int ComputeHash()
        {
            unchecked
            {
                int hash = (int)Count;
                for (int i = 0; i < 64; ++i)
                {
                    hash = hash * 31 + ShapeParamsAndMaxScatterDists[i].GetHashCode();
                    hash = hash * 31 + WorldScalesAndFilterRadiiAndThicknessRemaps[i].GetHashCode();
                    hash = hash * 31 + TransmissionTintsAndFresnel0[i].GetHashCode();
                    hash = hash * 31 + (int)DiffusionProfileHashTable[i];
                }
                return hash;
            }
        }

        public static PathTracingDiffusionProfiles Capture(IllusionRendererData rendererData)
        {
            var result = new PathTracingDiffusionProfiles();
            var profiles = VolumeManager.instance.stack.GetComponent<SubsurfaceScattering>()?.diffusionProfiles.value;
            result.Count = 1;
            for (int i = 0; i < DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT; ++i)
            {
                for (int c = 0; c < 4; ++c)
                {
                    result.ShapeParamsAndMaxScatterDists[i * 4 + c] = DiffusionProfileAsset.NeutralShape[c];
                    result.TransmissionTintsAndFresnel0[i * 4 + c] = DiffusionProfileAsset.NeutralTransmission[c];
                    result.WorldScalesAndFilterRadiiAndThicknessRemaps[i * 4 + c] = c == 0
                        ? rendererData.ScaleInverseWorldDistance(DiffusionProfileAsset.NeutralWorldScale[c])
                        : DiffusionProfileAsset.NeutralWorldScale[c];
                }
            }
            if (profiles == null) return result;
            foreach (var asset in profiles)
            {
                if (!asset || asset.profile.hash == 0 || result.Count >= 16) continue;
                int index = (int)result.Count++;
                for (int c = 0; c < 4; ++c)
                {
                    result.ShapeParamsAndMaxScatterDists[index * 4 + c] = asset.shapeParamAndMaxScatterDist[c];
                    result.TransmissionTintsAndFresnel0[index * 4 + c] = asset.transmissionTintAndFresnel0[c];
                    result.WorldScalesAndFilterRadiiAndThicknessRemaps[index * 4 + c] = c == 0
                        ? rendererData.ScaleInverseWorldDistance(asset.worldScaleAndFilterRadiusAndThicknessRemap[c])
                        : asset.worldScaleAndFilterRadiusAndThicknessRemap[c];
                }
                result.DiffusionProfileHashTable[index * 4] = asset.profile.hash;
            }
            if (result.Count > 1)
            {
                // Materials without a matching profile resolve to slot 0, which takes the first listed profile as in raster.
                for (int c = 0; c < 4; ++c)
                {
                    result.ShapeParamsAndMaxScatterDists[c] = result.ShapeParamsAndMaxScatterDists[4 + c];
                    result.TransmissionTintsAndFresnel0[c] = result.TransmissionTintsAndFresnel0[4 + c];
                    result.WorldScalesAndFilterRadiiAndThicknessRemaps[c] = result.WorldScalesAndFilterRadiiAndThicknessRemaps[4 + c];
                }
            }
            return result;
        }
    }
}
