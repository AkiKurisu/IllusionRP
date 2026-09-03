#ifdef DEBUG_DISPLAY // Guard define here to be compliant with how shader graph generate code for include

#ifndef UNITY_DEBUG_DISPLAY_INCLUDED
#define UNITY_DEBUG_DISPLAY_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// When displaying lux meter we compress the light in order to be able to display value higher than 65504
// The sun is between 100 000 and 150 000, so we use 4 to be able to cover such a range (4 * 65504)
#define LUXMETER_COMPRESSION_RATIO  4

// DebugFont code assume black and white font with texture size 256x128 with bloc of 16x16
#define DEBUG_FONT_TEXT_WIDTH   16
#define DEBUG_FONT_TEXT_HEIGHT  16
#define DEBUG_FONT_TEXT_COUNT_X 16
#define DEBUG_FONT_TEXT_COUNT_Y 8
#define DEBUG_FONT_TEXT_ASCII_START 32

#define DEBUG_FONT_TEXT_SCALE_WIDTH 10 // This control the spacing between characters (if a character fill the text block it will overlap).

#define TONEMAPPINGMODE_NONE (0)
#define TONEMAPPINGMODE_NEUTRAL (1)
#define TONEMAPPINGMODE_ACES (2)

// Draw a signed integer
// Can't display more than 16 digit
// The two following parameter are for float representation
// leading0 is used when drawing frac part of a float to draw the leading 0 (call is in charge of it)
// forceNegativeSign is used to force to display a negative sign as -0 is not recognize
void DrawInteger(int intValue, float3 fontColor, uint2 currentUnormCoord, inout uint2 fixedUnormCoord, inout float3 color, int leading0, bool forceNegativeSign)
{
    const uint maxStringSize = 16;

    uint absIntValue = abs(intValue);

    // 1. Get size of the number of display
    int numEntries = min((intValue == 0 ? 0 : log10(absIntValue)) + ((intValue < 0 || forceNegativeSign) ? 1 : 0) + leading0, maxStringSize);

    // 2. Shift curseur to last location as we will go reverse
    fixedUnormCoord.x += numEntries * DEBUG_FONT_TEXT_SCALE_WIDTH;

    // 3. Display the number
    bool drawCharacter = true; // bit weird, but it is to appease the compiler.
    for (uint j = 0; j < maxStringSize; ++j)
    {
        // Numeric value incurrent font start on the second row at 0
        if(drawCharacter)
            DrawCharacter((absIntValue % 10) + '0', fontColor, currentUnormCoord, fixedUnormCoord, color, -1);

        if (absIntValue  < 10)
            drawCharacter = false;

        absIntValue /= 10;
    }

    // 4. Display leading 0
    if (leading0 > 0)
    {
        for (int i = 0; i < leading0; ++i)
        {
            DrawCharacter('0', fontColor, currentUnormCoord, fixedUnormCoord, color, -1);
        }
    }

    // 5. Display sign
    if (intValue < 0 || forceNegativeSign)
    {
        DrawCharacter('-', fontColor, currentUnormCoord, fixedUnormCoord, color, -1);
    }

    // 6. Reset cursor at end location
    fixedUnormCoord.x += (numEntries + 2) * DEBUG_FONT_TEXT_SCALE_WIDTH;
}

void DrawInteger(int intValue, float3 fontColor, uint2 currentUnormCoord, inout uint2 fixedUnormCoord, inout float3 color)
{
    DrawInteger(intValue, fontColor, currentUnormCoord, fixedUnormCoord, color, 0, false);
}

void DrawFloatExplicitPrecision(float floatValue, float3 fontColor, uint2 currentUnormCoord, uint digitCount, inout uint2 fixedUnormCoord, inout float3 color)
{
    if (IsNaN(floatValue))
    {
        DrawCharacter('N', fontColor, currentUnormCoord, fixedUnormCoord, color);
        DrawCharacter('a', fontColor, currentUnormCoord, fixedUnormCoord, color);
        DrawCharacter('N', fontColor, currentUnormCoord, fixedUnormCoord, color);
    }
    else
    {
        int intValue = int(floatValue);
        bool forceNegativeSign = floatValue >= 0.0f ? false : true;
        DrawInteger(intValue, fontColor, currentUnormCoord, fixedUnormCoord, color, 0, forceNegativeSign);
        DrawCharacter('.', fontColor, currentUnormCoord, fixedUnormCoord, color);
        int fracValue = int(frac(abs(floatValue)) * pow(10, digitCount));
        int leading0 = digitCount - (int(log10(fracValue)) + 1); // Counting leading0 to add in front of the float
        DrawInteger(fracValue, fontColor, currentUnormCoord, fixedUnormCoord, color, leading0, false);
    }
}

void DrawFloat(float floatValue, float3 fontColor, uint2 currentUnormCoord, inout uint2 fixedUnormCoord, inout float3 color)
{
    DrawFloatExplicitPrecision(floatValue, fontColor, currentUnormCoord, 6, fixedUnormCoord, color);
}

// Debug rendering is performed at the end of the frame (after post-processing).
// Debug textures are never flipped upside-down automatically. Therefore, we must always flip manually.
bool ShouldFlipDebugTexture()
{
    #if UNITY_UV_STARTS_AT_TOP
        return (_ProjectionParams.x > 0);
    #else
        return (_ProjectionParams.x < 0);
    #endif
}

#endif

#endif // DEBUG_DISPLAY
