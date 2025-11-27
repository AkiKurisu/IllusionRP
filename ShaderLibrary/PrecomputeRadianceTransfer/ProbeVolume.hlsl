#ifndef PRT_PROBE_VOLUME_INCLUDED
#define PRT_PROBE_VOLUME_INCLUDED

//--------------------------------------------------------------------------------------------------
// PRT Definition
//--------------------------------------------------------------------------------------------------

struct Surfel
{
    float3 position;
    float3 normal;
    float3 albedo;
    float skyMask;
};

struct SurfelIndices
{
    uint surfelStart;
    uint surfelCount;
};

struct BrickRadiance
{
    float3 averageRadiance;
    float3 averagePosition;
    float averageSkyVisibility;
};

struct BrickFactor
{
    int brickIndex;
    float weight;
};

#define TOROIDAL_ADDRESSING 1

//--------------------------------------------------------------------------------------------------
// Implementation
//--------------------------------------------------------------------------------------------------

// Convert probe grid 3D coordinates to 3D Texture coordinates
int3 GetProbeTexture3DCoordFrom3DCoord(int3 probeCoord, uint shIndex)
{
    return int3(probeCoord.x, probeCoord.z, probeCoord.y * 9 + shIndex);
}

// Decode SH coefficients from 3D texture for a specific probe
void DecodeSHCoefficientFromVoxel3D(inout float3 c[9], in Texture3D<float3> voxel3D, int3 probeCoord)
{
    // Sample RGB components separately for each SH coefficient
    for (int i = 0; i < 9; i++)
    {
        // Calculate 3D texture coordinates
        int3 texCoord = GetProbeTexture3DCoordFrom3DCoord(probeCoord, i);
        c[i] = voxel3D.Load(int4(texCoord, 0));
    }
}

// Convert probe world position to probe grid 3D coordinates
int3 GetProbe3DCoordFromPosition(float3 worldPos, float voxelGridSize, float4 voxelCorner)
{
    float3 probeIndexF = floor((worldPos.xyz - voxelCorner.xyz) / voxelGridSize);
    int3 probeIndex3 = int3(probeIndexF.x, probeIndexF.y, probeIndexF.z);
    return probeIndex3;
}

bool IsProbeCoordInsideVoxel(int3 probeCoord, float4 voxelSize)
{
    bool isInsideVoxelX = 0 <= (float)probeCoord.x && (float)probeCoord.x < voxelSize.x;
    bool isInsideVoxelY = 0 <= (float)probeCoord.y && (float)probeCoord.y < voxelSize.y;
    bool isInsideVoxelZ = 0 <= (float)probeCoord.z && (float)probeCoord.z < voxelSize.z;
    bool isInsideVoxel = isInsideVoxelX && isInsideVoxelY && isInsideVoxelZ;
    return isInsideVoxel;
}

// Convert 3D texture coordinates to probe index
float3 GetProbePositionFromTexture3DCoord(uint3 probeCoord, float voxelGridSize, float4 voxelCorner)
{
    float3 res = float3(probeCoord.x, probeCoord.y, probeCoord.z) * voxelGridSize + voxelCorner.xyz;
    return res;
}

// Toroidal Addressing
uint3 Wrap3DCoord(int3 coord, uint3 length, uint3 maxSize)
{
    uint3 offset = (maxSize / length + 2) * length; // Apply bias first
    coord += offset;
    uint3 result = uint3(coord) % uint3(length);
    return result;
}

// Convert probe index to 3D texture coordinates
uint3 GetProbeTexture3DCoordFromIndex(uint probeIndex, uint shIndex, float4 voxelSize,
    float4 boundingBoxMin, float4 boundingBoxSize, float4 originalBoundingBoxMin)
{
    // Convert probe index to 3D grid coordinates
    uint probeSizeY = uint(voxelSize.y);
    uint probeSizeZ = uint(voxelSize.z);
    
    uint x = probeIndex / (probeSizeY * probeSizeZ);
    uint temp = probeIndex % (probeSizeY * probeSizeZ);
    uint y = temp / probeSizeZ;
    uint z = temp % probeSizeZ;

#ifdef TOROIDAL_ADDRESSING
    // Calculate relative coordinates within original bounding box
    int3 bboxCoord = int3(x, y, z) - int3(originalBoundingBoxMin.xyz);

    // Toroidal Addressing
    bboxCoord = Wrap3DCoord(bboxCoord, boundingBoxSize.xyz, voxelSize.xyz);
#else
    // Calculate relative coordinates within current bounding box
    uint3 bboxCoord = uint3(x, y, z) - uint3(boundingBoxMin.xyz);
#endif
    
    // Convert to 3D texture coordinates
    uint3 texCoord = GetProbeTexture3DCoordFrom3DCoord(bboxCoord, shIndex);
    return texCoord;
}
#endif // defined(PRT_SH_INCLUDED)