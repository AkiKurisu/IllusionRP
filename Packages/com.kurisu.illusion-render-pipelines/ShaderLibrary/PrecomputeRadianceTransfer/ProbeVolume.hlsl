#ifndef PRT_PROBE_VOLUME_INCLUDED
#define PRT_PROBE_VOLUME_INCLUDED

struct Surfel
{
    float3 position;
    float3 normal;
    float3 albedo;
    uint flags;
    uint renderingLayerMask;
    uint objectLayerMask;
    uint materialKey;
    int nearestProbe;
};

struct SurfelIndices { uint surfelStart; uint surfelCount; };
struct BrickFactor { int brickIndex; float sh[9]; };
struct PRTProbeData
{
    int factorStart;
    int factorCount;
    int skyStart;
    int skyCount;
    float3 captureOffset;
    uint validity;
};
struct PRTSkySample { float3 direction; float weight; };

CBUFFER_START(PRTProbeVolumeConstants)
float4 _prtGridOrigin;
int4 _prtGridMin;
int4 _prtGridCount;
int4 _prtWindowMin;
int4 _prtWindowCount;
float _prtGridSpacing;
uint _prtPublicationGeneration;
uint _prtVolumeEnabled;
uint _prtLayoutPadding;
CBUFFER_END

uint PRTProbeIndex(int3 coordinate)
{
    int3 local = coordinate - _prtGridMin.xyz;
    return uint(local.x * _prtGridCount.y * _prtGridCount.z + local.y * _prtGridCount.z + local.z);
}

int3 PRTProbeCoordinate(uint index)
{
    int yz = _prtGridCount.y * _prtGridCount.z;
    int x = int(index) / yz;
    int rem = int(index) % yz;
    return int3(x, rem / _prtGridCount.z, rem % _prtGridCount.z) + _prtGridMin.xyz;
}

int3 PRTTextureCoordinate(int3 slot, uint coefficient)
{
    return int3(slot.x, slot.z, slot.y + int(coefficient) * _prtWindowCount.y);
}

bool PRTInterpolationCell(float3 worldPosition, int3 minimum, int3 count, out int3 cell, out float3 rate)
{
    cell = minimum;
    rate = 0;
    if (_prtGridSpacing <= 0 || any(count <= 0))
        return false;
    float3 coordinate = (worldPosition - _prtGridOrigin.xyz) / _prtGridSpacing;
    [unroll]
    for (int axis = 0; axis < 3; axis++)
    {
        if (count[axis] == 1)
        {
            if (abs(coordinate[axis] - float(minimum[axis])) > 0.5)
                return false;
        }
        else
        {
            float end = float(minimum[axis] + count[axis] - 1);
            if (coordinate[axis] < float(minimum[axis]) || coordinate[axis] > end)
                return false;
            cell[axis] = min(int(floor(coordinate[axis])), minimum[axis] + count[axis] - 2);
            rate[axis] = saturate(coordinate[axis] - float(cell[axis]));
        }
    }
    return true;
}

float PRTCornerWeight(uint corner, float3 rate)
{
    float3 t = float3((corner & 4u) != 0u, (corner & 2u) != 0u, (corner & 1u) != 0u);
    float3 weights = lerp(1.0 - rate, rate, t);
    return weights.x * weights.y * weights.z;
}

int3 PRTCornerOffset(uint corner)
{
    return int3((corner & 4u) != 0u, (corner & 2u) != 0u, (corner & 1u) != 0u);
}

void UnpackIntensityValidity(uint packed, out float intensity, out float validity)
{
    intensity = float(packed & 0x00FFFFFFu) * (5.0 / 16777215.0);
    validity = float(packed >> 24) * (1.0 / 255.0);
}
#endif
