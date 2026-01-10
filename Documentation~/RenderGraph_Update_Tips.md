# 升级RenderGraph的注意事项

IllusionRP中来自HDRP的Feature是我手工从RDG版本翻译成传统SRP版本，一开始我以为要接入RenderGraph直接从HDRP再Copy一下就行，结果遇到超多坑点。

总结而言HDRP的RDG是落后于URP的，URP中为了更好的优化移动端性能，对RenderPass进行了拆分，例如ComputeRenderPass和RasterRenderPass，这样方便Compiler处理RenderPass合并逻辑（CS当然就不会有合并），下面是对常见迁移问题的总结。

## AddRenderPass无法使用 

如果直接拷贝HDRP的RenderPass写法，会有下面的报错：

`Exception: Pass 'XXX' is using the legacy rendergraph API. You cannot use legacy passes with the native render pass compiler. Please do not use AddPass on the rendergrpah but use one of the more specific pas types such as AddRasterPass.`

例如HDRP中的Depth Pyramid：

```csharp
using (var builder = renderGraph.AddRenderPass<DepthPyramidPassData>("Depth Pyramid", out var passData, ProfilingSampler.Get(HDProfileId.DepthPyramid)))
{
    passData.DepthPyramidTexture = builder.WriteTexture(depthPyramidHandle);
    passData.MipGenerator = _rendererData.MipGenerator;
    passData.MipChainInfo = _mipChainInfo;

    builder.SetRenderFunc((DepthPyramidPassData data, RenderGraphContext context) =>
    {
        data.MipGenerator.RenderMinDepthPyramid(context.cmd, data.DepthPyramidTexture, data.MipChainInfo);
    });
}
```

我们得指定是LowLevel（用于灵活配置）、Raster（片元着色，面向Fragment Shader）还是Compute（计算，面向ComputeShader），修改后如下：

```csharp
using (var builder = renderGraph.AddComputePass<DepthPyramidPassData>("Depth Pyramid", out var passData, DepthPyramidSampler))
{
    passData.DepthPyramidTexture = builder.UseTexture(depthPyramidHandle, IBaseRenderGraphBuilder.AccessFlags.Write);
    passData.MipGenerator = _rendererData.MipGenerator;
    passData.MipChainInfo = _mipChainInfo;

    builder.AllowPassCulling(false);
    builder.AllowGlobalStateModification(true); // 有Keyword切换，需要标识

    builder.SetRenderFunc((DepthPyramidPassData data, ComputeGraphContext context) =>
    {
        data.MipGenerator.RenderMinDepthPyramid(context.cmd, data.DepthPyramidTexture, data.MipChainInfo);
    });
}
```

## Texture2D的导入

RenderGraph的 `ImportTexture()` 只接受 `RTHandle` 参数，但很多资源（如debug字体、遮罩纹理等）是外部配置，通常是 `Texture2D` 类型。

例如：
```csharp
// 错误：Texture2D无法直接导入
passData.debugFontTex = renderGraph.ImportTexture(_rendererData.RuntimeResources.debugFontTex);
```

解决方法是使用 `RTHandles.Alloc(Texture)` 包装后再导入
```csharp
// 正确：先用RTHandles.Alloc包装Texture2D
RTHandle debugFontRTHandle = RTHandles.Alloc(_rendererData.RuntimeResources.debugFontTex);
passData.debugFontTex = renderGraph.ImportTexture(debugFontRTHandle);
```

虽然RTHandle的创建不会多创建RenderTexture，但也需要关注其本身的Allocation开销。

对于需要包装的自定义纹理，应该在Pass中缓存RTHandle，并在Dispose时释放：


IllusionRP对于常用的默认纹理（如 `Texture2D.whiteTexture`、`Texture2D.blackTexture`等），会在 `IllusionRendererData` 中统一管理：

```csharp
// 在IllusionRendererData中
private RTHandle _whiteTextureRTHandle;

public RTHandle GetWhiteTextureRT()
{
    if (_whiteTextureRTHandle == null)
    {
        _whiteTextureRTHandle = RTHandles.Alloc(Texture2D.whiteTexture);
    }
    return _whiteTextureRTHandle;
}

// 在Dispose中释放
public void Dispose()
{
    RTHandles.Release(_whiteTextureRTHandle);
    _whiteTextureRTHandle = null;
    // ...
}
```

## SetGlobalTexture的使用

使用RDG后，`SetGlobalTexture`这个普遍的操作变得麻烦了起来，下面是个示例

