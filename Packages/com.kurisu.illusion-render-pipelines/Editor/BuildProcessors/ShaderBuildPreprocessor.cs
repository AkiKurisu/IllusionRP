using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PrefilterMode = Illusion.Rendering.IllusionRendererFeature.PrefilterMode;

namespace Illusion.Rendering.Editor
{
    [Flags]
    internal enum ShaderFeatures : long
    {
        None = 0,
        ScreenSpaceReflection = 1L << 0,
        ScreenSpaceGlobalIllumination = 1L << 1,
        ScreenSpaceOcclusion = 1L << 2,
        MainLightShadowsScreen = 1L << 3,
        PrecomputedRadianceTransferGI = 1L << 4,
        ScreenSpaceSubsurfaceScattering = 1L << 5,
        OrderIndependentTransparency = 1L << 6,
        TransparentPerObjectShadow = 1L << 7,
        FragmentShadowBias = 1L << 8,
        ContactShadows = 1L << 9,
        PercentageCloserSoftShadows = 1L << 10,
        TransparentDepthPostPass = 1L << 11,
        TransparentOverdraw = 1L << 12,
        TransparentScreenSpaceReflection = 1L << 13,
        AreaLights = 1L << 14,
        AreaShadowMedium = 1L << 15,
        AreaShadowHigh = 1L << 16,
        All = ~0
    }

    internal sealed class IllusionShaderBuildData
    {
        private readonly ShaderFeatures[] _rendererFeatures;

        internal IllusionShaderBuildData(
            BuildTarget target,
            bool isValid,
            bool stripUnusedVariants,
            ShaderFeatures[] rendererFeatures)
        {
            Target = target;
            IsValid = isValid;
            StripUnusedVariants = stripUnusedVariants;
            _rendererFeatures = rendererFeatures ?? Array.Empty<ShaderFeatures>();
        }

        internal BuildTarget Target { get; }

        internal bool IsValid { get; }

        internal bool StripUnusedVariants { get; }

        internal IReadOnlyList<ShaderFeatures> RendererFeatures => _rendererFeatures;

