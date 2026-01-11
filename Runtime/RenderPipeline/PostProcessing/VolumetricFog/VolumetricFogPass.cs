// Modified from https://github.com/CristianQiu/Unity-URP-Volumetric-Light
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Collections;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering.PostProcessing
{
	/// <summary>
	/// The volumetric fog render pass.
	/// </summary>
	public sealed class VolumetricFogPass : ScriptableRenderPass, IDisposable
	{
		/// <summary>
		/// Downsampling factor for the camera depth texture that the volumetric fog will use to render
		/// the fog.
		/// </summary>
		private enum DownsampleFactor : byte
		{
			Half = 2,
		}

	#if UNITY_2023_1_OR_NEWER
		/// <summary>
		/// PassData for downsample depth pass.
		/// </summary>
		private class DownsampleDepthPassData
		{
			public Material DownsampleDepthMaterial;
			public int PassIndex;
		}

		/// <summary>
		/// PassData for raymarch pass.
		/// </summary>
		private class RaymarchPassData
		{
			public bool UseComputeShader;
			// Fragment shader path
			public Material VolumetricFogMaterial;
			public int PassIndex;
			// Compute shader path
			public ComputeShader RaymarchCS;
			public int RaymarchKernel;
			public int Width;
			public int Height;
			public int ViewCount;
			// Common
			public TextureHandle DownsampledDepthTexture;
			public TextureHandle ExposureTexture;
			public TextureHandle OutputTexture;
			public LightData LightData;
			public VolumetricLightManager VolumetricLightManager;
			public VolumetricFog VolumeSettings;
			public IllusionRendererData RendererData;
		}

		/// <summary>
		/// PassData for blur pass.
		/// </summary>
		private class BlurPassData
		{
			public bool UseComputeShader;
			public int BlurIterations;
			// Fragment shader path
			public Material VolumetricFogMaterial;
			public int HorizontalBlurPassIndex;
			public int VerticalBlurPassIndex;
			// Compute shader path
			public ComputeShader BlurCS;
			public int BlurKernel;
			public int Width;
			public int Height;
			// Common
			public TextureHandle InputTexture;
			public TextureHandle TempBlurTexture;
			public TextureHandle DownsampledDepthTexture;
		}

		/// <summary>
		/// PassData for upsample pass.
		/// </summary>
		private class UpsamplePassData
		{
			public bool UseComputeShader;
			// Fragment shader path
			public Material VolumetricFogMaterial;
			public int PassIndex;
			// Compute shader path
			public ComputeShader UpsampleCS;
			public int UpsampleKernel;
			public ShaderVariablesBilateralUpsample UpsampleVariables;
			public int Width;
			public int Height;
			public int ViewCount;
			// Common
			public TextureHandle VolumetricFogTexture;
			public TextureHandle CameraColorTexture;
			public TextureHandle DownsampledDepthTexture;
			public TextureHandle CameraDepthTexture;
			public TextureHandle OutputTexture;
		}

		/// <summary>
		/// PassData for composite pass.
		/// </summary>
		private class CompositePassData
		{
			public TextureHandle SourceTexture;
		}
#endif
	
		private const string DownsampledCameraDepthRTName = "_DownsampledCameraDepth";
		private const string VolumetricFogRenderRTName = "_VolumetricFog";
		private const string VolumetricFogBlurRTName = "_VolumetricFogBlur";
		private const string VolumetricFogUpsampleCompositionRTName = "_VolumetricFogUpsampleComposition";

		private static readonly float[] Anisotropies = new float[UniversalRenderPipeline.maxVisibleAdditionalLights];
		private static readonly float[] Scatterings = new float[UniversalRenderPipeline.maxVisibleAdditionalLights];
		private static readonly float[] RadiiSq = new float[UniversalRenderPipeline.maxVisibleAdditionalLights];

		private int _downsampleDepthPassIndex;
		private int _volumetricFogRenderPassIndex;
		private int _volumetricFogHorizontalBlurPassIndex;
		private int _volumetricFogVerticalBlurPassIndex;
		private int _volumetricFogDepthAwareUpsampleCompositionPassIndex;

		private readonly LazyMaterial _downsampleDepthMaterial = new(IllusionShaders.DownsampleDepth);
		private readonly LazyMaterial _volumetricFogMaterial = new(IllusionShaders.VolumetricFog);

		private readonly ComputeShader _volumetricFogRaymarchCS;
		private readonly int _volumetricFogRaymarchKernel;

		private readonly ComputeShader _volumetricFogBlurCS;
		private readonly int _volumetricFogBlurKernel;
		
		private readonly ComputeShader _bilateralUpsampleCS;
		private readonly int _bilateralUpSampleColorKernel;

		private RTHandle _downsampledCameraDepthRTHandle;
		private RTHandle _volumetricFogRenderRTHandle;
		private RTHandle _volumetricFogBlurRTHandle;
		private RTHandle _volumetricFogUpsampleCompositionRTHandle;

		private readonly ProfilingSampler _downsampleDepthProfilingSampler = new("Downsample Depth");
		private readonly ProfilingSampler _raymarchSampler = new("Raymarch");
		private readonly ProfilingSampler _blurSampler = new("Blur");
		private readonly ProfilingSampler _upsampleSampler = new("Upsample");
		private readonly ProfilingSampler _compositeSampler = new("Composite");

		private bool _upsampleInCS;
		private bool _raymarchInCS;
		private bool _blurInCS;

		private int _rtWidth;
		private int _rtHeight;

		private ShaderVariablesBilateralUpsample _shaderVariablesBilateralUpsampleCB;

		private readonly IllusionRendererData _rendererData;

		private VolumetricLightManager _volumetricLightManager;

		/// <summary>
		/// Constructor.
		/// </summary>
		/// <param name="rendererData"></param>
		public VolumetricFogPass(IllusionRendererData rendererData)
		{
			_rendererData = rendererData;
			profilingSampler = new ProfilingSampler("Volumetric Fog");
			renderPassEvent = IllusionRenderPassEvent.VolumetricFogPass;
			_bilateralUpsampleCS = rendererData.RuntimeResources.volumetricFogUpsampleCS;
			_bilateralUpSampleColorKernel = _bilateralUpsampleCS.FindKernel("VolumetricFogBilateralUpSample");
			_volumetricFogRaymarchCS = rendererData.RuntimeResources.volumetricFogRaymarchCS;
			_volumetricFogRaymarchKernel = _volumetricFogRaymarchCS.FindKernel("VolumetricFogRaymarch");
			_volumetricFogBlurCS = rendererData.RuntimeResources.volumetricFogBlurCS;
			_volumetricFogBlurKernel = _volumetricFogBlurCS.FindKernel("VolumetricFogBlur");
			InitializePassesIndices();
		}

		/// <summary>
		/// Initializes the passes indices.
		/// </summary>
		private void InitializePassesIndices()
		{
			_downsampleDepthPassIndex = _downsampleDepthMaterial.Value.FindPass("DownsampleDepth");
			_volumetricFogRenderPassIndex = _volumetricFogMaterial.Value.FindPass("VolumetricFogRender");
			_volumetricFogHorizontalBlurPassIndex = _volumetricFogMaterial.Value.FindPass("VolumetricFogHorizontalBlur");
			_volumetricFogVerticalBlurPassIndex = _volumetricFogMaterial.Value.FindPass("VolumetricFogVerticalBlur");
			_volumetricFogDepthAwareUpsampleCompositionPassIndex = _volumetricFogMaterial.Value.FindPass("VolumetricFogDepthAwareUpsampleComposition");
		}
		
		public void Setup(VolumetricLightManager volumetricLightManager)
		{
			_volumetricLightManager = volumetricLightManager;
		}

		/// <summary>
		/// Prepares volumetric fog data from rendering data.
		/// </summary>
		/// <param name="renderingData"></param>
		private void PrepareVolumetricFogData(ref RenderingData renderingData)
		{
			_raymarchInCS = _rendererData.PreferComputeShader;
			_blurInCS = _rendererData.PreferComputeShader;
			_upsampleInCS = _rendererData.PreferComputeShader;

			var descriptor = renderingData.cameraData.cameraTargetDescriptor;
			_rtWidth = descriptor.width;
			_rtHeight = descriptor.height;

			// Prepare bilateral upsample constants
			_shaderVariablesBilateralUpsampleCB._HalfScreenSize = new Vector4(_rtWidth / 2, _rtHeight / 2,
				1.0f / (_rtWidth * 0.5f), 1.0f / (_rtHeight * 0.5f));
			unsafe
			{
				for (int i = 0; i < 16; ++i)
					_shaderVariablesBilateralUpsampleCB._DistanceBasedWeights[i] = BilateralUpsample.distanceBasedWeights_2x2[i];

				for (int i = 0; i < 32; ++i)
					_shaderVariablesBilateralUpsampleCB._TapOffsets[i] = BilateralUpsample.tapOffsets_2x2[i];
			}
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="cmd"></param>
		/// <param name="renderingData"></param>
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			PrepareVolumetricFogData(ref renderingData);

			var descriptor = renderingData.cameraData.cameraTargetDescriptor;
				descriptor.depthBufferBits = (int)DepthBits.None;

				RenderTextureFormat originalColorFormat = descriptor.colorFormat;
				Vector2Int originalResolution = new Vector2Int(descriptor.width, descriptor.height);

				descriptor.width /= (int)DownsampleFactor.Half;
				descriptor.height /= (int)DownsampleFactor.Half;
				descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
				RenderingUtils.ReAllocateIfNeeded(ref _downsampledCameraDepthRTHandle, descriptor,
					wrapMode: TextureWrapMode.Clamp, name: DownsampledCameraDepthRTName);

				descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
				if (_raymarchInCS)
				{
					descriptor.enableRandomWrite = true;
				}
				RenderingUtils.ReAllocateIfNeeded(ref _volumetricFogRenderRTHandle, descriptor, wrapMode: TextureWrapMode.Clamp,
					name: VolumetricFogRenderRTName);

				// Blur RT needs random write access if using compute shader
				if (_blurInCS)
				{
					descriptor.enableRandomWrite = true;
				}
				else
				{
					descriptor.enableRandomWrite = false;
				}
				RenderingUtils.ReAllocateIfNeeded(ref _volumetricFogBlurRTHandle, descriptor, wrapMode: TextureWrapMode.Clamp,
					name: VolumetricFogBlurRTName);

				descriptor.width = originalResolution.x;
				descriptor.height = originalResolution.y;
				descriptor.colorFormat = originalColorFormat;
			if (_upsampleInCS)
			{
				descriptor.enableRandomWrite = true;
			}
			RenderingUtils.ReAllocateIfNeeded(ref _volumetricFogUpsampleCompositionRTHandle, descriptor,
				wrapMode: TextureWrapMode.Clamp, name: VolumetricFogUpsampleCompositionRTName);
		}

		public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
			ConfigureInput(ScriptableRenderPassInput.Depth);
		}

		/// <summary>
		/// <inheritdoc/>
		/// </summary>
		/// <param name="context"></param>
		/// <param name="renderingData"></param>
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			CommandBuffer cmd = CommandBufferPool.Get();

			using (new ProfilingScope(cmd, _downsampleDepthProfilingSampler))
			{
				Blitter.BlitCameraTexture(cmd, _downsampledCameraDepthRTHandle, _downsampledCameraDepthRTHandle,
					_downsampleDepthMaterial.Value, _downsampleDepthPassIndex);
				_volumetricFogMaterial.Value.SetTexture(ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);
			}

			using (new ProfilingScope(cmd, profilingSampler))
			{
				using (new ProfilingScope(cmd, _raymarchSampler))
				{
					DoRaymarch(cmd, ref renderingData);
				}

				using (new ProfilingScope(cmd, _blurSampler))
				{
					DoBlur(cmd, ref renderingData);
				}

				using (new ProfilingScope(cmd, _upsampleSampler))
				{
					DoUpsample(cmd, ref renderingData);
				}

				using (new ProfilingScope(cmd, _compositeSampler))
				{
					var cameraColorRt = renderingData.cameraData.renderer.cameraColorTargetHandle;
					Blitter.BlitCameraTexture(cmd, _volumetricFogUpsampleCompositionRTHandle, cameraColorRt);
				}
			}

			context.ExecuteCommandBuffer(cmd);

			cmd.Clear();

			CommandBufferPool.Release(cmd);
		}

		private void DoBlur(CommandBuffer cmd, ref RenderingData renderingData)
		{
			int blurIterations = VolumeManager.instance.stack.GetComponent<VolumetricFog>().blurIterations.value;

			if (_blurInCS)
			{
				DoBlurComputeShader(cmd, blurIterations);
			}
			else
			{
				DoBlurFragmentShader(cmd, blurIterations);
			}
		}

		private void DoBlurFragmentShader(CommandBuffer cmd, int blurIterations)
		{
			for (int i = 0; i < blurIterations; ++i)
			{
				Blitter.BlitCameraTexture(cmd, _volumetricFogRenderRTHandle, _volumetricFogBlurRTHandle,
					_volumetricFogMaterial.Value, _volumetricFogHorizontalBlurPassIndex);
				Blitter.BlitCameraTexture(cmd, _volumetricFogBlurRTHandle, _volumetricFogRenderRTHandle,
					_volumetricFogMaterial.Value, _volumetricFogVerticalBlurPassIndex);
			}
		}

		private void DoBlurComputeShader(CommandBuffer cmd, int blurIterations)
		{
			// Set input texture dimensions
			int halfWidth = _rtWidth / 2;
			int halfHeight = _rtHeight / 2;
			Vector2 texelSize = new Vector2(1.0f / halfWidth, 1.0f / halfHeight);

			cmd.SetComputeVectorParam(_volumetricFogBlurCS, ShaderIDs._BlurInputTexelSizeId, texelSize);

			// Calculate dispatch dimensions (updated for new 8x8 thread group size)
			int groupsX = IllusionRenderingUtils.DivRoundUp(halfWidth, 8);
			int groupsY = IllusionRenderingUtils.DivRoundUp(halfHeight, 8);

			for (int i = 0; i < blurIterations; ++i)
			{
				cmd.SetComputeTextureParam(_volumetricFogBlurCS, _volumetricFogBlurKernel, ShaderIDs._BlurInputTextureId, _volumetricFogRenderRTHandle);
				cmd.SetComputeTextureParam(_volumetricFogBlurCS, _volumetricFogBlurKernel, ShaderIDs._BlurOutputTextureId, _volumetricFogBlurRTHandle);
				cmd.SetComputeTextureParam(_volumetricFogBlurCS, _volumetricFogBlurKernel, ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);
				cmd.DispatchCompute(_volumetricFogBlurCS, _volumetricFogBlurKernel, groupsX, groupsY, 1);

				// Swap buffers for next iteration
				if (i < blurIterations - 1)
				{
					(_volumetricFogRenderRTHandle, _volumetricFogBlurRTHandle) = (_volumetricFogBlurRTHandle, _volumetricFogRenderRTHandle);
				}
				else
				{
					// Copy final result back to render handle if needed
					cmd.CopyTexture(_volumetricFogBlurRTHandle, _volumetricFogRenderRTHandle);
				}
			}
		}

		private void DoUpsample(CommandBuffer cmd, ref RenderingData renderingData)
		{
			var cameraColorRt = renderingData.cameraData.renderer.cameraColorTargetHandle;
			if (_upsampleInCS)
			{
				ConstantBuffer.Push(cmd, _shaderVariablesBilateralUpsampleCB, _bilateralUpsampleCS, ShaderIDs.ShaderVariablesBilateralUpsample);

				// Inject all the input buffers
				cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._LowResolutionTexture, _volumetricFogRenderRTHandle);
				cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._CameraColorTexture, cameraColorRt);
				cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);

				// Inject the output textures
				cmd.SetComputeTextureParam(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, ShaderIDs._OutputUpscaledTexture, _volumetricFogUpsampleCompositionRTHandle);

				// Upscale the buffer to full resolution
				int groupsX = IllusionRenderingUtils.DivRoundUp(_rtWidth, 8);
				int groupsY = IllusionRenderingUtils.DivRoundUp(_rtHeight, 8);
				cmd.DispatchCompute(_bilateralUpsampleCS, _bilateralUpSampleColorKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
			}
			else
			{
				_volumetricFogMaterial.Value.SetTexture(ShaderIDs._VolumetricFogTexture, _volumetricFogRenderRTHandle);
				Blitter.BlitCameraTexture(cmd, cameraColorRt, _volumetricFogUpsampleCompositionRTHandle,
					_volumetricFogMaterial.Value, _volumetricFogDepthAwareUpsampleCompositionPassIndex);
			}
		}

		private void DoRaymarch(CommandBuffer cmd, ref RenderingData renderingData)
		{
			if (_raymarchInCS)
			{
				UpdateVolumetricFogComputeShaderParameters(cmd, _volumetricFogRaymarchCS,
					renderingData.lightData.mainLightIndex,
					renderingData.lightData.additionalLightsCount, renderingData.lightData.visibleLights);

				DoRaymarchComputeShader(cmd);
			}
			else
			{
				UpdateVolumetricFogMaterialParameters(_volumetricFogMaterial.Value,
					renderingData.lightData.mainLightIndex,
					renderingData.lightData.additionalLightsCount, renderingData.lightData.visibleLights);
				Blitter.BlitCameraTexture(cmd, _volumetricFogRenderRTHandle, _volumetricFogRenderRTHandle,
					_volumetricFogMaterial.Value, _volumetricFogRenderPassIndex);
			}
		}

		private void DoRaymarchComputeShader(CommandBuffer cmd)
		{
			// Set input textures
			cmd.SetComputeTextureParam(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel,
				ShaderIDs._DownsampledCameraDepthTexture, _downsampledCameraDepthRTHandle);
			// Exposure texture may not be initialized in the first frame
			cmd.SetComputeTextureParam(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel,
				IllusionShaderProperties._ExposureTexture, _rendererData.GetExposureTexture());

			// Set output texture
			cmd.SetComputeTextureParam(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel,
				ShaderIDs._VolumetricFogOutput, _volumetricFogRenderRTHandle);

			cmd.SetComputeTextureParam(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel,
				ShaderIDs._VolumetricFogOutput, _volumetricFogRenderRTHandle);

			// Dispatch compute shader
			int halfWidth = _rtWidth / 2;
			int halfHeight = _rtHeight / 2;
			int groupsX = IllusionRenderingUtils.DivRoundUp(halfWidth, 8);
			int groupsY = IllusionRenderingUtils.DivRoundUp(halfHeight, 8);
			cmd.DispatchCompute(_volumetricFogRaymarchCS, _volumetricFogRaymarchKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
		}

		/// <summary>
		/// Updates the volumetric fog material parameters.
		/// </summary>
		/// <param name="volumetricFogMaterial"></param>
		/// <param name="mainLightIndex"></param>
		/// <param name="additionalLightsCount"></param>
		/// <param name="visibleLights"></param>
		private void UpdateVolumetricFogMaterialParameters(Material volumetricFogMaterial,
			int mainLightIndex, int additionalLightsCount,
			NativeArray<VisibleLight> visibleLights)
		{
			VolumetricFog fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFog>();

			bool enableMainLightContribution = fogVolume.enableMainLightContribution.value &&
											   fogVolume.scattering.value > 0.0f && mainLightIndex > -1;
			bool enableAdditionalLightsContribution =
				fogVolume.enableAdditionalLightsContribution.value && additionalLightsCount > 0;
			
			bool enableProbeVolumeContribution = fogVolume.enableProbeVolumeContribution.value 
										&& fogVolume.probeVolumeContributionWeight.value > 0.0f
										&&  _rendererData.SampleProbeVolumes;
			if (enableProbeVolumeContribution)
				volumetricFogMaterial.EnableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");
			else
				volumetricFogMaterial.DisableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");

			if (enableMainLightContribution)
				volumetricFogMaterial.DisableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");
			else
				volumetricFogMaterial.EnableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");

			if (enableAdditionalLightsContribution)
				volumetricFogMaterial.DisableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
			else
				volumetricFogMaterial.EnableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");

			UpdateLightsParameters(volumetricFogMaterial,
				fogVolume, enableMainLightContribution,
				enableAdditionalLightsContribution, mainLightIndex, visibleLights);

			volumetricFogMaterial.SetInteger(ShaderIDs.FrameCountId, Time.renderedFrameCount % 64);
			volumetricFogMaterial.SetInteger(ShaderIDs.CustomAdditionalLightsCountId, additionalLightsCount);
			volumetricFogMaterial.SetFloat(ShaderIDs.DistanceId, fogVolume.distance.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.BaseHeightId, fogVolume.baseHeight.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.MaximumHeightId, fogVolume.maximumHeight.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.GroundHeightId,
				(fogVolume.enableGround.overrideState && fogVolume.enableGround.value)
					? fogVolume.groundHeight.value
					: float.MinValue);
			volumetricFogMaterial.SetFloat(ShaderIDs.DensityId, fogVolume.density.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.AbsortionId, 1.0f / fogVolume.attenuationDistance.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.ProbeVolumeContributionWeigthId, fogVolume.enableProbeVolumeContribution.value ? fogVolume.probeVolumeContributionWeight.value : 0.0f);
			volumetricFogMaterial.SetColor(ShaderIDs.TintId, fogVolume.tint.value);
			volumetricFogMaterial.SetInteger(ShaderIDs.MaxStepsId, fogVolume.maxSteps.value);
			volumetricFogMaterial.SetFloat(ShaderIDs.TransmittanceThresholdId, fogVolume.transmittanceThreshold.value);
		}

		/// <summary>
		/// Updates the volumetric fog compute shader parameters.
		/// </summary>
		/// <param name="cmd"></param>
		/// <param name="volumetricFogCS"></param>
		/// <param name="mainLightIndex"></param>
		/// <param name="additionalLightsCount"></param>
		/// <param name="visibleLights"></param>
		private void UpdateVolumetricFogComputeShaderParameters(CommandBuffer cmd,
			ComputeShader volumetricFogCS,
			int mainLightIndex, int additionalLightsCount,
			NativeArray<VisibleLight> visibleLights)
		{
			VolumetricFog fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFog>();

			bool enableMainLightContribution = fogVolume.enableMainLightContribution.value &&
											   fogVolume.scattering.value > 0.0f && mainLightIndex > -1;
			bool enableAdditionalLightsContribution =
				fogVolume.enableAdditionalLightsContribution.value && additionalLightsCount > 0;

			// Set compute shader keywords
			if (enableMainLightContribution)
				volumetricFogCS.DisableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");
			else
				volumetricFogCS.EnableKeyword("_MAIN_LIGHT_CONTRIBUTION_DISABLED");

			if (enableAdditionalLightsContribution)
				volumetricFogCS.DisableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
			else
				volumetricFogCS.EnableKeyword("_ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED");
			
			bool enableProbeVolumeContribution = fogVolume.enableProbeVolumeContribution.value 
										&& fogVolume.probeVolumeContributionWeight.value > 0.0f
										&&  _rendererData.SampleProbeVolumes;
			if (enableProbeVolumeContribution)
				volumetricFogCS.EnableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");
			else
				volumetricFogCS.DisableKeyword("_PROBE_VOLUME_CONTRIBUTION_ENABLED");

			UpdateLightsParametersCS(cmd, volumetricFogCS,
				fogVolume, enableMainLightContribution,
				enableAdditionalLightsContribution, mainLightIndex, visibleLights);

			// Set compute shader parameters
			cmd.SetComputeIntParam(volumetricFogCS, ShaderIDs.FrameCountId, Time.renderedFrameCount % 64);
			cmd.SetComputeIntParam(volumetricFogCS, ShaderIDs.CustomAdditionalLightsCountId, additionalLightsCount);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.DistanceId, fogVolume.distance.value);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.BaseHeightId, fogVolume.baseHeight.value);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.MaximumHeightId, fogVolume.maximumHeight.value);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.GroundHeightId,
				(fogVolume.enableGround.overrideState && fogVolume.enableGround.value)
					? fogVolume.groundHeight.value
					: float.MinValue);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.DensityId, fogVolume.density.value);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.AbsortionId, 1.0f / fogVolume.attenuationDistance.value);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.ProbeVolumeContributionWeigthId, fogVolume.enableProbeVolumeContribution.value ? fogVolume.probeVolumeContributionWeight.value : 0.0f);
			cmd.SetComputeVectorParam(volumetricFogCS, ShaderIDs.TintId, fogVolume.tint.value);
			cmd.SetComputeIntParam(volumetricFogCS, ShaderIDs.MaxStepsId, fogVolume.maxSteps.value);
			cmd.SetComputeFloatParam(volumetricFogCS, ShaderIDs.TransmittanceThresholdId, fogVolume.transmittanceThreshold.value);
		}

		/// <summary>
		/// Updates the lights parameters from the material.
		/// </summary>
		/// <param name="volumetricFogMaterial"></param>
		/// <param name="fogVolume"></param>
		/// <param name="enableMainLightContribution"></param>
		/// <param name="enableAdditionalLightsContribution"></param>
		/// <param name="mainLightIndex"></param>
		/// <param name="visibleLights"></param>
		private static void UpdateLightsParameters(Material volumetricFogMaterial, VolumetricFog fogVolume, 
			bool enableMainLightContribution, bool enableAdditionalLightsContribution,
			int mainLightIndex, NativeArray<VisibleLight> visibleLights)
		{
			if (enableMainLightContribution)
			{
				Anisotropies[visibleLights.Length - 1] = fogVolume.anisotropy.value;
				Scatterings[visibleLights.Length - 1] = fogVolume.scattering.value;
			}

			if (enableAdditionalLightsContribution)
			{
				int additionalLightIndex = 0;

				for (int i = 0; i < visibleLights.Length; ++i)
				{
					if (i == mainLightIndex)
						continue;

					float anisotropy = 0.0f;
					float scattering = 0.0f;
					float radius = 0.0f;

					if (VolumetricLightManager.TryGetVolumetricAdditionalLight(visibleLights[i].light, out var volumetricLight))
					{
						if (volumetricLight.gameObject.activeInHierarchy && volumetricLight.enabled)
						{
							anisotropy = volumetricLight.Anisotropy;
							scattering = volumetricLight.Scattering;
							radius = volumetricLight.Radius;
						}
					}

					Anisotropies[additionalLightIndex] = anisotropy;
					Scatterings[additionalLightIndex] = scattering;
					RadiiSq[additionalLightIndex++] = radius * radius;
				}
			}

			if (enableMainLightContribution || enableAdditionalLightsContribution)
			{
				volumetricFogMaterial.SetFloatArray(ShaderIDs.AnisotropiesArrayId, Anisotropies);
				volumetricFogMaterial.SetFloatArray(ShaderIDs.ScatteringsArrayId, Scatterings);
				volumetricFogMaterial.SetFloatArray(ShaderIDs.RadiiSqArrayId, RadiiSq);
			}
		}

		/// <summary>
		/// Updates the lights parameters for compute shader.
		/// </summary>
		/// <param name="cmd"></param>
		/// <param name="volumetricFogCS"></param>
		/// <param name="fogVolume"></param>
		/// <param name="enableMainLightContribution"></param>
		/// <param name="enableAdditionalLightsContribution"></param>
		/// <param name="mainLightIndex"></param>
		/// <param name="visibleLights"></param>
		private static void UpdateLightsParametersCS(CommandBuffer cmd, ComputeShader volumetricFogCS, VolumetricFog fogVolume, 
			bool enableMainLightContribution,
			bool enableAdditionalLightsContribution,
			int mainLightIndex, NativeArray<VisibleLight> visibleLights)
		{
			for (int i = 0; i < UniversalRenderPipeline.maxVisibleAdditionalLights; ++i)
			{
				Anisotropies[i] = Scatterings[i] = RadiiSq[i] = 0;
			}

			if (enableMainLightContribution)
			{
				Anisotropies[visibleLights.Length - 1] = fogVolume.anisotropy.value;
				Scatterings[visibleLights.Length - 1] = fogVolume.scattering.value;
			}

			if (enableAdditionalLightsContribution)
			{
				int additionalLightIndex = 0;

				for (int i = 0; i < visibleLights.Length; ++i)
				{
					if (i == mainLightIndex)
						continue;

					float anisotropy = 0.0f;
					float scattering = 0.0f;
					float radius = 0.0f;

					if (VolumetricLightManager.TryGetVolumetricAdditionalLight(visibleLights[i].light, out var volumetricLight))
					{
						if (volumetricLight.gameObject.activeInHierarchy && volumetricLight.enabled)
						{
							anisotropy = volumetricLight.Anisotropy;
							scattering = volumetricLight.Scattering;
							radius = volumetricLight.Radius;
						}
					}

					Anisotropies[additionalLightIndex] = anisotropy;
					Scatterings[additionalLightIndex] = scattering;
					RadiiSq[additionalLightIndex++] = radius * radius;
				}
			}

			// Always push buffer in CS
			cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.AnisotropiesArrayId, Anisotropies);
			cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.ScatteringsArrayId, Scatterings);
			cmd.SetComputeFloatParams(volumetricFogCS, ShaderIDs.RadiiSqArrayId, RadiiSq);
		}

