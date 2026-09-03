# UnityRHI

An NVRHI-shaped render hardware interface for Unity on **Direct3D12**. Commands
are recorded into a command stream that replays
directly onto Unity's own D3D12 command list, which exposes ray tracing,
bindless resources, compute and explicit resource barriers that the engine does
not surface itself.

This folder is the UPM **C# source** (`top.kuanmi.unityrhi`). Native binaries
live in the sibling package `top.kuanmi.unityrhi.native` and are not committed.
Download the prebuilt native package from the
[latest GitHub Release](https://github.com/Kuan-Mi/UnityDLSSNR/releases/latest),
or run `1-Deploy.bat` / `2-Build.bat` / `3-Pack.bat` from the repository root
to build it from source. Native plugin sources live one level up, in
`RenderingPlugin/`.

## Requirements

- Direct3D12 must be the active graphics API. Set **Player Settings → Other
  Settings → Graphics APIs for Windows** to Direct3D12 and restart the editor;
  the plugin logs a warning and disables itself on any other API.
- Windows x64. The native plugins are Windows-only.

## Contents

| Folder | What it is |
|---|---|
| `Runtime` | Device, resources, desc types, shader assets, domain-reload hooks |
| `Runtime/Commands` | Command-stream recording, wire ABI, and decode |
| `Runtime/Interop` | P/Invoke for `UnityRHI.dll` |
| `Runtime/Shaders` | `RhiShader` / variant assets consumed at runtime |
| `Runtime/Dlrr` | In-process DLSS Ray Reconstruction (`DlrrContext`) |
| `Runtime/NRD` | NRIPlugin wrappers for NVIDIA Real-time Denoisers |
| `Runtime/DLSR` | NRIPlugin wrappers for DLSS Super Resolution |
| `Editor` | Debug and frame-debugger windows |
| `Editor/Shaders` | `.rhishader` importer, include tracking, variant JIT, player validation |

Native DLLs (`UnityRHI.dll` and everything it links or loads) are in
`top.kuanmi.unityrhi.native`. This package depends on that one.

DLSS-RR used by current renderers goes through `Runtime/Dlrr` and
`UnityRHI`. `Runtime/NRD` and `Runtime/DLSR` talk to `NRIPlugin.dll`
instead and are kept for a possible later integration; they are not on the
live RTXPT path.

## Shaders

Shader programs are configured by `.rhishader` importers. Each declared program
is emitted as a stable, directly referenceable `RhiShader` sub-asset:

```
[SerializeField] private RhiShader bloom;

RhiShaderVariant variant = bloom.CreateVariant();
variant.SetKeyword("BLOOM_HIGH_QUALITY", true);
byte[] dxil = variant.GetBytecode();
```

Source, include directories, entry point, target profile and compile options are
serialized in the importer's `.meta`. Rendering code references the required
`RhiShader` directly; there is no shader-set registry or string program lookup.

Compiled bytecode lives only in the import artifact under `Library/Artifacts`.
Nothing is generated into `Assets/`. Rebuilds, include-dependency tracking, and
cache invalidation are the asset database's job: editing an `.hlsli` reimports
exactly the manifests that include it, and rebuilding the native plugin or
swapping the bundled DXC invalidates every program through a registered custom
dependency.

Each caller creates an independent `RhiShaderVariant` and selects its keyword
values with `SetKeyword`. Editor-only combinations compile on demand into
`Library/UnityRhi/VariantJit` without mutating the asset. Player variants are
collected from directly referenced shaders by build variant providers, with
`multi_compile` dimensions expanded before import.

## Ray tracing

For the RTX Path Tracing renderer built on this package, see
`top.kuanmi.rtxpt`.

---

# 中文翻译

# UnityRHI

这是一个面向 Unity、运行于 **Direct3D12** 上且采用 NVRHI 风格的渲染硬件接口。命令会被记录到命令流中，再直接重放到 Unity 自身的 D3D12 命令列表上，从而提供引擎本身未公开的光线追踪、无绑定资源、计算以及显式资源屏障能力。

此文件夹是 UPM **C# 源码**包（`top.kuanmi.unityrhi`）。原生二进制文件位于同级包 `top.kuanmi.unityrhi.native` 中，且不会提交到仓库。可以从[最新 GitHub Release](https://github.com/Kuan-Mi/UnityDLSSNR/releases/latest)下载预编译原生包，也可以从仓库根目录依次运行 `1-Deploy.bat`、`2-Build.bat` 和 `3-Pack.bat`，自行从源码构建。原生插件源码位于上一级的 `RenderingPlugin/` 中。

## 要求

- Direct3D12 必须是当前图形 API。将 **Player Settings → Other Settings → Graphics APIs for Windows** 设置为 Direct3D12 并重启编辑器；使用其他 API 时，插件会记录警告并自行禁用。
- Windows x64。原生插件仅支持 Windows。

## 内容

| 文件夹 | 用途 |
|---|---|
| `Runtime` | 设备、资源、描述类型、着色器资源和域重载钩子 |
| `Runtime/Commands` | 命令流记录、线格式 ABI 和解码 |
| `Runtime/Interop` | `UnityRHI.dll` 的 P/Invoke 接口 |
| `Runtime/Shaders` | 运行时使用的 `RhiShader`／变体资源 |
| `Runtime/Dlrr` | 进程内 DLSS 光线重建（`DlrrContext`） |
| `Runtime/NRD` | NVIDIA Real-time Denoisers 的 NRIPlugin 包装器 |
| `Runtime/DLSR` | DLSS 超分辨率的 NRIPlugin 包装器 |
| `Editor` | 调试窗口和帧调试器窗口 |
| `Editor/Shaders` | `.rhishader` 导入器、include 跟踪、变体 JIT 和 Player 验证 |

原生 DLL（`UnityRHI.dll` 及其链接或加载的所有内容）位于 `top.kuanmi.unityrhi.native` 中。本包依赖该包。

当前渲染器所使用的 DLSS-RR 通过 `Runtime/Dlrr` 和 `UnityRHI` 运行。`Runtime/NRD` 与 `Runtime/DLSR` 则与 `NRIPlugin.dll` 通信，并为将来可能的集成而保留；它们不在当前 RTXPT 执行路径上。

## 着色器

着色器程序通过 `.rhishader` 导入器配置。每个声明的程序都会生成为一个稳定、可直接引用的 `RhiShader` 子资源：

```
[SerializeField] private RhiShader bloom;

RhiShaderVariant variant = bloom.CreateVariant();
variant.SetKeyword("BLOOM_HIGH_QUALITY", true);
byte[] dxil = variant.GetBytecode();
```

源码、include 目录、入口点、目标配置和编译选项会序列化到导入器的 `.meta` 中。渲染代码会直接引用所需的 `RhiShader`；不存在着色器集合注册表或通过字符串查找程序的机制。

编译后的字节码只存在于 `Library/Artifacts` 下的导入产物中，不会向 `Assets/` 生成任何内容。重新构建、include 依赖项跟踪和缓存失效均由资源数据库负责：编辑一个 `.hlsli` 时，只会重新导入包含它的清单；重新构建原生插件或替换随附的 DXC 时，则会通过已注册的自定义依赖项使所有程序失效。

每个调用方都会创建独立的 `RhiShaderVariant`，并使用 `SetKeyword` 选择其关键字值。仅用于编辑器的组合会按需编译到 `Library/UnityRhi/VariantJit`，而不会修改资源。Player 变体由构建变体提供程序从直接引用的着色器中收集，并在导入前展开 `multi_compile` 维度。

## 光线追踪

有关基于本包构建的 RTX Path Tracing 渲染器，请参阅 `top.kuanmi.rtxpt`。
