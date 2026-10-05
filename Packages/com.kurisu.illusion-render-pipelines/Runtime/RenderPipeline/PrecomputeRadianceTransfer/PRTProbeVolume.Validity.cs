using System;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        private bool _hasMetadataState;
        private int _metadataStateHash;

        private static float CalculateProbeIntensityScale(Vector3 position)
        {
            float intensity = 1;
            foreach (var volume in PRTVolumeManager.AdjustmentVolumes)
                if (volume && volume.isActiveAndEnabled && volume.Contains(position))
                    intensity *= volume.GetIntensityScale();
            return intensity;
        }

        private static bool ShouldInvalidateProbe(Vector3 position)
        {
            foreach (var volume in PRTVolumeManager.AdjustmentVolumes)
                if (volume && volume.isActiveAndEnabled && volume.Contains(position) && volume.ShouldInvalidateProbe())
                    return true;
            return false;
        }

        private void InitializeValidityData()
        {
            if (Probes == null)
                return;
            _validity = new uint[Probes.Length];
            for (int i = 0; i < Probes.Length; i++)
            {
                Vector3 position = Probes[i].Position;
                _validity[i] = PRTProbeValidity.Pack(CalculateProbeIntensityScale(position), ShouldInvalidateProbe(position) ? 0 : 1);
            }
            _hasMetadataState = false;
        }

        private void RefreshRuntimeValidity()
        {
            if (!_isDataInitialized || Probes == null)
                return;
            var hash = new HashCode();
            hash.Add(RuntimeDataId);
            foreach (var volume in PRTVolumeManager.AdjustmentVolumes)
            {
                if (!volume || !volume.isActiveAndEnabled ||
                    volume.mode is not (PRTProbeAdjustmentMode.IntensityScale or PRTProbeAdjustmentMode.InvalidateProbes))
                    continue;
                hash.Add(volume.GetInstanceID());
                hash.Add(volume.transform.localToWorldMatrix);
                hash.Add(volume.mode);
                hash.Add(volume.shape);
                hash.Add(volume.size);
                hash.Add(volume.radius);
                hash.Add(volume.intensityScale);
            }
            int state = hash.ToHashCode();
            if (_hasMetadataState && _metadataStateHash == state)
                return;
            _metadataStateHash = state;
            _hasMetadataState = true;
            _validity = new uint[Probes.Length];
            var content = new HashCode();
            for (int i = 0; i < Probes.Length; i++)
            {
                Vector3 position = Probes[i].Position;
                float validity = (_allProbes[i].validity >> 24) / 255f;
                if (ShouldInvalidateProbe(position))
                    validity = 0;
                _validity[i] = PRTProbeValidity.Pack(CalculateProbeIntensityScale(position), validity);
                content.Add(_validity[i]);
            }
            MetadataHash = content.ToHashCode();
        }

        public bool IsProbeValid(int index) => _validity != null && index >= 0 && index < _validity.Length && (_validity[index] >> 24) > 127;
    }
}
