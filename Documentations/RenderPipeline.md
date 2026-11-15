# Render Pipeline

Passes are ordered by `IllusionRenderPassEvent`. SSGI and RTGI are alternatives: an active RTGI Volume replaces SSGI on that camera.

```mermaid
flowchart TD
  subgraph Depth
    ForwardGBuffer
    CopyPreDepth
    DepthPyramid
    TransparentDepth
  end

  subgraph ScreenSpace
    GTAO
    SSR
  end

  subgraph Shadows
    PerObjectShadows
    ContactShadows
    AreaLightShadows
    ScreenSpaceShadows
  end

  subgraph Indirect
    PRTRelight
    SSGI
    RTGI
  end

  subgraph AfterOpaque
    Subsurface
    RefractionColor
    TransparentSSR
  end

  subgraph AfterTransparent
    OIT
    TransparentOverdraw
    ColorPyramid
  end

  subgraph BeforePostProcessing
    SunShafts
    VolumetricFog
    Exposure
    Bloom
  end

  subgraph AfterPostProcessing
    DLSSNeuralRendering
  end

  ForwardGBuffer --> CopyPreDepth --> DepthPyramid --> TransparentDepth --> GTAO --> SSR
  SSR --> PerObjectShadows --> ContactShadows --> AreaLightShadows --> PRTRelight
  PRTRelight --> SSGI --> RTGI --> ScreenSpaceShadows --> Subsurface
  Subsurface --> RefractionColor --> TransparentSSR --> OIT --> TransparentOverdraw --> ColorPyramid
  ColorPyramid --> SunShafts --> VolumetricFog --> Exposure --> Bloom --> DLSSNeuralRendering
```

An active Path Tracing Volume replaces this chain for that camera and runs exposure and convolution bloom.

## Limitations

`Native Render Pass` allows fewer than 20 custom passes (`ScriptableRenderer.kRenderPassMaxCount`). IllusionRP exceeds that. Leave **Native Render Pass** disabled on the renderer asset after enabling IllusionRP.
