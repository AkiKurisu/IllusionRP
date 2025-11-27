#ifndef PRT_EVALUATE_PROBE_VOLUME_INCLUDED
#define PRT_EVALUATE_PROBE_VOLUME_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/SphericalHarmonics.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PrecomputeRadianceTransfer/ProbeVolume.hlsl"

#ifndef _PRT_GLOBAL_ILLUMINATION
    #define _PRT_GLOBAL_ILLUMINATION 0
#endif

#define _PRT_GLOBAL_ILLUMINATION_ON (_PRT_GLOBAL_ILLUMINATION && !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON))

float4 _coefficientVoxelCorner;
float4 _coefficientVoxelSize;
float4 _boundingBoxMin;
float4 _boundingBoxSize;
float4 _originalBoundingBoxMin;
float _coefficientVoxelGridSize;

Texture3D<float3> _coefficientVoxel3D;

float3 TrilinearInterpolationFloat3(in float3 value[8], float3 rate)
{
    float3 a = lerp(value[0], value[4], rate.x);    // 000, 100
    float3 b = lerp(value[2], value[6], rate.x);    // 010, 110
    float3 c = lerp(value[1], value[5], rate.x);    // 001, 101
    float3 d = lerp(value[3], value[7], rate.x);    // 011, 111
    float3 e = lerp(a, b, rate.y);
    float3 f = lerp(c, d, rate.y);
    float3 g = lerp(e, f, rate.z); 
    return g;
}

// Evaluate SH coefficients from 3D texture
float3 EvaluateProbeVolumeSH(
    in float3 worldPos, 
    in float3 normal,
    in float3 bakedGI,
    in Texture3D<float3> coefficientVoxel3D,
    in float voxelGridSize,
    in float4 voxelCorner,
    in float4 voxelSize,
    in float4 boundingBoxMin,
    in float4 boundingBoxSize,
    in float4 originalBoundingBoxMin
)
{
    float4 boundingBoxVoxelSize = boundingBoxSize;
    float4 boundingBoxVoxelCorner = boundingBoxMin * voxelGridSize + voxelCorner;
    
    // probe grid is already converted to bounding box coordinate
    int3 probeCoord = GetProbe3DCoordFromPosition(worldPos, voxelGridSize, boundingBoxVoxelCorner);
    int3 offset[8] = {
        int3(0, 0, 0), int3(0, 0, 1), int3(0, 1, 0), int3(0, 1, 1), 
        int3(1, 0, 0), int3(1, 0, 1), int3(1, 1, 0), int3(1, 1, 1), 
    };

    float3 c[9];
    float3 Lo[8] = {
        float3(0, 0, 0),
        float3(0, 0, 0),
        float3(0, 0, 0),
        float3(0, 0, 0),
        float3(0, 0, 0),
        float3(0, 0, 0),
        float3(0, 0, 0),
        float3(0, 0, 0)
    };

    // near 8 probes
    for (int i = 0; i < 8; i++)
    {
        int3 neighborCoord = probeCoord + offset[i];
        bool isInsideVoxel = IsProbeCoordInsideVoxel(neighborCoord, boundingBoxVoxelSize);

        UNITY_BRANCH
        if (!isInsideVoxel)
        {
            Lo[i] = bakedGI;
            continue;
        }
        
#ifdef TOROIDAL_ADDRESSING
        int3 voxelCoord = neighborCoord + (int3)boundingBoxMin.xyz;

        // Calculate relative coordinates within original bounding box
        int3 neighborProbeCoord = voxelCoord - int3(originalBoundingBoxMin.xyz);

        // Toroidal Addressing
        neighborProbeCoord = Wrap3DCoord(neighborProbeCoord, boundingBoxSize.xyz, voxelSize.xyz);
#else
        int3 neighborProbeCoord = neighborCoord;
#endif
        
        // decode SH9 from 3D texture
        DecodeSHCoefficientFromVoxel3D(c, coefficientVoxel3D, neighborProbeCoord);
        Lo[i] = IrradianceSH9(c, normal.xzy);
    }

    // trilinear interpolation
    float3 minCorner = GetProbePositionFromTexture3DCoord(probeCoord, voxelGridSize, boundingBoxVoxelCorner);
    float3 rate = saturate((worldPos - minCorner) / voxelGridSize);
    float3 color = TrilinearInterpolationFloat3(Lo, rate);
    
    return color;
}

float3 SampleProbeVolume(float3 worldPos, float3 normal, float3 bakedGI)
{
#ifndef SHADER_STAGE_COMPUTE
    UNITY_BRANCH
    if (_coefficientVoxelGridSize == 0)
    {
        return bakedGI;
    }
#endif
    
    float3 radiance = EvaluateProbeVolumeSH(
                       worldPos, 
                       normal,
                       bakedGI,
                       _coefficientVoxel3D,
                       _coefficientVoxelGridSize,
                       _coefficientVoxelCorner,
                       _coefficientVoxelSize,
                       _boundingBoxMin,
                       _boundingBoxSize,
                       _originalBoundingBoxMin
                   );
    return radiance;
}
#endif