#if UNITY_2023_1_OR_NEWER
		/// <summary>
		/// Records render graph for volumetric fog.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="frameResources"></param>
		/// <param name="renderingData"></param>
		public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
		{
			// Early exit if fog is not active
			var fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFog>();
			if (fogVolume == null || !fogVolume.IsActive())
				return;

			// Prepare data
			PrepareVolumetricFogData(ref renderingData);

			UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;

			// Get input textures from frame resources
			TextureHandle cameraDepthTexture = frameResources.GetTexture(UniversalResource.CameraDepthTexture);
			TextureHandle cameraColorTexture = renderer.activeColorTexture;

			// Import external RTHandles
			TextureHandle exposureTexture = renderGraph.ImportTexture(_rendererData.GetExposureTexture());

			// Get shadow textures if available
			TextureHandle mainShadowsTexture = frameResources.GetTexture(UniversalResource.MainShadowsTexture);
			TextureHandle additionalShadowsTexture = frameResources.GetTexture(UniversalResource.AdditionalShadowsTexture);

			// Execute sub-passes in sequence
			var downsampledDepth = RenderDownsampleDepthPass(renderGraph, cameraDepthTexture);
			var volumetricFogTexture = RenderRaymarchPass(renderGraph, downsampledDepth, exposureTexture,
				mainShadowsTexture, additionalShadowsTexture, renderingData.lightData);
			volumetricFogTexture = RenderBlurPass(renderGraph, volumetricFogTexture, downsampledDepth);
			var upsampledTexture = RenderUpsamplePass(renderGraph, volumetricFogTexture, downsampledDepth,
				cameraColorTexture, cameraDepthTexture);

			// TODO: Optimize one blit in Unity 6
			// Composite the result to camera color
			RenderCompositePass(renderGraph, upsampledTexture, cameraColorTexture);
		}

		/// <summary>
		/// Renders the downsample depth pass.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="cameraDepthTexture"></param>
		/// <returns></returns>
		private TextureHandle RenderDownsampleDepthPass(RenderGraph renderGraph, TextureHandle cameraDepthTexture)
		{
			using (var builder = renderGraph.AddRasterRenderPass<DownsampleDepthPassData>(
				"Volumetric Fog Downsample Depth", out var passData, _downsampleDepthProfilingSampler))
			{
				// Create downsampled depth texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2, false, false)
				{
					colorFormat = GraphicsFormat.R32_SFloat,
					name = DownsampledCameraDepthRTName
				};
				var downsampledDepth = renderGraph.CreateTexture(desc);

				passData.DownsampleDepthMaterial = _downsampleDepthMaterial.Value;
				passData.PassIndex = _downsampleDepthPassIndex;

				builder.UseTextureFragment(downsampledDepth, 0);
				builder.UseTexture(cameraDepthTexture);
				builder.AllowPassCulling(false);

				builder.SetRenderFunc((DownsampleDepthPassData data, RasterGraphContext context) =>
				{
					Blitter.BlitTexture(context.cmd, Vector2.one, data.DownsampleDepthMaterial, data.PassIndex);
				});

				return downsampledDepth;
			}
		}

		/// <summary>
		/// Renders the raymarch pass (chooses between compute and fragment shader).
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="downsampledDepth"></param>
		/// <param name="exposureTexture"></param>
		/// <param name="mainShadowsTexture"></param>
		/// <param name="additionalShadowsTexture"></param>
		/// <param name="lightData"></param>
		/// <returns></returns>
		private TextureHandle RenderRaymarchPass(RenderGraph renderGraph, TextureHandle downsampledDepth,
			TextureHandle exposureTexture, TextureHandle mainShadowsTexture, TextureHandle additionalShadowsTexture,
			LightData lightData)
		{
			if (_raymarchInCS)
				return RenderRaymarchComputePass(renderGraph, downsampledDepth, exposureTexture,
					mainShadowsTexture, additionalShadowsTexture, lightData);
			return RenderRaymarchFragmentPass(renderGraph, downsampledDepth, mainShadowsTexture,
				additionalShadowsTexture, lightData);
		}

		/// <summary>
		/// Renders the raymarch pass using compute shader.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="downsampledDepth"></param>
		/// <param name="exposureTexture"></param>
		/// <param name="mainShadowsTexture"></param>
		/// <param name="additionalShadowsTexture"></param>
		/// <param name="lightData"></param>
		/// <returns></returns>
		private TextureHandle RenderRaymarchComputePass(RenderGraph renderGraph, TextureHandle downsampledDepth,
			TextureHandle exposureTexture, TextureHandle mainShadowsTexture, TextureHandle additionalShadowsTexture,
			LightData lightData)
		{
			using (var builder = renderGraph.AddComputePass<RaymarchPassData>(
				"Volumetric Fog Raymarch (CS)", out var passData, _raymarchSampler))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2, false, false)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					enableRandomWrite = true,
					name = VolumetricFogRenderRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.UseComputeShader = true;
				passData.RaymarchCS = _volumetricFogRaymarchCS;
				passData.RaymarchKernel = _volumetricFogRaymarchKernel;
				passData.Width = _rtWidth / 2;
				passData.Height = _rtHeight / 2;
				passData.ViewCount = IllusionRendererData.MaxViewCount;
				passData.DownsampledDepthTexture = builder.UseTexture(downsampledDepth);
				passData.ExposureTexture = builder.UseTexture(exposureTexture);
				passData.OutputTexture = builder.UseTexture(outputTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
				passData.LightData = lightData;
				passData.VolumetricLightManager = _volumetricLightManager;
				passData.VolumeSettings = VolumeManager.instance.stack.GetComponent<VolumetricFog>();
				passData.RendererData = _rendererData;

				if (mainShadowsTexture.IsValid())
					builder.UseTexture(mainShadowsTexture);
				if (additionalShadowsTexture.IsValid())
					builder.UseTexture(additionalShadowsTexture);

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc((RaymarchPassData data, ComputeGraphContext context) =>
				{
					UpdateVolumetricFogComputeShaderParameters(context.cmd.GetNativeCommandBuffer(),
						data.RaymarchCS, data.LightData.mainLightIndex, data.LightData.additionalLightsCount,
						data.LightData.visibleLights);

					context.cmd.SetComputeTextureParam(data.RaymarchCS, data.RaymarchKernel,
						ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);
					context.cmd.SetComputeTextureParam(data.RaymarchCS, data.RaymarchKernel,
						IllusionShaderProperties._ExposureTexture, data.ExposureTexture);
					context.cmd.SetComputeTextureParam(data.RaymarchCS, data.RaymarchKernel,
						ShaderIDs._VolumetricFogOutput, data.OutputTexture);

					int groupsX = IllusionRenderingUtils.DivRoundUp(data.Width, 8);
					int groupsY = IllusionRenderingUtils.DivRoundUp(data.Height, 8);
					context.cmd.DispatchCompute(data.RaymarchCS, data.RaymarchKernel, groupsX, groupsY, data.ViewCount);
				});

				return outputTexture;
			}
		}

		/// <summary>
		/// Renders the raymarch pass using fragment shader.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="downsampledDepth"></param>
		/// <param name="mainShadowsTexture"></param>
		/// <param name="additionalShadowsTexture"></param>
		/// <param name="lightData"></param>
		/// <returns></returns>
		private TextureHandle RenderRaymarchFragmentPass(RenderGraph renderGraph, TextureHandle downsampledDepth,
			TextureHandle mainShadowsTexture, TextureHandle additionalShadowsTexture, LightData lightData)
		{
			using (var builder = renderGraph.AddRasterRenderPass<RaymarchPassData>(
				"Volumetric Fog Raymarch (FS)", out var passData, _raymarchSampler))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2, false, false)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = VolumetricFogRenderRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.UseComputeShader = false;
				passData.VolumetricFogMaterial = _volumetricFogMaterial.Value;
				passData.PassIndex = _volumetricFogRenderPassIndex;
				passData.DownsampledDepthTexture = builder.UseTexture(downsampledDepth);
				passData.OutputTexture = builder.UseTextureFragment(outputTexture, 0);
				passData.LightData = lightData;
				passData.VolumetricLightManager = _volumetricLightManager;

				if (mainShadowsTexture.IsValid())
					builder.UseTexture(mainShadowsTexture);
				if (additionalShadowsTexture.IsValid())
					builder.UseTexture(additionalShadowsTexture);

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc((RaymarchPassData data, RasterGraphContext context) =>
				{
					UpdateVolumetricFogMaterialParameters(data.VolumetricFogMaterial,
						data.LightData.mainLightIndex, data.LightData.additionalLightsCount, data.LightData.visibleLights);

					data.VolumetricFogMaterial.SetTexture(ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);

					Blitter.BlitTexture(context.cmd, Vector2.one, data.VolumetricFogMaterial, data.PassIndex);
				});

				return outputTexture;
			}
		}

		/// <summary>
		/// Renders the blur pass (chooses between compute and fragment shader).
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="volumetricFogTexture"></param>
		/// <param name="downsampledDepth"></param>
		/// <returns></returns>
		private TextureHandle RenderBlurPass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth)
		{
			if (_blurInCS)
				return RenderBlurComputePass(renderGraph, volumetricFogTexture, downsampledDepth);
			return RenderBlurFragmentPass(renderGraph, volumetricFogTexture);
		}

		/// <summary>
		/// Renders the blur pass using compute shader.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="volumetricFogTexture"></param>
		/// <param name="downsampledDepth"></param>
		/// <returns></returns>
		private TextureHandle RenderBlurComputePass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth)
		{
			using (var builder = renderGraph.AddComputePass<BlurPassData>(
				"Volumetric Fog Blur (CS)", out var passData, _blurSampler))
			{
				// Create temp blur texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2, false, false)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					enableRandomWrite = true,
					name = VolumetricFogBlurRTName
				};
				var tempBlurTexture = renderGraph.CreateTexture(desc);

				var fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFog>();
				passData.UseComputeShader = true;
				passData.BlurCS = _volumetricFogBlurCS;
				passData.BlurKernel = _volumetricFogBlurKernel;
				passData.BlurIterations = fogVolume.blurIterations.value;
				passData.Width = _rtWidth / 2;
				passData.Height = _rtHeight / 2;
				passData.InputTexture = builder.UseTexture(volumetricFogTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
				passData.TempBlurTexture = builder.UseTexture(tempBlurTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
				passData.DownsampledDepthTexture = builder.UseTexture(downsampledDepth);

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc((BlurPassData data, ComputeGraphContext context) =>
				{
					// Ping-pong blur between two textures
					int halfWidth = data.Width;
					int halfHeight = data.Height;
					Vector2 texelSize = new Vector2(1.0f / halfWidth, 1.0f / halfHeight);

					context.cmd.SetComputeVectorParam(data.BlurCS, ShaderIDs._BlurInputTexelSizeId, texelSize);

					int groupsX = IllusionRenderingUtils.DivRoundUp(halfWidth, 8);
					int groupsY = IllusionRenderingUtils.DivRoundUp(halfHeight, 8);

					for (int i = 0; i < data.BlurIterations; ++i)
					{
						context.cmd.SetComputeTextureParam(data.BlurCS, data.BlurKernel,
							ShaderIDs._BlurInputTextureId, data.InputTexture);
						context.cmd.SetComputeTextureParam(data.BlurCS, data.BlurKernel,
							ShaderIDs._BlurOutputTextureId, data.TempBlurTexture);
						context.cmd.SetComputeTextureParam(data.BlurCS, data.BlurKernel,
							ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);
						context.cmd.DispatchCompute(data.BlurCS, data.BlurKernel, groupsX, groupsY, 1);

						// Copy back for next iteration
						if (i < data.BlurIterations - 1)
						{
							context.cmd.GetNativeCommandBuffer().CopyTexture(data.TempBlurTexture, data.InputTexture);
						}
					}
				});

				// Return the blurred result (last iteration was written to TempBlurTexture)
				return passData.TempBlurTexture;
			}
		}

		/// <summary>
		/// Renders the blur pass using fragment shader.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="volumetricFogTexture"></param>
		/// <returns></returns>
		private TextureHandle RenderBlurFragmentPass(RenderGraph renderGraph, TextureHandle volumetricFogTexture)
		{
			using (var builder = renderGraph.AddRasterRenderPass<BlurPassData>(
				"Volumetric Fog Blur (FS)", out var passData, _blurSampler))
			{
				// Create temp blur texture
				var desc = new TextureDesc(_rtWidth / 2, _rtHeight / 2, false, false)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = VolumetricFogBlurRTName
				};
				var tempBlurTexture = renderGraph.CreateTexture(desc);

				var fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFog>();
				passData.UseComputeShader = false;
				passData.VolumetricFogMaterial = _volumetricFogMaterial.Value;
				passData.HorizontalBlurPassIndex = _volumetricFogHorizontalBlurPassIndex;
				passData.VerticalBlurPassIndex = _volumetricFogVerticalBlurPassIndex;
				passData.BlurIterations = fogVolume.blurIterations.value;
				passData.InputTexture = builder.UseTextureFragment(volumetricFogTexture, 0, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
				passData.TempBlurTexture = builder.UseTextureFragment(tempBlurTexture, 0);
				
				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc((BlurPassData data, RasterGraphContext context) =>
				{
					for (int i = 0; i < data.BlurIterations; ++i)
					{
						// Horizontal blur
						Blitter.BlitTexture(context.cmd, data.InputTexture, Vector2.one, data.VolumetricFogMaterial, data.HorizontalBlurPassIndex);
						
						// Vertical blur
						Blitter.BlitTexture(context.cmd, data.TempBlurTexture, Vector2.one, data.VolumetricFogMaterial, data.VerticalBlurPassIndex);
					}
				});

				return passData.InputTexture;
			}
		}

		/// <summary>
		/// Renders the upsample pass (chooses between compute and fragment shader).
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="volumetricFogTexture"></param>
		/// <param name="downsampledDepth"></param>
		/// <param name="cameraColorTexture"></param>
		/// <param name="cameraDepthTexture"></param>
		/// <returns></returns>
		private TextureHandle RenderUpsamplePass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth, TextureHandle cameraColorTexture, TextureHandle cameraDepthTexture)
		{
			if (_upsampleInCS)
				return RenderUpsampleComputePass(renderGraph, volumetricFogTexture, downsampledDepth,
					cameraColorTexture, cameraDepthTexture);
			return RenderUpsampleFragmentPass(renderGraph, volumetricFogTexture, downsampledDepth,
				cameraColorTexture, cameraDepthTexture);
		}

		/// <summary>
		/// Renders the upsample pass using compute shader.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="volumetricFogTexture"></param>
		/// <param name="downsampledDepth"></param>
		/// <param name="cameraColorTexture"></param>
		/// <param name="cameraDepthTexture"></param>
		/// <returns></returns>
		private TextureHandle RenderUpsampleComputePass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth, TextureHandle cameraColorTexture, TextureHandle cameraDepthTexture)
		{
			using (var builder = renderGraph.AddComputePass<UpsamplePassData>(
				"Volumetric Fog Upsample (CS)", out var passData, _upsampleSampler))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth, _rtHeight, false, false)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					enableRandomWrite = true,
					name = VolumetricFogUpsampleCompositionRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.UseComputeShader = true;
				passData.UpsampleCS = _bilateralUpsampleCS;
				passData.UpsampleKernel = _bilateralUpSampleColorKernel;
				passData.UpsampleVariables = _shaderVariablesBilateralUpsampleCB;
				passData.Width = _rtWidth;
				passData.Height = _rtHeight;
				passData.ViewCount = IllusionRendererData.MaxViewCount;
				passData.VolumetricFogTexture = builder.UseTexture(volumetricFogTexture);
				passData.CameraColorTexture = builder.UseTexture(cameraColorTexture);
				passData.DownsampledDepthTexture = builder.UseTexture(downsampledDepth);
				passData.CameraDepthTexture = builder.UseTexture(cameraDepthTexture);
				passData.OutputTexture = builder.UseTexture(outputTexture, IBaseRenderGraphBuilder.AccessFlags.Write);

				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc((UpsamplePassData data, ComputeGraphContext context) =>
				{
					ComputeConstantBuffer.Push(context.cmd, data.UpsampleVariables, data.UpsampleCS, ShaderIDs.ShaderVariablesBilateralUpsample);

					// Inject all the input buffers
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._LowResolutionTexture, data.VolumetricFogTexture);
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._CameraColorTexture, data.CameraColorTexture);
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._DownsampledCameraDepthTexture, data.DownsampledDepthTexture);

					// Inject the output textures
					context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderIDs._OutputUpscaledTexture, data.OutputTexture);

					// Upscale the buffer to full resolution
					int groupsX = IllusionRenderingUtils.DivRoundUp(data.Width, 8);
					int groupsY = IllusionRenderingUtils.DivRoundUp(data.Height, 8);
					context.cmd.DispatchCompute(data.UpsampleCS, data.UpsampleKernel, groupsX, groupsY, data.ViewCount);
				});

				return outputTexture;
			}
		}

		/// <summary>
		/// Renders the upsample pass using fragment shader.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="volumetricFogTexture"></param>
		/// <param name="downsampledDepth"></param>
		/// <param name="cameraColorTexture"></param>
		/// <param name="cameraDepthTexture"></param>
		/// <returns></returns>
		private TextureHandle RenderUpsampleFragmentPass(RenderGraph renderGraph, TextureHandle volumetricFogTexture,
			TextureHandle downsampledDepth, TextureHandle cameraColorTexture, TextureHandle cameraDepthTexture)
		{
			using (var builder = renderGraph.AddRasterRenderPass<UpsamplePassData>(
				"Volumetric Fog Upsample (FS)", out var passData, _upsampleSampler))
			{
				// Create output texture
				var desc = new TextureDesc(_rtWidth, _rtHeight, false, false)
				{
					colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
					name = VolumetricFogUpsampleCompositionRTName
				};
				var outputTexture = renderGraph.CreateTexture(desc);

				passData.UseComputeShader = false;
				passData.VolumetricFogMaterial = _volumetricFogMaterial.Value;
				passData.PassIndex = _volumetricFogDepthAwareUpsampleCompositionPassIndex;
				passData.VolumetricFogTexture = builder.UseTexture(volumetricFogTexture);
				passData.CameraColorTexture = builder.UseTexture(cameraColorTexture);
				passData.DownsampledDepthTexture = builder.UseTexture(downsampledDepth);
				passData.CameraDepthTexture = builder.UseTexture(cameraDepthTexture);
				passData.OutputTexture = builder.UseTextureFragment(outputTexture, 0);
				
				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc((UpsamplePassData data, RasterGraphContext context) =>
				{
					data.VolumetricFogMaterial.SetTexture(ShaderIDs._VolumetricFogTexture, data.VolumetricFogTexture);
					
					Blitter.BlitTexture(context.cmd, data.CameraColorTexture, Vector2.one, data.VolumetricFogMaterial, data.PassIndex);
				});

				return outputTexture;
			}
		}

		/// <summary>
		/// Renders the composite pass to blit the result to camera color.
		/// </summary>
		/// <param name="renderGraph"></param>
		/// <param name="sourceTexture"></param>
		/// <param name="cameraColorTexture"></param>
		private void RenderCompositePass(RenderGraph renderGraph, TextureHandle sourceTexture, TextureHandle cameraColorTexture)
		{
			using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
				"Volumetric Fog Composite", out var passData, _compositeSampler))
			{
				passData.SourceTexture = builder.UseTexture(sourceTexture);

				builder.UseTextureFragment(cameraColorTexture, 0);
				builder.AllowPassCulling(false);

				builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) =>
				{
					Blitter.BlitTexture(context.cmd, data.SourceTexture, Vector2.one, Blitter.GetBlitMaterial(TextureDimension.Tex2D), 0);
				});
			}
		}
