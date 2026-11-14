using UnityEngine;

namespace Illusion.Rendering
{
    public static class HybridLitMaterialState
    {
        public static bool SynchronizePasses(Material material)
        {
            if (!material.HasProperty("_Surface"))
                return false;
            bool transparent = (int)material.GetFloat("_Surface") == 1;
            bool orderIndependent = transparent
                && material.HasProperty(IllusionShaderProperties.OrderIndependent)
                && Mathf.Approximately(material.GetFloat(IllusionShaderProperties.OrderIndependent), 1f);
            bool changed = material.GetShaderPassEnabled("ForwardGBuffer") != !transparent
                || material.GetShaderPassEnabled("OIT") != orderIndependent
                || material.GetShaderPassEnabled("UniversalForward") != !orderIndependent
                || material.GetShaderPassEnabled("UniversalForwardOnly") != !orderIndependent;
            if (changed)
            {
                material.SetShaderPassEnabled("ForwardGBuffer", !transparent);
                material.SetShaderPassEnabled("OIT", orderIndependent);
                material.SetShaderPassEnabled("UniversalForward", !orderIndependent);
                material.SetShaderPassEnabled("UniversalForwardOnly", !orderIndependent);
            }
            return changed;
        }
    }
}
