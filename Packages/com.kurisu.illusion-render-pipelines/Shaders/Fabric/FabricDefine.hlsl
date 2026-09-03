// ============================ Shader Define for Fabric =============================== //
// Unified Translucency effect strength for fabric
#define TRANSLUCENCY_STRENGTH                   1

struct AnisotropyData
{
    half3 T;
    half3 B;
    half Anisotropy;
};

struct SheenData
{
    half3 Color;
    half3 N;
    half Sheen;
};
// ============================ Shader Define for Fabric =============================== //