#ifndef ILLUSION_PATH_TRACING_HLSL_2018_INCLUDED
#define ILLUSION_PATH_TRACING_HLSL_2018_INCLUDED

#if !defined(__HLSL_VERSION) || __HLSL_VERSION < 2021

float  select(bool  c, float  a, float  b) { return c ? a : b; }
float2 select(bool2 c, float2 a, float2 b) { return c ? a : b; }
float3 select(bool3 c, float3 a, float3 b) { return c ? a : b; }
float4 select(bool4 c, float4 a, float4 b) { return c ? a : b; }
float2 select(bool2 c, float  a, float  b) { return c ? a.xx : b.xx; }
float3 select(bool3 c, float  a, float  b) { return c ? a.xxx : b.xxx; }
int    select(bool  c, int    a, int    b) { return c ? a : b; }
int2   select(bool2 c, int2   a, int2   b) { return c ? a : b; }
int3   select(bool3 c, int3   a, int3   b) { return c ? a : b; }
int4   select(bool4 c, int4   a, int4   b) { return c ? a : b; }
uint   select(bool  c, uint   a, uint   b) { return c ? a : b; }
uint2  select(bool2 c, uint2  a, uint2  b) { return c ? a : b; }
uint3  select(bool3 c, uint3  a, uint3  b) { return c ? a : b; }
uint4  select(bool4 c, uint4  a, uint4  b) { return c ? a : b; }

#endif

#endif
