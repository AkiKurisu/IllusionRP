# Third-party notices

This package redistributes or derives from the following components. Each
keeps its original license. SDK trees are fetched at build time (CMake
FetchContent / NuGet), not committed.

| Component | Location | License |
|---|---|---|
| NVRHI (API/design port) | native D3D12 backend | MIT — `ThirdParty/NVRHI-LICENSE.txt` |
| DirectX-Headers | fetched (`v1.717.0-preview`) | MIT |
| DirectX Shader Compiler | fetched (`v1.9.2602`), shipped in `Plugins/x86_64` | LLVM + Microsoft |
| NVAPI | fetched (NVIDIA/nvapi), linked, not redistributed as a DLL | NVIDIA |
| NRD, NRI, DLSS/NGX runtimes | fetched, shipped in `Plugins/x86_64` | NVIDIA RTX SDKs license |
| DX12BindlessUnity (patched) | fetched (`ae06d54`), built into `GfxPluginUnityRhiBindless.dll` | MIT |
| MinHook | fetched (`8af6b4a`), built into `GfxPluginUnityRhiBindless.dll` | BSD-2-Clause (Hacker Disassembler Engine: BSD-2-Clause) |
