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
packed `Build/top.kuanmi.unityrhi.native` tree there. The final path must be
`Packages/top.kuanmi.unityrhi.native`. The native package must be an embedded
package under the project's `Packages` folder; it contains the preload function
that must run before Unity creates the D3D12 device. Do not reference the
external packed `Build` directory directly with a `file:` entry.

Point `top.kuanmi.unityrhi` at `Packages/top.kuanmi.unityrhi` as appropriate
for the target project. Unity discovers the embedded native package directly
from its `Packages/top.kuanmi.unityrhi.native` folder.

After a native rebuild, replace the contents of the target project's embedded
`Packages/top.kuanmi.unityrhi.native` package. The C# package can stay as a
`file:` reference to `Packages/top.kuanmi.unityrhi`.

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

请同时添加这**两个**包。C# 包依赖本包。下载预编译包并解压到目标项目的 `Packages` 文件夹，或者将打包后的 `Build/top.kuanmi.unityrhi.native` 目录树复制到该文件夹。最终路径必须为 `Packages/top.kuanmi.unityrhi.native`。原生包必须作为嵌入式包放在项目的 `Packages` 文件夹下；其中包含必须在 Unity 创建 D3D12 设备之前执行的预加载函数。不要通过 `file:` 条目直接引用项目外已打包的 `Build` 目录。

根据目标项目的情况，将 `top.kuanmi.unityrhi` 指向 `Packages/top.kuanmi.unityrhi`。Unity 会直接从 `Packages/top.kuanmi.unityrhi.native` 文件夹发现嵌入式原生包。

重新构建原生部分后，请替换目标项目中嵌入式 `Packages/top.kuanmi.unityrhi.native` 包的内容。C# 包可继续使用指向 `Packages/top.kuanmi.unityrhi` 的 `file:` 引用。

## 内容

`UnityRHI.dll` 会静态导入 `NRI.dll`，并从自身目录加载 `D3D12HeapHook.dll`、`dxcompiler.dll`、`dxil.dll` 以及 NGX/DLSS SR、RR 和 FG 库——它们必须并列放置在 `Plugins/x86_64` 中。
