#if CHRIS_INSTALL
using Chris.Configs;
#endif

namespace Illusion.Rendering
{
    /// <summary>
    /// Runtime rendering config allow you to toggle illusion rendering features above renderer feature settings.
    /// </summary>
#if CHRIS_INSTALL
    [ConfigPath("Graphics.IllusionRP")] // Not mark serializable as a runtime only config.
    public class IllusionRuntimeRenderingConfig : Config<IllusionRuntimeRenderingConfig>
#else
    public class IllusionRuntimeRenderingConfig
#endif
    {
        /// <summary>
        /// Whether enable Screen Space Reflection.
        /// </summary>
        [ConfigVariable("r.ssr")]
        public bool EnableScreenSpaceReflection { get; set; } = true;
        
        /// <summary>
        /// Whether enable Screen Space Reflection.
        /// </summary>
        [ConfigVariable("r.ssgi")]
        public bool EnableScreenSpaceGlobalIllumination { get; set; } = true;

        /// <summary>
        /// Whether enable Contact Shadows.
        /// </summary>
        [ConfigVariable("r.contactshadows")]
        public bool EnableContactShadows { get; set; } = true;
        
        /// <summary>
        /// Whether enable Percentage Closer Soft Shadows.
        /// </summary>
        [ConfigVariable("r.pcss")]
        public bool EnablePercentageCloserSoftShadows { get; set; } = true;

        /// <summary>
        /// Whether enable Screen Space Ambient Occlusion.
        /// </summary>
        [ConfigVariable("r.ssao")]
        public bool EnableScreenSpaceAmbientOcclusion { get; set; } = true;

        /// <summary>
        /// Whether enable Volumetric Fog.
        /// </summary>
        [ConfigVariable("r.volumetricfog")]
        public bool EnableVolumetricFog { get; set; } = true;

        /// <summary>
        /// Whether enable PRT GI.
        /// </summary>
        [ConfigVariable("r.prt")]
        public bool EnablePrecomputedRadianceTransferGlobalIllumination { get; set; } = true;
        
        /// <summary>
        /// Whether enable convolution bloom
        /// </summary>
        [ConfigVariable("r.bloom")]
        public bool EnableConvolutionBloom { get; set; } = true;

        /// <summary>
        /// Whether enable Async Compute.
        /// </summary>
        [ConfigVariable("r.asynccompute")]
#if UNITY_2023_1_OR_NEWER
        // TODO: Has wrong graphics fence dependency due to NativePassCompiler bug.
        public bool EnableAsyncCompute { get; set; } = false;
#else
        public bool EnableAsyncCompute { get; set; } = true;
#endif
        
        /// <summary>
        /// Whether enable Native Render Pass.
        /// </summary>
        [ConfigVariable("r.renderpass")]
        public bool EnableNativeRenderPass { get; set; } = true;
        
        /// <summary>
        /// Whether enable Compute Shader.
        /// </summary>
        [ConfigVariable("r.computeshader")]
        public bool EnableComputeShader { get; set; } = true;

        // =================================== Debug ========================================= //
        [ConfigVariable("r.debug.velocity", IsEditor = true)]
        public bool EnableMotionVectorsDebug { get; set; }
        
        [ConfigVariable("r.debug.ssr", IsEditor = true)]
        public bool EnableScreenSpaceReflectionDebug { get; set; }
        
        /// <summary>
        /// Exposure debug mode.
        /// </summary>
        [ConfigVariable("r.debug.exposure", IsEditor = true)]
        public ExposureDebugMode ExposureDebugMode { get; set; } = ExposureDebugMode.None;
        
        /// <summary>
        /// Screen space shadow debug mode.
        /// </summary>
        [ConfigVariable("r.debug.ssshadow", IsEditor = true)]
        public ScreenSpaceShadowDebugMode ScreenSpaceShadowDebugMode { get; set; } = ScreenSpaceShadowDebugMode.None;
        
        /// <summary>
        /// Enable Per Object Shadow debug mode.
        /// </summary>
        [ConfigVariable("r.debug.perobjectshadow", IsEditor = true)]
        public bool EnablePerObjectShadowDebug { get; set; }

        /// <summary>
        /// Whether to center the histogram debug view around the middle-grey point or not.
        /// </summary>
        [ConfigVariable("r.CenterHistogramAroundMiddleGrey", IsEditor = true)]
        public bool CenterHistogramAroundMiddleGrey { get; set; }

        /// <summary>
        /// Whether to show the on scene overlay displaying pixels excluded by the exposure computation via histogram.
        /// </summary>
        [ConfigVariable("r.DisplayOnSceneOverlay", IsEditor = true)]
        public bool DisplayOnSceneOverlay { get; set; } = true;
        
        /// <summary>
        /// Whether to display histogram debug view in rgb mode.
        /// </summary>
        [ConfigVariable("r.DisplayFinalImageHistogramAsRGB", IsEditor = true)]
        public bool DisplayFinalImageHistogramAsRGB { get; set; }

        /// <summary>
        /// Whether to show only the mask in the picture in picture. If unchecked, the mask view is weighted by the scene color.
        /// </summary>
        [ConfigVariable("r.DisplayMaskOnly", IsEditor = true)]
        public bool DisplayMaskOnly { get; set; }
        // =================================== Debug ========================================= //

#if !CHRIS_INSTALL
        private static IllusionRuntimeRenderingConfig _instance;

        public static IllusionRuntimeRenderingConfig Get()
        {
            return _instance ??= new IllusionRuntimeRenderingConfig();
        }

        private sealed class ConfigVariableAttribute : System.Attribute
        {
            public string Name { get; private set; }
        
            public bool IsEditor { get; set; }

            public ConfigVariableAttribute(string name)
            {
                Name = name;
            }
        }
#endif
    }
}