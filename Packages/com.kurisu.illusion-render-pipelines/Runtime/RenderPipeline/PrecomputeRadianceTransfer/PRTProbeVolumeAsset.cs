using UnityEngine;

namespace Illusion.Rendering.PRTGI
{
    /// <summary>
    /// Asset for storing PRT probe volume data
    /// </summary>
    [PreferBinarySerialization]
    public class PRTProbeVolumeAsset : ScriptableObject
    {
        [SerializeField] 
        private bool hasValidData;

        [SerializeField, HideInInspector]
        private CellData cellData = CellData.GeDefault();

        /// <summary>
        /// Whether volume asset has valid data
        /// </summary>
        public bool HasValidData => hasValidData;

        /// <summary>
        /// Clear volume asset contained data
        /// </summary>
        public void Clear()
        {
            hasValidData = false;
            cellData = CellData.GeDefault();
        }

        /// <summary>
        /// Get and set cell data in this asset
        /// </summary>
        public CellData CellData
        {
            get => cellData;
            set 
            { 
                cellData = value;
                hasValidData = true;
            }
        }
    }
}