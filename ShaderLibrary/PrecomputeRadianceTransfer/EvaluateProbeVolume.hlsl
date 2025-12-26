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
Texture3D<float> _validityVoxel3D;

real3 TrilinearInterpolation(in real3 value[8], in real validity[8], in real intensity[8], real3 rate)
{
    // Calculate interpolation weights for each corner
    real w[8];
    w[0] = (1.0 - rate.x) * (1.0 - rate.y) * (1.0 - rate.z);
    w[1] = (1.0 - rate.x) * (1.0 - rate.y) * rate.z;
    w[2] = (1.0 - rate.x) * rate.y * (1.0 - rate.z);
    w[3] = (1.0 - rate.x) * rate.y * rate.z;
    w[4] = rate.x * (1.0 - rate.y) * (1.0 - rate.z);
    w[5] = rate.x * (1.0 - rate.y) * rate.z;
    w[6] = rate.x * rate.y * (1.0 - rate.z);
    w[7] = rate.x * rate.y * rate.z;
    
    // Combine interpolation weight with validity weight, then apply intensity
    real totalWeight = 0.0;
    real3 result = real3(0, 0, 0);
    
    for (int i = 0; i < 8; i++)
    {
        real combinedWeight = w[i] * validity[i];
        // Apply per-probe intensity to the radiance value
        result += value[i] * intensity[i] * combinedWeight;
        totalWeight += combinedWeight;
    }
    
    // Normalize by total weight
    if (totalWeight > 0.001)
    {
        result /= totalWeight;
    }
    
    return result;
}

// Evaluate SH coefficients from 3D texture
real3 EvaluateProbeVolumeSH(
    in float3 worldPos, 
    in real3 normal,
    in real3 bakedGI,
    in Texture3D<float3> coefficientVoxel3D,
    in Texture3D<float> validityVoxel3D,
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

    real3 c[9];
    real3 Lo[8] = {
        real3(0, 0, 0),
        real3(0, 0, 0),
        real3(0, 0, 0),
        real3(0, 0, 0),
        real3(0, 0, 0),
        real3(0, 0, 0),
        real3(0, 0, 0),
        real3(0, 0, 0)
    };
    
    real validity[8] = { 0, 0, 0, 0, 0, 0, 0, 0 };
    real intensity[8] = { 1, 1, 1, 1, 1, 1, 1, 1 }; // Add intensity array

    // near 8 probes
    for (int i = 0; i < 8; i++)
    {
        int3 neighborCoord = probeCoord + offset[i];
        bool isInsideVoxel = IsProbeCoordInsideVoxel(neighborCoord, boundingBoxVoxelSize);

        UNITY_BRANCH
        if (!isInsideVoxel)
        {
            // Mark as invalid, will be skipped in weighted interpolation
            validity[i] = 0.0;
            intensity[i] = 1.0;
            Lo[i] = bakedGI; // fallback value if all probes are invalid
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
        
        // Load and unpack validity + intensity
        int3 validityCoord = int3(neighborCoord.x, neighborCoord.z, neighborCoord.y);
        float packedData = validityVoxel3D.Load(int4(validityCoord, 0));
        UnpackIntensityValidity(packedData, intensity[i], validity[i]);
    }

    // trilinear interpolation with intensity
    float3 minCorner = GetProbePositionFromTexture3DCoord(probeCoord, voxelGridSize, boundingBoxVoxelCorner);
    real3 rate = saturate((worldPos - minCorner) / voxelGridSize);
    real3 color = TrilinearInterpolation(Lo, validity, intensity, rate);
    
    return color;
}

real3 SampleProbeVolume(float3 worldPos, real3 normal, real3 bakedGI)
{
#ifndef SHADER_STAGE_COMPUTE
    UNITY_BRANCH
    if (_coefficientVoxelGridSize == 0)
    {
        return bakedGI;
    }
#endif
    
    real3 radiance = EvaluateProbeVolumeSH(
                       worldPos, 
                       normal,
                       bakedGI,
                       _coefficientVoxel3D,
                       _validityVoxel3D,
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