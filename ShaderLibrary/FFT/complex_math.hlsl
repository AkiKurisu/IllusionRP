#pragma once

static const float PI = 3.14159265f;

float2 cmul(float2 a, float2 b)
{
	return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

float4 cmul(float2 a, float4 b)
{
	return float4(cmul(a, b.xy), cmul(a, b.zw));
}

float4 cmul(float4 b, float2 a)
{
	return float4(cmul(a, b.xy), cmul(a, b.zw));
}

float4 cmul(float4 a, float4 b)
{
	return float4(cmul(a.xy, b.xy), cmul(a.zw, b.zw));
}

float2 cmuli(float2 a)
{
	return float2(- a.y, a.x);
}

float4 cmuli(float4 a)
{
	return float4(- a.y, a.x, -a.w, a.z);
}

float2 cconj(float2 a)
{
	return float2(a.x, -a.y);
}

float4 cconj(float4 a)
{
	return float4(a.x, -a.y, a.z, -a.w);
}

//return a*b and a*conj(b)
float4 cconjmul(float2 a, float2 b)
{
	float v0 = a.x * b.x;
	float v1 = a.y * b.y;
	float v2 = a.x * b.y;
	float v3 = a.y * b.x;
	return float4(v0 - v1, v2 + v3, v0 + v1, -v2 + v3);
}

half2 cmul(half2 a, half2 b)
{
	return half2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

half4 cmul(half2 a, half4 b)
{
	return half4(cmul(a, b.xy), cmul(a, b.zw));
}

half4 cmul(half4 b, half2 a)
{
	return half4(cmul(a, b.xy), cmul(a, b.zw));
}

half4 cmul(half4 a, half4 b)
{
	return half4(cmul(a.xy, b.xy), cmul(a.zw, b.zw));
}

half2 cmuli(half2 a)
{
	return half2(- a.y, a.x);
}

half4 cmuli(half4 a)
{
	return half4(- a.y, a.x, -a.w, a.z);
}

half2 cconj(half2 a)
{
	return half2(a.x, -a.y);
}

//return a*b and a*conj(b)
half4 cconjmul(half2 a, half2 b)
{
	half v0 = a.x * b.x;
	half v1 = a.y * b.y;
	half v2 = a.x * b.y;
	half v3 = a.y * b.x;
	return half4(v0 - v1, v2 + v3, v0 + v1, -v2 + v3);
}

uint fast_mul(uint a, uint log2_b)
{
	return a << log2_b;
}

uint fast_div(uint a, uint log2_b)
{
	return a >> log2_b;
}

// b must be power of 2
uint fast_mod(uint a, uint b)
{
	return a & (b - 1);
}

uint ceil_div(uint a, uint b)
{
	return (a + b - 1) / b;
}