#ifndef PRT_PROBE_VOLUME_INCLUDED
#define PRT_PROBE_VOLUME_INCLUDED

struct PRTCaptureSample
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

// Layout shared with Surfel in PRTData.cs: unorm16 position within the sector's surfel bounds, octahedral unorm12
// normal, sRGB unorm8 albedo.
struct Surfel
{
    uint positionXY;
    uint positionZAlbedoRG;
    uint normalAlbedoB;
    int nearestProbe;
};

float3 PRTSurfelPosition(Surfel surfel, float3 boundsMin, float3 boundsSize)
{
    float3 q = float3(surfel.positionXY & 0xFFFFu, surfel.positionXY >> 16, surfel.positionZAlbedoRG & 0xFFFFu) / 65535.0;
    return boundsMin + q * boundsSize;
}

float3 PRTSurfelNormal(Surfel surfel)
{
    float2 e = float2(surfel.normalAlbedoB & 0xFFFu, (surfel.normalAlbedoB >> 12) & 0xFFFu) / 4095.0 * 2.0 - 1.0;
    float3 n = float3(e, 1.0 - abs(e.x) - abs(e.y));
    float t = saturate(-n.z);
    n.xy += float2(n.x >= 0 ? -t : t, n.y >= 0 ? -t : t);
    return normalize(n);
}

float PRTSRGBToLinear(float c)
{
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

float3 PRTSurfelAlbedo(Surfel surfel)
{
    float3 srgb = float3((surfel.positionZAlbedoRG >> 16) & 0xFFu, surfel.positionZAlbedoRG >> 24, surfel.normalAlbedoB >> 24) / 255.0;
    return float3(PRTSRGBToLinear(srgb.r), PRTSRGBToLinear(srgb.g), PRTSRGBToLinear(srgb.b));
}

struct SurfelIndices { uint surfelStart; uint surfelCount; uint renderingLayerMask; uint objectLayerMask; };
// Layout shared with BrickFactor in PRTData.cs: sector-local brick index and nine half coefficients.
struct BrickFactor { uint brickSh0; uint sh12; uint sh34; uint sh56; uint sh78; };

uint PRTFactorBrick(BrickFactor factor) { return factor.brickSh0 & 0xFFFFu; }

void PRTFactorSH(BrickFactor factor, out float sh[9])
{
    sh[0] = f16tof32(factor.brickSh0 >> 16);
    sh[1] = f16tof32(factor.sh12); sh[2] = f16tof32(factor.sh12 >> 16);
    sh[3] = f16tof32(factor.sh34); sh[4] = f16tof32(factor.sh34 >> 16);
    sh[5] = f16tof32(factor.sh56); sh[6] = f16tof32(factor.sh56 >> 16);
    sh[7] = f16tof32(factor.sh78); sh[8] = f16tof32(factor.sh78 >> 16);
}
struct PRTProbeData
{
    int factorStart;
    int factorCount;
    float3 captureOffset;
    uint validity;
};

CBUFFER_START(PRTProbeVolumeConstants)
float4 _prtGridOrigin;
int4 _prtGridMin;
int4 _prtGridCount;
int4 _prtSlotCount;
float _prtGridSpacing;
uint _prtPublicationGeneration;
uint _prtVolumeEnabled;
uint _prtCascadeCount;
float4 _prtCascadeCenter;
int4 _prtCascadeWindowMin[4];
int4 _prtCascadeWindowCount[4];
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

int PRTWrap(int coordinate, int count)
{
    return coordinate - count * int(floor(float(coordinate) / float(count)));
}

// Horizontal axes are toroidal so a window step only republishes the newly covered slabs. Cascades share one
// texture: level c occupies depth slices [c * 9 * slots.y, (c + 1) * 9 * slots.y), coefficient-major.
int3 PRTPublicationSlot(int3 node, uint cascade)
{
    return int3(PRTWrap(node.x, _prtSlotCount.x), node.y - _prtCascadeWindowMin[cascade].y, PRTWrap(node.z, _prtSlotCount.z));
}

int3 PRTTextureCoordinate(int3 slot, uint cascade, uint coefficient)
{
    return int3(slot.x, slot.z, slot.y + int(cascade * 9u + coefficient) * _prtSlotCount.y);
}

// Node coordinates of a cascade level, relative to the grid minimum; level c has spacing * 2^c.
float3 PRTCascadeCoordinate(float3 worldPosition, uint cascade)
{
    return ((worldPosition - _prtGridOrigin.xyz) / _prtGridSpacing - float3(_prtGridMin.xyz)) / float(1u << cascade);
}

// A single-node axis accepts half a cell around its node and never interpolates.
bool PRTInterpolationCell(float3 coordinate, int3 minimum, int3 count, out int3 cell, out float3 rate)
{
    float3 interpolated = float3(count > 1);
    float3 margin = 0.5 - 0.5 * interpolated;
    cell = clamp(int3(floor(coordinate)), minimum, max(minimum + count - 2, minimum));
    rate = saturate(coordinate - float3(cell)) * interpolated;
    return _prtGridSpacing > 0 && all(count > 0)
        && all(coordinate >= float3(minimum) - margin) && all(coordinate <= float3(minimum + count - 1) + margin);
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
