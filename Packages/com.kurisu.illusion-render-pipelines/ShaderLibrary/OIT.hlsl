#ifndef OIT_INCLUDED
#define OIT_INCLUDED

// OIT
#define _WEIGHTED_EQUATION_8 0
#define _WEIGHTED_EQUATION_9 1

// Reference: [Weighted Blended Order-Independent Transparency]
// https://jcgt.org/published/0002/02/09/
inline float OITWeight(in float3 color, in float alpha, in float z)
{
    // http://casual-effects.blogspot.com/2014/03/weighted-blended-order-independent.html
    // The color-based factor avoids color pollution from the edges of wispy clouds.
    // The z-based factor gives precedence to nearer surfaces.
    alpha = max(min(1.0, max(max(color.r, color.g), color.b) * alpha), alpha);
#if _WEIGHTED_EQUATION_8
    return alpha * max(1e-2, min(3 * 1e3, 10.0 / (1e-5 + pow(z / 5, 2) + pow(z / 200, 6))));
#elif _WEIGHTED_EQUATION_9
    float length = _ProjectionParams.y * 2000;
    return alpha * max(1e-2, min(3 * 1e3, 0.03 / (1e-5 + pow(z / length, 4))));
#else
    return alpha;
#endif
}
#endif