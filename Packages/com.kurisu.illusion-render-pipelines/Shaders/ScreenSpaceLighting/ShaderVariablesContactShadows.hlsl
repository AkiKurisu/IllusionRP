// Disable warning
// https://discussions.unity.com/t/globally-suppress-pow-f-e-negative-f-warning/807217/16
#pragma warning(disable: 3556)

#define CONTACT_SHADOW_FADE_BITS (8)
#define CONTACT_SHADOW_MASK_BITS (24)
#define CONTACT_SHADOW_FADE_MASK (255)
#define CONTACT_SHADOW_MASK_MASK (16777215)
#define _ContactShadowOpacity 1.0f
#define TILE_SIZE_FPTL (16)


RWTexture2D<float> _ContactShadowTextureUAV;

CBUFFER_START(ContactShadowParameters)
float4  _ContactShadowParamsParameters;
float4  _ContactShadowParamsParameters2;
float4  _ContactShadowParamsParameters3;
CBUFFER_END

#define _ContactShadowLength                _ContactShadowParamsParameters.x
#define _ContactShadowDistanceScaleFactor   _ContactShadowParamsParameters.y
#define _ContactShadowFadeEnd               _ContactShadowParamsParameters.z
#define _ContactShadowFadeOneOverRange      _ContactShadowParamsParameters.w
#define _ContactShadowMinDistance           _ContactShadowParamsParameters2.y
#define _ContactShadowFadeInEnd             _ContactShadowParamsParameters2.z
#define _ContactShadowBias                  _ContactShadowParamsParameters2.w
#define _SampleCount                        (int)_ContactShadowParamsParameters3.x
#define _ContactShadowThickness             _ContactShadowParamsParameters3.y
#define _FrameCountMod8                     (int)_ContactShadowParamsParameters3.z

void UnpackContactShadowData(uint contactShadowData, out float fade, out uint mask)
{
    fade = float(contactShadowData >> CONTACT_SHADOW_MASK_BITS) / (float)CONTACT_SHADOW_FADE_MASK;
    mask = contactShadowData & CONTACT_SHADOW_MASK_MASK; // store only the first 24 bits which represent
}

uint PackContactShadowData(float fade, uint mask)
{
    uint fadeAsByte = (uint(saturate(fade) * CONTACT_SHADOW_FADE_MASK) << CONTACT_SHADOW_MASK_BITS);

    return fadeAsByte | mask;
}

float GetContactShadow(float fade, uint mask)
{
    bool occluded = mask & mask != 0;
    return 1.0 - occluded * fade * _ContactShadowOpacity;
}