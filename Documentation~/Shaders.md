# Shader Workflow

Combining nodes and code together is the juiciest way to make shader in Unity.

In IllusionRP, custom lighting models are handwritten in HLSL and passed to the <b>Amplify Shader Editor (ASE)</b> to read template shaders. Shader-level functionality is created in the ASE node editor.

For other rendering effects, such as post-processing and screen-space effects, IllusionRP uses the <b>Volume System</b> for control and <b>RendererFeatures</b> for master switches.

# Shader Details

Introduction to IllusionRP shader details.

## HD Shaders

As a sample, IllusionRP provides `HD Skin`, `HD Hair`, `HD Fabric` and `HD Lit` shader which use HDRP surface properties. 

These HD Shaders serve as reference implementations for developers, demonstrating how to create character shaders using IllusionRP. 

> Since these shaders use HDRP surface properties, you can now convert HDRP materials to IllusionRP shaders directly.

## Stencil Mask

Here are the stencil masks used in shader's Forward / ForwardGBuffer passes.

| Ref      | Usage                              |
| -------- | :--------------------------------: |
| 0001     | Skin                               |
| 0010     | Hair                               |
| 0100     | Receive Screen Space Reflection    |

In DepthOnly / DepthNormal passes, following stencil mask is used.

| Ref      | Usage                           |
| -------- | :-----------------------------: |
| 0001     | Not Receive Ambient Occlusion   |

## OIT Hair

`Order Independent Transparency` is a technique that allows transparent objects to be drawn in any order, and the order in which they are drawn does not affect the transparency of the object.

However, it may cause incorrect blending if the object is not actually transparent, see [Matt - Weighted Blended Order-Independent Transparency](https://therealmjp.github.io/posts/weighted-blended-oit/). 

Thus, IllusionRP only use OIT when needed, and the other transparent objects will still be drawn by urp transparent pass using render queue.

To be compatible with common transparent objects, IllusionRP draw OIT objects after transparent objects, here is a sample workflow for rendering OIT hair:

1. Draw opaque part of hair.
2. Draw transparent objects.
3. Draw OIT transparent part of hair.

> [!TIP]
> In this order, common transparent objects will always be drawn down to OIT transparent objects, we can solve this by enable `Transparent Depth Post Pass` and `OIT Transparent Overdraw Pass` to write depth before draw pass and overdraw transparent objects on top of oit objects. To overdraw only the OIT area, we need to set `OIT Stencil` to `0010 (2)`.

![OIT Overdraw Transparent](./images/oit_overdraw_transparent.png)

> For more techniques, please refer to [如何在Unity URP中让头发渲染更丝滑](https://zhuanlan.zhihu.com/p/1907549925065070387).

## Dithering Hair

Beside of OIT hair, IllusionRP also keeps a <b>Dithering</b> version which is more common in modern games.

You can switch OIT and Dithering mode in ASE `Additional Options/Multi Pass`.

![Hair Mode](./images/hair_mode.png)

If you don't need OIT at all, you can disable OIT by turn off `Illusion Graphics/Order Independent Transparency` in active `UniversalRendererData` to remove related passes.

> [!TIP]
> When use <b>Dithering</b> hair, you need to enable <b>Temporal Anti-Aliasing</b> in camera settings.

## Forward GBuffer

Since IllusionRP uses screen space reflection in forward rendering path, shader needs to have `ForwardGBuffer` pass.

> For more techniques, please refer to [UPR Forward渲染路径下的Screen Space Reflection实践¶](https://zhuanlan.zhihu.com/p/1912828657585590857).

# Shader Template Details

Introduction to all customizable shader input/output options for ASE template files in IllusionRP, helping developers understand and use these templates.

## Available Templates


| Template | Options | Lighting Features |
|----------|----------------|---------------------------|
| **Hair Template** | • **Diffuse Attenuation**: Kajiya/Uncharted<br>• **Shading Model**: Kajiya/Marschner<br>• **Multi Pass**: Enables order-independent transparency rendering<br>| • **Kajiya-Kay Shading**: High-performance hair shading<br>• **Marschner Shading**: Physically-based hair model<br>• **Backlight Scattering**: Volumetric scattering effect<br>• **Order Independent Transparency**: For multi-layer hair rendering |
| **Skin Template** | | • **Screen Space Subsurface Scattering**: High-quality subsurface scattering<br>• **Spherical Gaussian SSS**: Performance-optimized subsurface scattering<br>• **Dual Lobe Specular**: Natural skin specular reflection |
| **Fabric Template** | | • **Anisotropy Specular**: Directional specular reflections<br>• **Sheen Scattering**: Ashikhmin/Charlie sheen models<br>• **Oren-Nayar Diffuse**: Physically-based diffuse lighting |
| **Hybrid Lit Template** | | • **Screen Space Reflection**: Real-time reflections<br>• **PreIntegrated IBL**: Advanced image-based lighting<br>• **Screen Space Ambient Occlusion**: High-quality ambient occlusion<br>|

## Shader Stripping

IllusionRP will strip shader variants not used in order to reduce shader compilation time and build size. For example, if `Screen Space Subsurface Scattering` is enabled in all renderer features in the project, the off variant will be stripped.

To disable shader stripping, you can follow these steps:

1. Open Project Settings > Graphics > IllusionRP Global Settings.
2. Uncheck `Strip Unused Variants`.

![Shader Stripping](./images/shader-stripping.png)