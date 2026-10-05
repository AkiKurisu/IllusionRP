using System;
using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    [PreferBinarySerialization]
    public class PRTProbeVolumeAsset : ScriptableObject
    {
        [SerializeField] private bool hasValidData;
        [SerializeField] private PRTProbeGrid grid;
        [SerializeField] private PRTBakeSignature signature;
        [SerializeField] private Bounds geometryBounds;
        [SerializeField] private int sectorWidth;
        [SerializeField, HideInInspector] private PRTProbeMetadata[] probes = Array.Empty<PRTProbeMetadata>();
        [SerializeField, HideInInspector] private PRTSectorData[] sectors = Array.Empty<PRTSectorData>();
        public PRTProbeGrid Grid => grid;
        public PRTBakeSignature Signature => signature;
        public Bounds GeometryBounds => geometryBounds;
        public int SectorWidth => sectorWidth;
        public PRTProbeMetadata[] Probes => probes;
        public PRTSectorData[] Sectors => sectors;
        public int SectorIndex(int globalProbeId)
        {
            int x = globalProbeId / (grid.count.y * grid.count.z);
            int z = globalProbeId % grid.count.z;
            return x / sectorWidth * ((grid.count.z + sectorWidth - 1) / sectorWidth) + z / sectorWidth;
        }
        public int LocalProbeIndex(int globalProbeId)
        {
            var sector = sectors[SectorIndex(globalProbeId)];
            return Array.BinarySearch(sector.probeIds, globalProbeId);
        }
        public bool HasValidData => hasValidData;
        public bool TryValidate(out string reason)
        {
            if (!hasValidData) { reason = "The probe volume has not been baked."; return false; }
            return PRTDataValidation.Validate(grid, sectorWidth, probes, sectors, out reason);
        }
        public void SetBakedData(PRTProbeGrid layout, PRTBakeSignature inputSignature, Bounds bounds, int width, PRTProbeMetadata[] metadata, PRTSectorData[] data)
        {
            if (!PRTDataValidation.Validate(layout, width, metadata, data, out string reason)) throw new ArgumentException(reason, nameof(data));
            grid = layout;
            signature = inputSignature;
            geometryBounds = bounds;
            sectorWidth = width;
            probes = metadata;
            sectors = data;
            hasValidData = true;
        }
        public void Clear()
        {
            hasValidData = false;
            probes = Array.Empty<PRTProbeMetadata>();
            sectors = Array.Empty<PRTSectorData>();
        }
    }
}