        internal bool AnyRendererSupports(ShaderFeatures required, bool requireAll = false)
        {
            for (int i = 0; i < _rendererFeatures.Length; i++)
            {
                ShaderFeatures available = _rendererFeatures[i];
                if (requireAll ? (available & required) == required : (available & required) != 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Updates IllusionRP keyword prefiltering before Unity expands shader variants.
    /// </summary>
    internal sealed class UpdateShaderPrefilteringDataBeforeBuild : IPreprocessShaders
    {
        public int callbackOrder => -99; // After URP

        public UpdateShaderPrefilteringDataBeforeBuild()
        {
            ShaderBuildPreprocessor.Gather(EditorUserBuildSettings.activeBuildTarget);
        }

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> compilerDataList)
        {
        }
    }

    internal sealed class IllusionShaderBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -99;

        public void OnPreprocessBuild(BuildReport report)
        {
            ShaderBuildPreprocessor.Gather(report.summary.platform);
        }
    }

    internal static class ShaderBuildPreprocessor
    {
        private static IllusionShaderBuildData s_CurrentData;

        internal static IllusionShaderBuildData CurrentData
        {
            get
            {
                BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
                if (s_CurrentData == null || s_CurrentData.Target != target)
                    Gather(target);
                return s_CurrentData;
            }
        }

        internal static void Gather(BuildTarget target)
        {
            try
            {
                GatherCore(target);
            }
            catch (Exception)
            {
                s_CurrentData = new IllusionShaderBuildData(
                    target,
                    isValid: false,
                    stripUnusedVariants: false,
                    Array.Empty<ShaderFeatures>());
            }
        }

        private static void GatherCore(BuildTarget target)
        {
            var rendererFeatures = new List<ShaderFeatures>();
            var featureAssets = new HashSet<IllusionRendererFeature>();
            bool valid = true;

            using (UnityEngine.Pool.ListPool<UniversalRenderPipelineAsset>.Get(out var assets))
            {
                if (!target.TryGetRenderPipelineAssets(assets) || assets.Count == 0)
                {
                    valid = false;
                }
                else
                {
                    for (int assetIndex = 0; assetIndex < assets.Count; assetIndex++)
                    {
                        UniversalRenderPipelineAsset asset = assets[assetIndex];
                        if (!asset || asset.m_RendererDataList == null)
                        {
                            valid = false;
                            continue;
                        }

                        foreach (ScriptableRendererData rendererData in asset.m_RendererDataList)
                        {
                            if (!rendererData)
                            {
                                valid = false;
                                continue;
                            }

                            IllusionRendererFeature illusionFeature = null;
                            int featureCount = 0;
                            if (rendererData.rendererFeatures == null)
                            {
                                valid = false;
                                rendererFeatures.Add(ShaderFeatures.None);
                                continue;
                            }

                            foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
                            {
                                if (feature is not IllusionRendererFeature candidate)
                                    continue;
                                illusionFeature = candidate;
                                featureCount++;
                                featureAssets.Add(candidate);
                            }

                            if (featureCount > 1)
                            {
                                valid = false;
                                rendererFeatures.Add(ShaderFeatures.None);
                                continue;
                            }

                            rendererFeatures.Add(
                                illusionFeature && illusionFeature.isActive
                                    ? GetFeatures(illusionFeature)
                                    : ShaderFeatures.None);
                        }
                    }
                }
            }

            if (rendererFeatures.Count == 0)
                valid = false;

            IllusionRenderPipelineSettings settings = IllusionRenderPipelineSettings.instance;
            bool stripUnusedVariants = valid && settings.stripUnusedVariants;

            if (valid)
            {
                ApplyPrefilterModes(
                    featureAssets,
                    rendererFeatures,
                    stripUnusedVariants);
            }

            s_CurrentData = new IllusionShaderBuildData(
                target,
                valid,
                stripUnusedVariants,
                rendererFeatures.ToArray());

        }

        private static ShaderFeatures GetFeatures(IllusionRendererFeature feature)
        {
            ShaderFeatures features = ShaderFeatures.MainLightShadowsScreen;
            if (feature.screenSpaceReflection)
                features |= ShaderFeatures.ScreenSpaceReflection;
            if (feature.screenSpaceGlobalIllumination)
                features |= ShaderFeatures.ScreenSpaceGlobalIllumination;
            if (feature.groundTruthAO)
                features |= ShaderFeatures.ScreenSpaceOcclusion;
            if (feature.precomputedRadianceTransferGI)
                features |= ShaderFeatures.PrecomputedRadianceTransferGI;
            if (feature.subsurfaceScattering)
                features |= ShaderFeatures.ScreenSpaceSubsurfaceScattering;
            if (feature.orderIndependentTransparency)
                features |= ShaderFeatures.OrderIndependentTransparency;
            if (feature.transparentReceivePerObjectShadows)
                features |= ShaderFeatures.TransparentPerObjectShadow;
            if (feature.fragmentShadowBias)
                features |= ShaderFeatures.FragmentShadowBias;
            if (feature.contactShadows)
                features |= ShaderFeatures.ContactShadows;
            if (feature.pcssShadows)
                features |= ShaderFeatures.PercentageCloserSoftShadows;
            if (feature.transparentDepthPostPass)
                features |= ShaderFeatures.TransparentDepthPostPass;
            if (feature.orderIndependentTransparency && feature.oitTransparentOverdrawPass)
                features |= ShaderFeatures.TransparentOverdraw;
            if (feature.screenSpaceReflection && feature.transparentScreenSpaceReflection)
                features |= ShaderFeatures.TransparentScreenSpaceReflection;
            if (feature.areaLights)
            {
                features |= ShaderFeatures.AreaLights;
                features |= feature.areaShadowFilteringQuality == AreaLights.HDAreaShadowFilteringQuality.High
                    ? ShaderFeatures.AreaShadowHigh
                    : ShaderFeatures.AreaShadowMedium;
            }
            return features;
        }

        private static void ApplyPrefilterModes(
            IEnumerable<IllusionRendererFeature> featureAssets,
            IReadOnlyList<ShaderFeatures> rendererFeatures,
            bool enabled)
        {
            PrefilterMode safe = PrefilterMode.Select;
            PrefilterMode subsurface = enabled
                ? AggregateMode(rendererFeatures, ShaderFeatures.ScreenSpaceSubsurfaceScattering)
                : safe;
            PrefilterMode contact = enabled
                ? RuntimeToggleMode(rendererFeatures, ShaderFeatures.ContactShadows)
                : safe;
            PrefilterMode pcss = enabled
                ? RuntimeToggleMode(rendererFeatures, ShaderFeatures.PercentageCloserSoftShadows)
                : safe;
            PrefilterMode prt = enabled
                ? AggregateMode(rendererFeatures, ShaderFeatures.PrecomputedRadianceTransferGI)
                : safe;
            PrefilterMode transparentShadow = enabled
                ? AggregateMode(rendererFeatures, ShaderFeatures.TransparentPerObjectShadow)
                : safe;
            PrefilterMode ssr = enabled
                ? AggregateMode(rendererFeatures, ShaderFeatures.ScreenSpaceReflection)
                : safe;
            PrefilterMode ssgi = enabled
                ? AggregateMode(rendererFeatures, ShaderFeatures.ScreenSpaceGlobalIllumination)
                : safe;
            PrefilterMode fragmentBias = enabled
                ? AggregateMode(rendererFeatures, ShaderFeatures.FragmentShadowBias)
                : safe;
            IllusionRendererFeature.AreaShadowPrefilterMode areaShadow = enabled
                ? AreaShadowMode(rendererFeatures)
                : IllusionRendererFeature.AreaShadowPrefilterMode.All;

            foreach (IllusionRendererFeature feature in featureAssets)
            {
                if (!feature)
                    continue;

                bool changed = false;
                changed |= SetMode(ref feature.screenSpaceSubsurfaceScatteringPrefilterMode, subsurface);
                changed |= SetMode(ref feature.contactShadowPrefilterMode, contact);
                changed |= SetMode(ref feature.percentageCloserSoftShadowsPrefilterMode, pcss);
                changed |= SetMode(ref feature.precomputedRadianceTransferGIPrefilterMode, prt);
                changed |= SetMode(ref feature.transparentPerObjectShadowsPrefilterMode, transparentShadow);
                changed |= SetMode(ref feature.screenSpaceReflectionPrefilterMode, ssr);
                changed |= SetMode(ref feature.screenSpaceGlobalIlluminationPrefilterMode, ssgi);
                changed |= SetMode(ref feature.fragmentShadowBiasPrefilterMode, fragmentBias);
                if (feature.areaShadowPrefilterMode != areaShadow)
                {
                    feature.areaShadowPrefilterMode = areaShadow;
                    changed = true;
                }

                if (!changed)
                    continue;
                EditorUtility.SetDirty(feature);
                AssetDatabase.SaveAssetIfDirty(feature);
            }
        }

        private static PrefilterMode AggregateMode(
            IReadOnlyList<ShaderFeatures> rendererFeatures,
            ShaderFeatures feature)
        {
            bool any = false;
            bool all = rendererFeatures.Count > 0;
            for (int i = 0; i < rendererFeatures.Count; i++)
            {
                bool available = (rendererFeatures[i] & feature) != 0;
                any |= available;
                all &= available;
            }

            if (!any)
                return PrefilterMode.Remove;
            return all ? PrefilterMode.SelectOnly : PrefilterMode.Select;
        }

        private static PrefilterMode RuntimeToggleMode(
            IReadOnlyList<ShaderFeatures> rendererFeatures,
            ShaderFeatures feature)
        {
            for (int i = 0; i < rendererFeatures.Count; i++)
            {
                if ((rendererFeatures[i] & feature) != 0)
                    return PrefilterMode.Select;
            }
            return PrefilterMode.Remove;
        }

        // Off is needed by renderers without area lights, each tier by the renderers that selected it.
        internal static IllusionRendererFeature.AreaShadowPrefilterMode AreaShadowMode(
            IReadOnlyList<ShaderFeatures> rendererFeatures)
        {
            bool needOff = false;
            bool needMedium = false;
            bool needHigh = false;
            for (int i = 0; i < rendererFeatures.Count; i++)
            {
                ShaderFeatures features = rendererFeatures[i];
                needOff |= (features & ShaderFeatures.AreaLights) == 0;
                needMedium |= (features & ShaderFeatures.AreaShadowMedium) != 0;
                needHigh |= (features & ShaderFeatures.AreaShadowHigh) != 0;
            }

            if (needMedium && needHigh)
                return needOff
                    ? IllusionRendererFeature.AreaShadowPrefilterMode.All
                    : IllusionRendererFeature.AreaShadowPrefilterMode.MediumAndHigh;
            if (needMedium)
                return needOff
                    ? IllusionRendererFeature.AreaShadowPrefilterMode.OffAndMedium
                    : IllusionRendererFeature.AreaShadowPrefilterMode.MediumOnly;
            if (needHigh)
                return needOff
                    ? IllusionRendererFeature.AreaShadowPrefilterMode.OffAndHigh
                    : IllusionRendererFeature.AreaShadowPrefilterMode.HighOnly;
            return IllusionRendererFeature.AreaShadowPrefilterMode.OffOnly;
        }

        private static bool SetMode(ref PrefilterMode current, PrefilterMode value)
        {
            if (current == value)
                return false;
            current = value;
            return true;
        }

    }
}
