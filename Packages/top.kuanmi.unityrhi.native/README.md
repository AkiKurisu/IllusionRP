# UnityRHI Native

Windows x64 native plugins for [`top.kuanmi.unityrhi`](../top.kuanmi.unityrhi).
This package has no C# — only `Plugins/x86_64`.

Prebuilt DLLs are **not committed** to the repository. Download the ready-to-use
native package from the
[latest GitHub Release](https://github.com/Kuan-Mi/UnityDLSSNR/releases/latest),
or run `1-Deploy.bat` / `2-Build.bat` / `3-Pack.bat` from the repository root
to build it from source. Pack writes binaries into
`Build/top.kuanmi.unityrhi.native`.

## Add to a Unity project

Add **both** packages. The C# package depends on this one. Download and extract
the prebuilt package into the target project's `Packages` folder, or copy the
packed `Build/top.kuanmi.unityrhi.native` tree there, or reference it with a
`file:` entry. The native runtime uses the D3D12 Agility SDK that Unity ships
(1.618 in Unity 6.3), so the package needs no particular location.

Point `top.kuanmi.unityrhi` at `Packages/top.kuanmi.unityrhi` as appropriate
for the target project.

After a native rebuild, replace the contents of the native package the target
project uses. The C# package can stay as a `file:` reference to
`Packages/top.kuanmi.unityrhi`.

## Contents

`UnityRHI.dll` statically imports `NRI.dll` and loads `D3D12HeapHook.dll`,
`dxcompiler.dll`, `dxil.dll` and the NGX/DLSS SR, RR and FG libraries from its own directory —
they must stay side by side in `Plugins/x86_64`.


---

# 中文翻译

# UnityRHI Native

这是 [`top.kuanmi.unityrhi`](../top.kuanmi.unityrhi) 的 Windows x64 原生插件包。本包不含 C#，只有 `Plugins/x86_64`。

预构建 DLL **不会提交**到仓库。可以从[最新 GitHub Release](https://github.com/Kuan-Mi/UnityDLSSNR/releases/latest)下载可直接使用的原生包，也可以从仓库根目录依次运行 `1-Deploy.bat`、`2-Build.bat` 和 `3-Pack.bat`，自行从源码构建。打包操作会将二进制文件写入 `Build/top.kuanmi.unityrhi.native`。

## 添加到 Unity 项目

请同时添加这**两个**包。C# 包依赖本包。下载预编译包并解压到目标项目的 `Packages` 文件夹，将打包后的 `Build/top.kuanmi.unityrhi.native` 目录树复制到该文件夹，或者用 `file:` 条目引用它。原生运行时使用 Unity 自带的 D3D12 Agility SDK（Unity 6.3 为 1.618），因此原生包放在哪里都可以。

根据目标项目的情况，将 `top.kuanmi.unityrhi` 指向 `Packages/top.kuanmi.unityrhi`。

重新构建原生部分后，请替换目标项目所用的原生包的内容。C# 包可继续使用指向 `Packages/top.kuanmi.unityrhi` 的 `file:` 引用。

## 内容

`UnityRHI.dll` 会静态导入 `NRI.dll`，并从自身目录加载 `D3D12HeapHook.dll`、`dxcompiler.dll`、`dxil.dll` 以及 NGX/DLSS SR、RR 和 FG 库——它们必须并列放置在 `Plugins/x86_64` 中。
