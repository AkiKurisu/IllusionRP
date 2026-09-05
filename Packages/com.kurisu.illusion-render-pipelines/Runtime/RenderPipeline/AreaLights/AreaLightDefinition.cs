using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Illusion.Rendering.AreaLights
{
    internal enum GPULightType
    {
        Rectangle = 6
    }

    internal enum CookieMode
    {
        None = 0,
        Clamp = 1,
        Repeat = 2
    }

    public enum CookieAtlasResolution
    {
        CookieResolution64 = 64,
        CookieResolution128 = 128,
        CookieResolution256 = 256,
        CookieResolution512 = 512,
        CookieResolution1024 = 1024,
        CookieResolution2048 = 2048,
        CookieResolution4096 = 4096,
        CookieResolution8192 = 8192,
        CookieResolution16384 = 16384
    }

    public enum CookieAtlasGraphicsFormat
    {
        R11G11B10 = GraphicsFormat.B10G11R11_UFloatPack32,
        R16G16B16A16 = GraphicsFormat.R16G16B16A16_SFloat,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AreaLightData
    {
        // Packing order depends on chronological access to avoid cache misses
        // Make sure to respect the 16-byte alignment
        public Vector3 positionRWS;
        public uint lightLayers;

        public Vector3 forward;
        public GPULightType lightType;

        public Vector3 right;
        public float penumbraTint;

        public float range;
        public CookieMode cookieMode;
        public int shadowIndex;             // -1 if unused (TODO: 16 bit)
        public float rangeAttenuationScale;

        public Vector3 up;
        public float rangeAttenuationBias;

        public Vector3 color;
        public float shadowDimmer;

        public Vector4 cookieScaleOffset;       // coordinates of the cookie texture in the atlas

        public Vector3 shadowTint;              // Use to tint shadow color
        public int nonLightMappedOnly;      // Used with ShadowMask feature (TODO: use a bitfield)

        public float minRoughness;            // This is use to give a small "area" to punctual light, as if we have a light with a radius.
        public int screenSpaceShadowIndex;  // -1 if unused (TODO: 16 bit)
        public float diffuseDimmer;
        public float specularDimmer;

        public Vector4 shadowMaskSelector;      // Used with ShadowMask feature

        public Vector4 size;                    // Used by area (X = length or width, Y = height, Z = CosBarnDoorAngle, W = BarnDoorLength) and punctual lights (X = radius)
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HDShadowData
    {
        public Vector3 rot0;
        public Vector3 rot1;
        public Vector3 rot2;
        public Vector3 pos;
        public Vector4 proj;

        public Vector2 atlasOffset;
        public float worldTexelSize;
        public float normalBias;

        public Vector4 zBufferParam;
        public Vector4 shadowMapSize;

        public Vector4 shadowFilterParams0;
        public Vector4 dirLightPCSSParams0;
        public Vector4 dirLightPCSSParams1;

        public Vector3 cacheTranslationDelta;
        public float isInCachedAtlas;

        public Matrix4x4 shadowToWorld;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderVariablesAreaLights
    {
        public int _AreaLightCount;
        public int _AreaLightPadding0;
        public int _AreaLightPadding1;
        public int _AreaLightPadding2;
        public Vector4 _AreaShadowAtlasSize;
        public Vector4 _CachedAreaShadowAtlasSize;
        public Vector4 _CookieAtlasSize;
        public Vector4 _CookieAtlasData;
    }

    internal enum LTCLightingModel
    {
        // Lit, Stack-Lit and Fabric/Silk
        GGX,
        DisneyDiffuse,

        // Fabric/CottonWool shader
        Charlie,
        // FabricLambert, (Isotropic)

        // Hair
        KajiyaKaySpecular,
        // KajiyaKayDiffuse, (Isotropic)
        Marschner, // TODO

        // Other
        CookTorrance,
        Ward,
        OrenNayar,
        Count
    }

    /// <summary>
    /// Shadow filtering quality for area lights.
    /// </summary>
    public enum HDAreaShadowFilteringQuality
    {
        /// <summary>
        /// Area Medium Shadow Filtering Quality
        /// </summary>
        Medium = 0,
        /// <summary>
        /// Area High Shadow Filtering Quality
        /// </summary>
        High = 1
    }
}
