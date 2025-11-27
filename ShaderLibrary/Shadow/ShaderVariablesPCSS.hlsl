// Reference: https://github.com/recaeee/RecaNoMaho_P
#ifndef PCSS_VARIABLES_INCLUDED
#define PCSS_VARIABLES_INCLUDED

#define PCSS_PENUMBRA_MASK 1

// Params
float4 _DirLightPcssParams0[4];
float4 _DirLightPcssParams1[4];
float4 _DirLightPcssProjs[4];
float4 _CascadeOffsetScales[4];
float _FindBlockerSampleCount;
float _PcfSampleCount;

// Penumbra Mask Optimization
float4 _PenumbraMaskTexelSize;
float4 _ColorAttachmentTexelSize;
TEXTURE2D(_PenumbraMaskTex);
float4 _PenumbraMaskTex_TexelSize;
#endif