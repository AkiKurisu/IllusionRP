Shader "Hidden/LutBuilderHdr"
{
    HLSLINCLUDE
        #pragma multi_compile_local _ _TONEMAP_ACES _TONEMAP_NEUTRAL
        #pragma multi_compile_local_fragment _ HDR_COLORSPACE_CONVERSION
        #pragma multi_compile_fragment _ _TONEMAP_ACES_FILMIC _TONEMAP_GT
        
        #define _ACTUAL_TONEMAP_ACES    (defined(_TONEMAP_ACES_FILMIC) || defined(_TONEMAP_ACES))
        #define _ACTUAL_TONEMAP_GT      (defined(_TONEMAP_GT) && !defined(_TONEMAP_NEUTRAL) && !_ACTUAL_TONEMAP_ACES)

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ACES.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.kurisu.illusion-render-pipelines/Shaders/PostProcessing/GranTurismoTonemapping.hlsl"
#if defined(HDR_COLORSPACE_CONVERSION)
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/HDROutput.hlsl"
#endif

        float4 _Lut_Params;         // x: lut_height, y: 0.5 / lut_width, z: 0.5 / lut_height, w: lut_height / lut_height - 1
        float4 _ColorBalance;       // xyz: LMS coeffs, w: unused
        float4 _ColorFilter;        // xyz: color, w: unused
        float4 _ChannelMixerRed;    // xyz: rgb coeffs, w: unused
        float4 _ChannelMixerGreen;  // xyz: rgb coeffs, w: unused
        float4 _ChannelMixerBlue;   // xyz: rgb coeffs, w: unused
        float4 _HueSatCon;          // x: hue shift, y: saturation, z: contrast, w: unused
        float4 _Lift;               // xyz: color, w: unused
        float4 _Gamma;              // xyz: color, w: unused
        float4 _Gain;               // xyz: color, w: unused
        float4 _Shadows;            // xyz: color, w: unused
        float4 _Midtones;           // xyz: color, w: unused
        float4 _Highlights;         // xyz: color, w: unused
        float4 _ShaHiLimits;        // xy: shadows min/max, zw: highlight min/max
        float4 _SplitShadows;       // xyz: color, w: balance
        float4 _SplitHighlights;    // xyz: color, w: unused
        float4 _HDROutputLuminanceParams; // xy: brightness min/max, z: paper white brightness, w: 1.0 / paper white
        float4 _HDROutputGradingParams; // x: eetf/range reduction mode, y: hue shift, zw: unused

        float _FilmSlope;            // = 0.91;
        float _FilmToe;              // = 0.53;
        float _FilmShoulder;         // = 0.23;
        float _FilmBlackClip;        // = 0;
        float _FilmWhiteClip;        // = 0.035;

        TEXTURE2D(_CurveMaster);
        TEXTURE2D(_CurveRed);
        TEXTURE2D(_CurveGreen);
        TEXTURE2D(_CurveBlue);

        TEXTURE2D(_CurveHueVsHue);
        TEXTURE2D(_CurveHueVsSat);
        TEXTURE2D(_CurveSatVsSat);
        TEXTURE2D(_CurveLumVsSat);

        #define MinNits                 _HDROutputLuminanceParams.x
        #define MaxNits                 _HDROutputLuminanceParams.y
        #define PaperWhite              _HDROutputLuminanceParams.z
        #define RangeReductionMode      (int)_HDROutputGradingParams.x
        #define HueShift                _HDROutputGradingParams.y

        float EvaluateCurve(TEXTURE2D(curve), float t)
        {
            float x = SAMPLE_TEXTURE2D(curve, sampler_LinearClamp, float2(t, 0.0)).x;
            return saturate(x);
        }

        float3 RotateToColorGradeOutputSpace(float3 gradedColor)
        {
            #if _ACTUAL_TONEMAP_ACES
                // In ACES workflow we return graded color in ACEScg, we move to ACES (AP0) later on
                return gradedColor;
            #elif defined(HDR_COLORSPACE_CONVERSION) // HDR but not ACES workflow
                // If we are doing HDR we expect grading to finish at Rec2020. Any supplemental rotation is done inside the various options.
                return RotateRec709ToRec2020(gradedColor);
            #else // Nor ACES or HDR
                // We already graded in sRGB
                return gradedColor;
            #endif
        }

        half IllusionGetLuminance(half3 colorLinear)
        {
        #if _ACTUAL_TONEMAP_ACES
            return AcesLuminance(colorLinear);
        #else
            return Luminance(colorLinear);
        #endif
        }

        // Note: when the ACES tonemapper is selected the grading steps will be done using ACES spaces
        float3 ColorGrade(float3 colorLutSpace)
        {
            // Switch back to linear
            float3 colorLinear = LogCToLinear(colorLutSpace);

            // White balance in LMS space
            float3 colorLMS = LinearToLMS(colorLinear);
            colorLMS *= _ColorBalance.xyz;
            colorLinear = LMSToLinear(colorLMS);

            // Do contrast in log after white balance
            #if _ACTUAL_TONEMAP_ACES
            float3 colorLog = ACES_to_ACEScc(unity_to_ACES(colorLinear));
            #else
            float3 colorLog = LinearToLogC(colorLinear);
            #endif

            colorLog = (colorLog - ACEScc_MIDGRAY) * _HueSatCon.z + ACEScc_MIDGRAY;

            #if _ACTUAL_TONEMAP_ACES
            colorLinear = ACES_to_ACEScg(ACEScc_to_ACES(colorLog));
            #else
            colorLinear = LogCToLinear(colorLog);
            #endif

            // Color filter is just an unclipped multiplier
            colorLinear *= _ColorFilter.xyz;

            // Do NOT feed negative values to the following color ops
            colorLinear = max(0.0, colorLinear);

            // Split toning
            // As counter-intuitive as it is, to make split-toning work the same way it does in Adobe
            // products we have to do all the maths in gamma-space...
            float balance = _SplitShadows.w;
            float3 colorGamma = PositivePow(colorLinear, 1.0 / 2.2);

            float luma = saturate(IllusionGetLuminance(saturate(colorGamma)) + balance);
            float3 splitShadows = lerp((0.5).xxx, _SplitShadows.xyz, 1.0 - luma);
            float3 splitHighlights = lerp((0.5).xxx, _SplitHighlights.xyz, luma);
            colorGamma = SoftLight(colorGamma, splitShadows);
            colorGamma = SoftLight(colorGamma, splitHighlights);

            colorLinear = PositivePow(colorGamma, 2.2);

            // Channel mixing (Adobe style)
            colorLinear = float3(
                dot(colorLinear, _ChannelMixerRed.xyz),
                dot(colorLinear, _ChannelMixerGreen.xyz),
                dot(colorLinear, _ChannelMixerBlue.xyz)
            );

            // Shadows, midtones, highlights
            luma = IllusionGetLuminance(colorLinear);
            float shadowsFactor = 1.0 - smoothstep(_ShaHiLimits.x, _ShaHiLimits.y, luma);
            float highlightsFactor = smoothstep(_ShaHiLimits.z, _ShaHiLimits.w, luma);
            float midtonesFactor = 1.0 - shadowsFactor - highlightsFactor;
            colorLinear = colorLinear * _Shadows.xyz * shadowsFactor
                        + colorLinear * _Midtones.xyz * midtonesFactor
                        + colorLinear * _Highlights.xyz * highlightsFactor;

            // Lift, gamma, gain
            colorLinear = colorLinear * _Gain.xyz + _Lift.xyz;
            colorLinear = sign(colorLinear) * pow(abs(colorLinear), _Gamma.xyz);

            // HSV operations
            float satMult;
            float3 hsv = RgbToHsv(colorLinear);
            {
                // Hue Vs Sat
                satMult = EvaluateCurve(_CurveHueVsSat, hsv.x) * 2.0;

                // Sat Vs Sat
                satMult *= EvaluateCurve(_CurveSatVsSat, hsv.y) * 2.0;

                // Lum Vs Sat
                satMult *= EvaluateCurve(_CurveLumVsSat, Luminance(colorLinear)) * 2.0;

                // Hue Shift & Hue Vs Hue
                float hue = hsv.x + _HueSatCon.x;
                float offset = EvaluateCurve(_CurveHueVsHue, hue) - 0.5;
                hue += offset;
                hsv.x = RotateHue(hue, 0.0, 1.0);
            }
            colorLinear = HsvToRgb(hsv);

            // Global saturation
            luma = IllusionGetLuminance(colorLinear);
            colorLinear = luma.xxx + (_HueSatCon.yyy * satMult) * (colorLinear - luma.xxx);

            // YRGB curves
            // Conceptually these need to be in range [0;1] and from an artist-workflow perspective
            // it's easier to deal with
            colorLinear = FastTonemap(colorLinear);
            {
                const float kHalfPixel = (1.0 / 128.0) / 2.0;
                float3 c = colorLinear;

                // Y (master)
                c += kHalfPixel.xxx;
                float mr = EvaluateCurve(_CurveMaster, c.r);
                float mg = EvaluateCurve(_CurveMaster, c.g);
                float mb = EvaluateCurve(_CurveMaster, c.b);
                c = float3(mr, mg, mb);

                // RGB
                c += kHalfPixel.xxx;
                float r = EvaluateCurve(_CurveRed, c.r);
                float g = EvaluateCurve(_CurveGreen, c.g);
                float b = EvaluateCurve(_CurveBlue, c.b);
                colorLinear = float3(r, g, b);
            }
            colorLinear = FastTonemapInvert(colorLinear);

            colorLinear = max(0.0, colorLinear);
            return RotateToColorGradeOutputSpace(colorLinear);
        }

        // Ported from UE
        float3 FilmicACESTonemap(float3 aces)
        {
            // "Glow" module constants
            const float RRT_GLOW_GAIN = 0.05;
            const float RRT_GLOW_MID = 0.08;
         
            float saturation = rgb_2_saturation(aces);
            float ycIn = rgb_2_yc(aces);
            float s = sigmoid_shaper((saturation - 0.4) / 0.2);
            float addedGlow = 1.0 + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);
            aces *= addedGlow;
         
            const float RRT_RED_SCALE = 0.82;
            const float RRT_RED_PIVOT = 0.03;
            const float RRT_RED_HUE = 0.0;
            const float RRT_RED_WIDTH = 135.0;
         
            // --- Red modifier --- //
            float hue = rgb_2_hue(aces);
            float centeredHue = center_hue(hue, RRT_RED_HUE);
            float hueWeight;
            {
                hueWeight = smoothstep(0.0, 1.0, 1.0 - abs(2.0 * centeredHue / RRT_RED_WIDTH));
                hueWeight *= hueWeight;
            }
            //float hueWeight = Square( smoothstep(0.0, 1.0, 1.0 - abs(2.0 * centeredHue / RRT_RED_WIDTH)) );
         
            aces.r += hueWeight * saturation * (RRT_RED_PIVOT - aces.r) * (1.0 - RRT_RED_SCALE);
         
            // Use ACEScg primaries as working space
            float3 acescg = max(0.0, ACES_to_ACEScg(aces));
         
            // Pre desaturate
            acescg = lerp(dot(acescg, AP1_RGB2Y).xxx, acescg, 0.96);
         
            const half ToeScale = 1 + _FilmBlackClip - _FilmToe;
            const half ShoulderScale = 1 + _FilmWhiteClip - _FilmShoulder;
         
            const float InMatch = 0.18;
            const float OutMatch = 0.18;
         
            float ToeMatch;
            if (_FilmToe > 0.8)
            {
                // 0.18 will be on straight segment
                ToeMatch = (1 - _FilmToe - OutMatch) / _FilmSlope + log10(InMatch);
            }
            else
            {
                // 0.18 will be on toe segment
         
                // Solve for ToeMatch such that input of InMatch gives output of OutMatch.
                const float bt = (OutMatch + _FilmBlackClip) / ToeScale - 1;
                ToeMatch = log10(InMatch) - 0.5 * log((1 + bt) / (1 - bt)) * (ToeScale / _FilmSlope);
            }
         
            float StraightMatch = (1 - _FilmToe) / _FilmSlope - ToeMatch;
            float ShoulderMatch = _FilmShoulder / _FilmSlope - StraightMatch;
         
            half3 LogColor = log10(acescg);
            half3 StraightColor = _FilmSlope * (LogColor + StraightMatch);
         
            half3 ToeColor = (-_FilmBlackClip) + (2 * ToeScale) / (1 + exp((-2 * _FilmSlope / ToeScale) * (LogColor - ToeMatch)));
            half3 ShoulderColor = (1 + _FilmWhiteClip) - (2 * ShoulderScale) / (1 + exp((2 * _FilmSlope / ShoulderScale) * (LogColor - ShoulderMatch)));
         
            ToeColor = LogColor < ToeMatch ? ToeColor : StraightColor;
            ShoulderColor = LogColor > ShoulderMatch ? ShoulderColor : StraightColor;
         
            half3 t = saturate((LogColor - ToeMatch) / (ShoulderMatch - ToeMatch));
            t = ShoulderMatch < ToeMatch ? 1 - t : t;
            t = (3 - 2 * t)*t*t;
            half3 linearCV = lerp(ToeColor, ShoulderColor, t);
         
            // Post desaturate
            linearCV = lerp(dot(float3(linearCV), AP1_RGB2Y), linearCV, 0.93);
         
            // Returning positive AP1 values
            //return max(0, linearCV);
         
            // Convert to display primary encoding
            // Rendering space RGB to XYZ
            float3 XYZ = mul(AP1_2_XYZ_MAT, linearCV);
         
            // Apply CAT from ACES white point to assumed observer adapted white point
            XYZ = mul(D60_2_D65_CAT, XYZ);
         
            // CIE XYZ to display primaries
            linearCV = mul(XYZ_2_REC709_MAT, XYZ);
         
            linearCV = saturate(linearCV); //Protection to make negative return out.
         
            return linearCV;
        }

        float3 Tonemap(float3 colorLinear)
        {
            #if _ACTUAL_TONEMAP_GT
            {
                colorLinear.r = GranTurismoTonemapper(colorLinear.r);
                colorLinear.g = GranTurismoTonemapper(colorLinear.g);
                colorLinear.b = GranTurismoTonemapper(colorLinear.b);
            }
            #elif _TONEMAP_NEUTRAL
            {
                colorLinear = NeutralTonemap(colorLinear);
            }
            #elif _ACTUAL_TONEMAP_ACES
            {
                // Note: input is actually ACEScg (AP1 w/ linear encoding)
                float3 aces = ACEScg_to_ACES(colorLinear);
                #ifdef _TONEMAP_ACES_FILMIC
                    colorLinear = FilmicACESTonemap(aces);
                #else
                    colorLinear = AcesTonemap(aces);
                #endif
            }
            #endif

            return colorLinear;
        }

        float3 ProcessColorForHDR(float3 colorLinear)
        {
            #ifdef HDR_COLORSPACE_CONVERSION
                #if _ACTUAL_TONEMAP_ACES
                float3 aces = ACEScg_to_ACES(colorLinear);
                return HDRMappingACES(aces.rgb, PaperWhite, MinNits, MaxNits, RangeReductionMode, true);
                #elif _TONEMAP_NEUTRAL
                return HDRMappingFromRec2020(colorLinear.rgb, PaperWhite, MinNits, MaxNits, RangeReductionMode, HueShift, true);
                #else
                // Grading finished in Rec2020, converting to the expected color space and [0, 10k] nits range
                return RotateRec2020ToOutputSpace(colorLinear) * PaperWhite;
                #endif
            #endif

            return colorLinear;
        }

        float4 FragLutBuilderHdr(Varyings input) : SV_Target
        {
            // Lut space
            // We use Alexa LogC (El 1000) to store the LUT as it provides a good enough range
            // (~58.85666) and is good enough to be stored in fp16 without losing precision in the
            // darks
            float3 colorLutSpace = GetLutStripValue(input.texcoord, _Lut_Params);

            // Color grade & tonemap
            float3 gradedColor = ColorGrade(colorLutSpace);

#ifdef HDR_COLORSPACE_CONVERSION
            gradedColor = ProcessColorForHDR(gradedColor);
#else
            gradedColor = Tonemap(gradedColor);
#endif

            return float4(gradedColor, 1.0);
        }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "LutBuilderHdr"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragLutBuilderHdr
            ENDHLSL
        }
    }
}