```csharp
var currentExposureRT = _rendererData.GetExposureTexture();
passData.currentExposureTexture = builder.UseTexture(renderGraph.ImportTexture(currentExposureRT));

builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
{
    context.cmd.SetGlobalTexture(ShaderIDs._ExposureTexture, data.currentExposureTexture);
});
```

> [!TIP]
> 需要添加 `builder.AllowGlobalStateModification(true)` 以允许设置全局状态。

## Raster和Compute分离

材质的绘制操作（如DrawProcedural、Blit）不能在ComputePass中执行。这也是和HDRP中使用Legacy Graph Compiler的差异之一。

例如自动曝光这里在第一帧需要先计算直方图后直接用Fragment Shader应用到RenderTarget上，URP下需要将后者操作分离到独立的RasterPass。
```csharp
// Pass 1: ComputePass - 计算曝光值
using (var builder = renderGraph.AddComputePass<ExposurePassData>(...))
{
    // 只做计算着色器操作
}

// Pass 2: RasterPass - 应用曝光（如果需要）
using (var builder = renderGraph.AddRasterRenderPass<ApplyExposurePassData>(...))
{
    passData.destination = builder.UseTextureFragment(destTexture, 0);
    
    builder.SetRenderFunc((ApplyExposurePassData data, RasterGraphContext context) =>
    {
        // 正确：在RasterPass中使用材质
        data.material.SetTexture(...);
        context.cmd.DrawProcedural(...);
    });
}
```

## OnCameraSetup不会调用

`OnCameraSetup` 在RenderGraph模式下会被跳过。

如果需要CommandBuffer可以使用LowLevelPass，不需要的直接移到RecordRenderGraph即可。

例如：

```csharp
// 提取为独立方法
private void PrepareExposureData(ref RenderingData renderingData)
{
    _exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
    PrepareExposureCurveData();
    // ... 传统路径逻辑
}

// Unity 2022传统路径
public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
{
    PrepareExposureData(ref renderingData);
}

#if UNITY_2023_1_OR_NEWER
// Unity 2023 RenderGraph路径
public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
{
    PrepareExposureData(ref renderingData);
    // ... RenderGraph逻辑
}
#endif
```

## RenderGraph.CreateTexture的管理

使用`RenderGraph.CreateTexture`创建的纹理在最后一次被使用后（计数管理），在TextureDesc没有标记需要discard的情况下，可以被RenderGraph中的其他Pass复用（这也是为什么需要有TextureHandle再次封装RTHandle的原因之一，这样能有效减少需要Allocate的RT数量）。

但需要注意如果该纹理需要被`SetGlobalTexture`隐式给Lighting Pass使用，应该使用唯一的RTHandle进行管理，否则就会被其他Pass错误写入。

下面代码就是在某些情况下遇到了屏幕空间反射的结果`SsrLightingTexture`被SSGI中`Intermediate Texture`错误写入，故相较于HDRP进行了调整。

```csharp
// Create transient textures for hit points and lighting
TextureHandle hitPointTexture = renderGraph.CreateTexture(new TextureDesc(_rtWidth, _rtHeight, false, false)
{
    colorFormat = GraphicsFormat.R16G16_UNorm,
    clearBuffer = !useAsyncCompute,
    clearColor = Color.clear,
    enableRandomWrite = _tracingInCS,
    name = "SSR_HitPoint_Texture"
});

// @IllusionRP: 
// Notice if we use RenderGraph.CreateTexture, ssr lighting texture may be re-used before lighting.
// So we should always use SsrAccum(RTHandle) instead of SsrLighting in RenderGraph.
// TextureHandle ssrLightingTexture = renderGraph.CreateTexture(new TextureDesc(_rtWidth, _rtHeight, false, false)
// {
//     colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
//     clearBuffer = !useAsyncCompute && !_needAccumulate,
//     clearColor = Color.clear,
//     enableRandomWrite = _reprojectInCS || _needAccumulate,
//     name = "SSR_Lighting_Texture"
// });

// Clear operations for async compute or PBR accumulation
var ssrAccumRT = _rendererData.GetCurrentFrameRT((int)IllusionFrameHistoryType.ScreenSpaceReflectionAccumulation);
TextureHandle ssrAccum = renderGraph.ImportTexture(ssrAccumRT);
ClearTexturePass(renderGraph, ssrAccum, Color.clear, useAsyncCompute);

// @IllusionRP: Always use SSrAccum
TextureHandle ssrLightingTexture = ssrAccum;
```