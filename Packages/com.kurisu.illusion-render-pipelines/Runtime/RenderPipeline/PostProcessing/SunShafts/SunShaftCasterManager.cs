using System.Collections.Generic;
using UnityEngine;

namespace Illusion.Rendering.PostProcessing
{
    public class SunShaftCasterManager
    {
        private static readonly List<SunShaftCaster> Casters = new();

        internal SunShaftCasterManager()
        {
        }

        public static void RegisterCaster(SunShaftCaster caster)
        {
            if (!Casters.Contains(caster))
            {
                Casters.Add(caster);
            }
        }

        public static void UnregisterCaster(SunShaftCaster caster)
        {
            Casters.Remove(caster);
        }

        public Transform GetActiveCaster()
        {
            for (int i = Casters.Count - 1; i >= 0; i--)
            {
                var caster = Casters[i];
                if (caster && caster.isActiveAndEnabled)
                {
                    return caster.transform;
                }
            }

            return null;
        }
    }
}
