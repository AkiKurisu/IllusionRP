using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    public enum SubsurfaceScatteringSampleBudgetQualityLevel
    {
        Low = 20,
        Medium = 40,
        High = 80,
        Max = 1000
    }
    
    [Serializable]
    public sealed class SubsurfaceScatteringSampleBudgetQualityLevelParameter : VolumeParameter<SubsurfaceScatteringSampleBudgetQualityLevel>
    {
        public SubsurfaceScatteringSampleBudgetQualityLevelParameter(SubsurfaceScatteringSampleBudgetQualityLevel value, bool overrideState = false)
            : base(value, overrideState) { }
    }
    
     /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="DiffusionProfileAsset"/> value.
    /// </summary>
    [Serializable]
    public sealed class DiffusionProfilesParameter : VolumeParameter<DiffusionProfileAsset[]>
    {
        private static System.Buffers.ArrayPool<DiffusionProfileAsset> _arrayPool =
            System.Buffers.ArrayPool<DiffusionProfileAsset>.Create(DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT, 5);

        // To accumulate diffusion profiles when resolving stack and not make a new allocation everytime,
        // We allocate once an array with max size, and store the ammount of slots used here.
        internal DiffusionProfileAsset[] AccumulatedArray;
        
        internal int AccumulatedCount;

        /// <summary>
        /// Creates a new <see cref="DiffusionProfilesParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public DiffusionProfilesParameter(DiffusionProfileAsset[] value, bool overrideState = true)
            : base(value, overrideState) { }


        // Perform custom interpolation: We want to accumulate profiles instead of replacing them

        private void AddProfile(DiffusionProfileAsset profile)
        {
            if (profile == null)
                return;
            for (int i = 0; i < AccumulatedCount; i++)
            {
                if (profile == m_Value[i])
                    return;
            }

            m_Value[AccumulatedCount++] = profile;
        }
        
#if UNITY_2023_1_OR_NEWER
        /// <summary>
        /// Sets the value of this parameter to the value in <paramref name="parameter"/>.
        /// Implemented explicitly for DiffusionProfileList because we have internal state to copy.
        /// </summary>
        /// <param name="parameter">The <see cref="VolumeParameter"/> to copy the value from.</param>
        public override void SetValue(VolumeParameter parameter)
        {
            base.SetValue(parameter);

            var param = (DiffusionProfilesParameter)parameter;
            AccumulatedCount = param.AccumulatedCount;
        }
#endif
        
        /// <summary>
        /// Interpolates two values using a factor <paramref name="t"/>.
        /// </summary>
        /// <remarks>
        /// By default, this method does a "snap" interpolation, meaning it returns the value
        /// <paramref name="to"/> if <paramref name="t"/> is higher than 0, and <paramref name="from"/>
        /// otherwise.
        /// </remarks>
        /// <param name="from">The start value.</param>
        /// <param name="to">The end value.</param>
        /// <param name="t">The interpolation factor in range [0,1].</param>
        public override void Interp(DiffusionProfileAsset[] from, DiffusionProfileAsset[] to, float t)
        {
            m_Value = _arrayPool.Rent(DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT);

            AccumulatedCount = 0;
            
            if (to != null)
            {
                foreach (var profile in to)
                {
                    AddProfile(profile);
                    if (AccumulatedCount >= DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT)
                        break;
                }
            }
            if (from != null)
            {
                foreach (var profile in from)
                {
                    AddProfile(profile);
                    if (AccumulatedCount >= DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT)
                        break;
                }
            }

            for (int i = AccumulatedCount; i < m_Value.Length; i++)
                m_Value[i] = null;

            if (AccumulatedArray != null)
                _arrayPool.Return(AccumulatedArray);
            AccumulatedArray = m_Value;
        }

        /// <summary>
        /// Override this method to free all allocated resources
        /// </summary>
        public override void Release()
        {
            if (AccumulatedArray != null)
                _arrayPool.Return(AccumulatedArray);
            AccumulatedArray = null;
        }
    }
    
    [Serializable]
#if UNITY_2023_1_OR_NEWER
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeComponentMenu("Illusion/Subsurface Scattering")]
#else
    [VolumeComponentMenuForRenderPipeline("Illusion/Subsurface Scattering", typeof(UniversalRenderPipeline))]
#endif
    public class SubsurfaceScattering : VolumeComponent
    {
        public BoolParameter enable = new(false, BoolParameter.DisplayType.EnumPopup);
        
        [SerializeField]
        public DiffusionProfilesParameter diffusionProfiles = new(null);

        [Header("Performance")]
        [AdditionalProperty] 
        public SubsurfaceScatteringSampleBudgetQualityLevelParameter sampleBudget = new(SubsurfaceScatteringSampleBudgetQualityLevel.Medium);
        
        public bool IsActive()
        {
            return enable.value && diffusionProfiles.value.Length > 0;
        }
    }
}