#endif

		/// <summary>
		/// Disposes the resources used by this pass.
		/// </summary>
		public void Dispose()
		{
			_downsampleDepthMaterial.DestroyCache();
			_volumetricFogMaterial.DestroyCache();
			_downsampledCameraDepthRTHandle?.Release();
			_volumetricFogRenderRTHandle?.Release();
			_volumetricFogBlurRTHandle?.Release();
			_volumetricFogUpsampleCompositionRTHandle?.Release();
		}

		private static class ShaderIDs
		{
			public static readonly int _CameraColorTexture = MemberNameHelpers.ShaderPropertyID();

			public static readonly int _LowResolutionTexture = MemberNameHelpers.ShaderPropertyID();

			public static readonly int _OutputUpscaledTexture = MemberNameHelpers.ShaderPropertyID();

			public static readonly int ShaderVariablesBilateralUpsample = MemberNameHelpers.ShaderPropertyID();

			public static readonly int _DownsampledCameraDepthTexture = MemberNameHelpers.ShaderPropertyID();

			public static readonly int _VolumetricFogTexture = MemberNameHelpers.ShaderPropertyID();

			public static readonly int _VolumetricFogOutput = MemberNameHelpers.ShaderPropertyID();

			public static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");

			public static readonly int CustomAdditionalLightsCountId = Shader.PropertyToID("_CustomAdditionalLightsCount");

			public static readonly int DistanceId = Shader.PropertyToID("_Distance");

			public static readonly int BaseHeightId = Shader.PropertyToID("_BaseHeight");

			public static readonly int MaximumHeightId = Shader.PropertyToID("_MaximumHeight");

			public static readonly int GroundHeightId = Shader.PropertyToID("_GroundHeight");

			public static readonly int DensityId = Shader.PropertyToID("_Density");

			public static readonly int AbsortionId = Shader.PropertyToID("_Absortion");
			
			public static readonly int ProbeVolumeContributionWeigthId = Shader.PropertyToID("_ProbeVolumeContributionWeight");

			public static readonly int TintId = Shader.PropertyToID("_Tint");

			public static readonly int MaxStepsId = Shader.PropertyToID("_MaxSteps");

			public static readonly int TransmittanceThresholdId = Shader.PropertyToID("_TransmittanceThreshold");

			public static readonly int AnisotropiesArrayId = Shader.PropertyToID("_Anisotropies");

			public static readonly int ScatteringsArrayId = Shader.PropertyToID("_Scatterings");

			public static readonly int RadiiSqArrayId = Shader.PropertyToID("_RadiiSq");

			// Blur compute shader properties
			public static readonly int _BlurInputTextureId = Shader.PropertyToID("_BlurInputTexture");
			
			public static readonly int _BlurOutputTextureId = Shader.PropertyToID("_BlurOutputTexture");
			
			public static readonly int _BlurInputTexelSizeId = Shader.PropertyToID("_BlurInputTexelSize");
		}
	}
}