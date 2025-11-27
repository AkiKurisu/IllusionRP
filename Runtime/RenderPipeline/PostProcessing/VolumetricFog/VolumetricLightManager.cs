using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PostProcessing
{
    public class VolumetricLightManager
    {
        private static readonly Dictionary<Light, VolumetricAdditionalLight> Lights = new();

        internal VolumetricLightManager()
        {
            
        }

        public static bool TryGetVolumetricAdditionalLight(Light light, out VolumetricAdditionalLight volumetricAdditionalLight)
        {
            return Lights.TryGetValue(light, out volumetricAdditionalLight);
        }

        public static void RegisterVolumetricLight(Light light, VolumetricAdditionalLight additionalLight)
        {
            Lights.Add(light, additionalLight);
        }
        
        public static void UnregisterVolumetricLight(Light light, VolumetricAdditionalLight additionalLight)
        {
            if (!Lights.TryGetValue(light, out var currentLight) || currentLight != additionalLight) return;
            Lights.Remove(light);
        }
    }
}