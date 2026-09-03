#ifndef SPHERICAL_GAUSSIAN_INCLUDED
#define SPHERICAL_GAUSSIAN_INCLUDED

///////////////////////////////////////////////////////////////////////////////
//                      Spherical Gaussian Functions                         //
///////////////////////////////////////////////////////////////////////////////
// Reference: https://therealmjp.github.io/posts/sss-sg/

struct FSphericalGaussian
{
    half3 Amplitude;
    half3 Axis;
    half Sharpness;
};

FSphericalGaussian MakeNormalizedSG(half3 LightDir, half Sharpness)
{
    FSphericalGaussian SG;
    SG.Axis = LightDir;
    SG.Sharpness = Sharpness; 
    // SG.Amplitude = SG.Sharpness / (2 * PI - 2 * PI * exp(-2 * SG.Sharpness));
    SG.Amplitude = 1;
    return SG;
}

half SGIrradianceFitted(FSphericalGaussian SG, half3 normal)
{
    half muDotN = dot(SG.Axis, normal);

    half c0 = 0.36;
    half c1 = 0.25 / c0;
 
    half eml  = exp(-SG.Sharpness);
    half em2l = eml * eml;
    half rl   = rcp(SG.Sharpness);
 
    half scale = 1.0f + 2.0f * em2l - rl;
    half bias  = (eml - em2l) * rl - em2l;
 
    half x  = sqrt(1.0f - scale);
    half x0 = c0 * muDotN;
    half x1 = c1 * x;
 
    half n = x0 + x1;
 
    half y = abs(x0) <= x1 ? n * n / x : saturate(muDotN);

    return scale * y + bias;
}

// Spherical Gaussian Pre-integrated SSS
half3 SGDiffuseLighting(half3 N, half3 L, half3 ScatterAmt)
{
    FSphericalGaussian red = MakeNormalizedSG(L, 1 / max(ScatterAmt.x, 0.0001f));
    FSphericalGaussian green = MakeNormalizedSG(L, 1 / max(ScatterAmt.y, 0.0001f));
    FSphericalGaussian blue = MakeNormalizedSG(L, 1 / max(ScatterAmt.z, 0.0001f));
    half3 diffuse = half3(SGIrradianceFitted(red, N), SGIrradianceFitted(green, N), SGIrradianceFitted(blue, N));

    // Tonemapping
    // Reference: https://zhuanlan.zhihu.com/p/139836594?tdsourcetag=s_pctim_aiomsg
    // half3 x = max(0, diffuse - 0.004);
    // diffuse = x * (6.2 * x + 0.5) / (x * (6.2 * x + 1.7) + 0.06);
    return diffuse;
}
#endif