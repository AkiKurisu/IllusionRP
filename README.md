<div align="center">

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/AkiKurisu/IllusionRP)
[![Zhihu](https://img.shields.io/badge/知乎-AkiKurisu-0084ff?style=flat-square)](https://www.zhihu.com/people/akikurisu)
[![Bilibili](https://img.shields.io/badge/Bilibili-爱姬Kurisu-00A1D6?style=flat-square)](https://space.bilibili.com/20472331)

# IllusionRP

Unity high-definition graphics plugin for Universal Render Pipelines.

<img src="./Documentation~/images/banner.png">

</div>

# Highlights

- High-quality graphics features with one-click integration for your URP project
- Ready-to-use shaders and templates for Amplify Shader Editor

# Demo

<b>Sponza Demo</b> 

IllusionRP demo for Sponza scene, see [AkiKurisu/IllusionRP-Demo](https://github.com/AkiKurisu/IllusionRP-Demo) for more details.

![Demo](https://github.com/AkiKurisu/IllusionRP-Demo/raw/master/Documentation/images/sponza.png)

<b>AIChara Remaster Demo</b> 

IllusionRP is first designed to re-rendering game characters of @ILLUSION.

Demo is WIP, stay tuned.

![Showcase](./Documentation~/images/showcase.png)

# Notice

Designed for <b>Forward</b> and <b>Forward+</b> rendering path, <b>Deferred</b> rendering path is not supported.

> Add new shading model in deferred rendering path need modify the URP source code, which is hard to maintain. While using forward rendering path, we can add shaders without limitations.

# Platforms

- Android (Vulkan)

- Standalone Windows (DX12)

# Version

[IllusionRP for Unity 6](https://github.com/AkiKurisu/IllusionRP/tree/master)

- Unity 6.3 (URP 17.3.0)

[IllusionRP for Unity 2022 ~ 2023](https://github.com/AkiKurisu/IllusionRP/tree/urp14)

- Unity 2022.3.62f1 (URP 14.0.12)

- Unity 2023.2.22f1 (URP 16.0.6)

# Install

Use git URL to download package by Unity Package Manager ```https://github.com/AkiKurisu/IllusionRP.git```.

To fix compute shader compilation error when enable `Forward+`, copy the `./com.unity.render-pipelines.ps5~` folder to your project `Package` directory and rename it to `com.unity.render-pipelines.ps5`. 

# Setup
1. Replace Post Process Data with `IllusionRPPostProcessData` in your URP renderer asset.
    ![Post Process Data](./Documentation~/images/post_process_data.png)
2. Add `Illusion Graphics` renderer feature in your URP renderer asset.
    ![Renderer Feature](./Documentation~/images/renderer_feature.png)

>[!Tip]
> For best visual experience and performance, I recommend you to enable `Depth Priming` and `Forward+` in renderer asset to use <b>Pre-Z Cluster Forward Rendering</b>.

# Features

Introduction to included rendering features.

## Skin

Support <b>Screen Space Subsurface Scattering (SSSSS)</b> ported from HDRP and fallback to <b>Spherical Gaussian Subsurface Scattering (SGSSS)</b> introduced by [Matt - Approximating Subsurface Scattering With Spherical Gaussians](https://therealmjp.github.io/posts/sss-sg/) when screen space sss is disabled.

<b>Dual Lobe Specular</b> provides a more natural skin effect.

![SSS](./Documentation~/images/sss.png)

## Hair

Support <b>Kajiya-Kay</b> shading model with better performance.

Support <b>Marschner</b> shading model implemented by Unreal Engine.

![Hair Shading](./Documentation~/images/hair_shading.png)

<b>Multipass</b> is used (opaque & transparent).

<b>OIT</b> to solve hair transparent rendering order based on NVIDIA's [Weighted Blended Order-Independent Transparency](https://jcgt.org/published/0002/02/09/).
  
## Fabric

Support <b>Anisotropy Specular</b>.

![Anisotropy](./Documentation~/images/anisotropy.png)

<b>Oren-Nayar</b> diffuse model.
  
<b>Sheen Scattering</b> with <b>Ashikhmin</b> (smooth) and <b>Charlie</b> (softer) model.

## Hybrid Lit

The upgraded version of `Universal Render Pipeline/Lit` shader.

Support <b>PreIntegrated Imaged Based Lighting</b> ported from HDRP.

Support all <b>Screen Space Lighting</b> features in IllusionRP.

Support <b>Order Independent Transparency</b>.

![Hybrid Lit](./Documentation~/images/hybrid_lit.png)

> [!TIP]
> You can directly replace the urp lit shader with `Universal Render Pipeline/Hybrid Lit` shader to get better lighting results.

## Water

Water shader is modified from URP samples and has been converted to ASE version.

> [!TIP]
> To sample full scene color instead of `_CameraOpaqueTexture`, you need enable `requireHistoryColor` or `ScreenSpaceReflection` in `Illusion Graphics` renderer feature.

## Shadows

Support <b>Per-Object Shadow</b> based on [stalomeow/StarRailNPRShader](https://github.com/stalomeow/StarRailNPRShader) to enhance character shadow quality.

Include <b>Contact Shadows</b> ported from HDRP to capture small details.

![Contact Shadows](./Documentation~/images/contact_shadows.png)

Support <b>Micro Shadows</b> ported from HDRP to simulate small details embedded in the Material of a GameObject, but not in its Mesh geometry.

Support <b>Fragment Shadow Bias</b> to reduce shadow acne.

> [!Warning]
> Enable Fragment Shadow Bias will let IllusionRP shaders not compatiable with URP's built-in shader since we now apply shadow bias in receiver fragement shader instead of in caster vertex shader.

Include <b>Percentage-Closer Soft Shadow</b> from [recaeee/RecaNoMaho_P](https://github.com/recaeee/RecaNoMaho_P) for more realistic shadows.

![PCSS](./Documentation~/images/pcss.png)

> [!TIP]
> Percentage-Closer Soft Shadow only works for Main Light Shadow Caster and Per-Object Shadow Caster.

## Ambient Occlusion

Include <b>Ground Truth Ambient Occlusion</b> based on [bladesero/GTAO_URP](https://github.com/bladesero/GTAO_URP) and HDRP implementation.

Support <b>AO Multi-Bounce</b> and <b>Separate AO</b> for diffuse and specular lighting.

![GTAO](./Documentation~/images/gtao.png)

## Global Illumination

Include <b>Screen Space Reflection</b> based on [EricHu33/URP_SSR](https://github.com/EricHu33/URP_SSR) and HDRP implementation.

![SSR](./Documentation~/images/ssr.png)

Support 3 algorithms: <b>View Space Linear Search</b>, <b>Screen Space Linear Search</b> and <b>Hiz Search</b>.

Include <b>Precomputed Radiance Transfer Global Illumination (PRTGI)</b> based on [AKGWSB/CasualPRT](https://github.com/AKGWSB/CasualPRT).

![PRTGI](./Documentation~/images/prtgi.png)

> [!TIP]
> `PRTGI` will not affect materials that use baked lightmaps.

Support <b>Reflection Probe Normalization</b> based on Unity Adaptive Probe Volume when enable PRTGI to prevent light leaking.

![Reflection Probe Normalization](./Documentation~/images/reflection_normalization.png)

Include <b>Screen Space Global Illumination</b> based on [jiaozi158/UnitySSGIURP](https://github.com/jiaozi158/UnitySSGIURP) and HDRP implementation.

![SSGI](./Documentation~/images/ssgi.png)

> [!TIP]
> Recommend to use `PRTGI` outdoor and combine with `SSGI` indoor. 
> 
> Also, `SSGI` will not affect materials that use baked lightmaps.

![SSGI + PRTGI](./Documentation~/images/gi_combine.png)

## Post Processing

Include <b>Automatic Exposure</b> ported from HDRP.

![Exposure](./Documentation~/images/exposure.png)

Include <b>Convolution Bloom</b> from [StellarWarp/High-Performance-Convolution-Bloom-On-Unity](https://github.com/StellarWarp/High-Performance-Convolution-Bloom-On-Unity). 

![Bloom](./Documentation~/images/bloom.png)

Include <b>Volumetric Light</b> from [CristianQiu/Unity-URP-Volumetric-Light](https://github.com/CristianQiu/Unity-URP-Volumetric-Light).

![Volumetric Light](./Documentation~/images/volumetric_light.png)

Include <b>Advanced Tonemapping</b> with two kinds: 

<b>Gran-Turismo</b> from [yaoling1997/GT-ToneMapping](https://github.com/yaoling1997/GT-ToneMapping) better for anime characters.

<b>Filmic ACES</b> from Unreal Engine which is more artist friendly.

![Tonemapping](./Documentation~/images/tonemapping.png)

> [!NOTE]
> Advanced Tonemapping only works in `High Dynamic Range` grading mode.

# Debugging

Recommended to install [Chris](https://github.com/AkiKurisu/Chris) to use Unreal-like console variables for debugging rendering features.

> Install from git url `https://github.com/AkiKurisu/Chris`

![Console Variable](./Documentation~/images/console_variables.png)

`Chris` also provides a Data-Driven Dynamic Volume Provider to support configuring volume profile per-platform.

![Dynamic Volume](./Documentation~/images/dynamic_volume.png)

# Documents

Documents are generated with [Devin AI](https://app.devin.ai/), please create an issue if you find any problems.

[Shaders](./Documentation~/Shaders.md)

[Lighting](./Documentation~/Lighting.md)

[Render Pipeline](./Documentation~/RenderPipeline.md)

[Optimization](./Documentation~/Optimization.md)

# Contribution

See [Contributing](./CONTRIBUTING) for more details.

# Credits

Thanks to the following great works and articles.

[bladesero/GTAO_URP](https://github.com/bladesero/GTAO_URP)

[stalomeow/StarRailNPRShader](https://github.com/stalomeow/StarRailNPRShader)

[ecidevilin/KhaosLWRP](https://github.com/ecidevilin/KhaosLWRP)

[EricHu33/URP_SSR](https://github.com/EricHu33/URP_SSR)

[AKGWSB/CasualPRT](https://github.com/AKGWSB/CasualPRT)

[HigashiSan/Weighted-Blended-OIT-in-Unity-URP](https://github.com/HigashiSan/Weighted-Blended-OIT-in-Unity-URP)

[Unity-Technologies/Graphics](https://github.com/Unity-Technologies/Graphics)

[yaoling1997/GT-ToneMapping](https://github.com/yaoling1997/GT-ToneMapping)

[StellarWarp/High-Performance-Convolution-Bloom-On-Unity](https://github.com/StellarWarp/High-Performance-Convolution-Bloom-On-Unity)

[CristianQiu/Unity-URP-Volumetric-Light](https://github.com/CristianQiu/Unity-URP-Volumetric-Light)

[recaeee/RecaNoMaho_P](https://github.com/recaeee/RecaNoMaho_P)

[jiaozi158/UnitySSGIURP](https://github.com/jiaozi158/UnitySSGIURP)

[Matt - Approximating Subsurface Scattering With Spherical Gaussians](https://therealmjp.github.io/posts/sss-sg/)

[傻头傻脑亚古兽 - Unity实现SSS皮肤次表面散射](https://zhuanlan.zhihu.com/p/583108480)

[jplee - Extra technique for Skin shader by URP](https://leegoonz.blog/2020/08/24/dual-lobe-for-skin-shader-by-urp/)

[jplee - UE4 ACES Tone mapping port URP](https://leegoonz.blog/2021/09/03/ue4-aces-tone-mapping-port-urp/)

[Naughty Dog - The Process of Creating Volumetric-based Materials in Uncharted 4](https://advances.realtimerendering.com/s2016/The%20Process%20of%20Creating%20Volumetric-based%20Materials%20in%20Uncharted%204.pptx)

[NVIDIA - Weighted Blended Order-Independent Transparency](https://jcgt.org/published/0002/02/09/)

[Casual Effects - Weighted, Blended Order-Independent Transparency](http://casual-effects.blogspot.com/2014/03/weighted-blended-order-independent.html)

[ZZNEWCLEAR13 - 在URP的片元着色器中应用阴影偏移](https://zznewclear13.github.io/posts/unity-urp-apply-shadow-bias-in-fragment-shader/)

# Articles

Technical articles related to IllusionRP.

[如何在Unity URP中让头发渲染更丝滑](https://zhuanlan.zhihu.com/p/1907549925065070387)

[UPR Screen Space Reflection实践](https://zhuanlan.zhihu.com/p/1912828657585590857)

[Unity预计算辐照度全局光照PRTGI实践与拓展（上）](https://zhuanlan.zhihu.com/p/1957857268528834096)

[Unity预计算辐照度全局光照PRTGI实践与拓展（下）](https://zhuanlan.zhihu.com/p/1957865115991908586)

[IllusionRP：Unity URP高质量渲染开发实践](https://zhuanlan.zhihu.com/p/1978425877839761874)

# License

Licensed under the Unity Companion License for Unity-dependent projects--see [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).

Unless expressly provided otherwise, the Software under this license is made available strictly on an "AS IS" BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.

For integrated third-party code, please refer to their licenses included in the package.