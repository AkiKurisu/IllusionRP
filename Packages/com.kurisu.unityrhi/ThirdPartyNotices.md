# Third-party notices

This package redistributes or derives from the following components. Each
keeps its original license. SDK trees are fetched at build time (CMake
FetchContent / NuGet), not committed.

| Component | Location | License |
|---|---|---|
| NVRHI (API/design port) | native D3D12 backend | MIT — `ThirdParty/NVRHI-LICENSE.txt` |
| D3D12 Agility SDK | fetched (`1.619.1`); packed into `com.kurisu.unityrhi` | Microsoft Software License Terms |
| DirectX-Headers | fetched (`v1.717.0-preview`) | MIT |
| DirectX Shader Compiler | fetched (`v1.9.2602`), shipped in `Plugins/x86_64` | LLVM + Microsoft |
| NVAPI | linked into the native plugin; shader headers in `ShaderLibrary/NVAPI` | NVIDIA (headers retain their license notices) |
| NRD, NRI, DLSS/NGX runtimes | fetched, shipped in `Plugins/x86_64` | NVIDIA RTX SDKs license |
| DX12BindlessUnity (patched) | fetched (`ae06d54`), built into `GfxPluginIllusionRHI.dll` | MIT |
| MinHook | fetched (`8af6b4a`), built into `GfxPluginIllusionRHI.dll` | BSD-2-Clause (Hacker Disassembler Engine: BSD-2-Clause) |
