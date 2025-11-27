// Reference: https://github.com/CristianQiu/Unity-URP-Volumetric-Light

using UnityEditor;
using UnityEditor.Rendering;
using Illusion.Rendering.PostProcessing;

namespace Illusion.Rendering.Editor
{
	/// <summary>
	/// Custom editor for the volumetric fog volume component.
	/// </summary>
	[CustomEditor(typeof(VolumetricFog))]
	public sealed class VolumetricFogEditor : VolumeComponentEditor
	{
		#region Private Attributes

		private SerializedDataParameter _distance;
		private SerializedDataParameter _baseHeight;
		private SerializedDataParameter _maximumHeight;

		private SerializedDataParameter _enableGround;
		private SerializedDataParameter _groundHeight;

		private SerializedDataParameter _density;
		private SerializedDataParameter _attenuationDistance;
		
		private SerializedDataParameter _enableProbeVolumeContribution;
		private SerializedDataParameter _probeVolumeContributionWeight;

		private SerializedDataParameter _enableMainLightContribution;
		private SerializedDataParameter _anisotropy;
		private SerializedDataParameter _scattering;
		private SerializedDataParameter _tint;

		private SerializedDataParameter _enableAdditionalLightsContribution;

		private SerializedDataParameter _maxSteps;
		private SerializedDataParameter _blurIterations;
		private SerializedDataParameter _transmittanceThreshold;
		private SerializedDataParameter _enable;

		#endregion

		#region VolumeComponentEditor Methods

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		public override void OnEnable()
		{
			PropertyFetcher<VolumetricFog> pf =
				new PropertyFetcher<VolumetricFog>(serializedObject);

			_distance = Unpack(pf.Find(x => x.distance));
			_baseHeight = Unpack(pf.Find(x => x.baseHeight));
			_maximumHeight = Unpack(pf.Find(x => x.maximumHeight));

			_enableGround = Unpack(pf.Find(x => x.enableGround));
			_groundHeight = Unpack(pf.Find(x => x.groundHeight));

			_density = Unpack(pf.Find(x => x.density));
			_attenuationDistance = Unpack(pf.Find(x => x.attenuationDistance));
			
			_enableProbeVolumeContribution = Unpack(pf.Find(x => x.enableProbeVolumeContribution));
			_probeVolumeContributionWeight = Unpack(pf.Find(x => x.probeVolumeContributionWeight));

			_enableMainLightContribution = Unpack(pf.Find(x => x.enableMainLightContribution));
			_anisotropy = Unpack(pf.Find(x => x.anisotropy));
			_scattering = Unpack(pf.Find(x => x.scattering));
			_tint = Unpack(pf.Find(x => x.tint));

			_enableAdditionalLightsContribution = Unpack(pf.Find(x => x.enableAdditionalLightsContribution));

			_maxSteps = Unpack(pf.Find(x => x.maxSteps));
			_blurIterations = Unpack(pf.Find(x => x.blurIterations));
			_transmittanceThreshold = Unpack(pf.Find(x => x.transmittanceThreshold));
			_enable = Unpack(pf.Find(x => x.enable));
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		public override void OnInspectorGUI()
		{
			bool enabledGround = _enableGround.overrideState.boolValue && _enableGround.value.boolValue;
			bool enabledMainLightContribution = _enableMainLightContribution.overrideState.boolValue &&
												_enableMainLightContribution.value.boolValue;

			PropertyField(_enable);
			PropertyField(_distance);
			PropertyField(_baseHeight);
			PropertyField(_maximumHeight);

			PropertyField(_enableGround);
			if (enabledGround)
				PropertyField(_groundHeight);

			PropertyField(_density);
			PropertyField(_attenuationDistance);
			
			bool enabledAPVContribution = _enableProbeVolumeContribution.overrideState.boolValue && _enableProbeVolumeContribution.value.boolValue;
			PropertyField(_enableProbeVolumeContribution);
			if (enabledAPVContribution)
				PropertyField(_probeVolumeContributionWeight);

			PropertyField(_enableMainLightContribution);
			if (enabledMainLightContribution)
			{
				PropertyField(_anisotropy);
				PropertyField(_scattering);
				PropertyField(_tint);
			}

			PropertyField(_enableAdditionalLightsContribution);

			PropertyField(_maxSteps);
			PropertyField(_blurIterations);
			PropertyField(_transmittanceThreshold);
		}

		#endregion
	}
}