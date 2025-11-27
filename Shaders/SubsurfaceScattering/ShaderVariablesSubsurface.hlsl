#ifndef SUBSURFACE_VARIABLES_INCLUDED
#define SUBSURFACE_VARIABLES_INCLUDED

// Should match SubsurfaceScatteringPass.ShaderVariables
GLOBAL_CBUFFER_START(ShaderVariablesSubsurface, b2)
    float4 _ShapeParamsAndMaxScatterDists[16];
    float4 _TransmissionTintsAndFresnel0[16];
    float4 _WorldScalesAndFilterRadiiAndThicknessRemaps[16];
    uint4 _DiffusionProfileHashTable[16];
    uint _DiffusionProfileCount;
    float3 _Padding;
CBUFFER_END

struct SSSData
{
    float3 diffuseColor;
    float  subsurfaceMask;
    uint   diffusionProfileIndex;
};

// Note: The SSS buffer used here is sRGB
void EncodeIntoSSSBuffer(SSSData sssData, out float4 outSSSBuffer)
{
    outSSSBuffer = float4(sssData.diffuseColor, PackFloatInt8bit(sssData.subsurfaceMask, sssData.diffusionProfileIndex, 16));
}

uint FindDiffusionProfileIndex(uint diffusionProfileHash)
{
    if (diffusionProfileHash == 0)
        return 0;

    uint diffusionProfileIndex = 0;
    uint i = 0;

    // Fetch the 4 bit index number by looking for the diffusion profile unique ID:
    for (i = 0; i < _DiffusionProfileCount; i++)
    {
        if (_DiffusionProfileHashTable[i].x == diffusionProfileHash)
        {
            diffusionProfileIndex = i;
            break;
        }
    }

    return diffusionProfileIndex;
}
#endif