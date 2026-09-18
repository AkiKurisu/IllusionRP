using UnityEngine;

namespace Illusion.Rendering.PostProcessing
{
    /// <summary>
    /// Marks the transform sun shafts radiate from.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Illusion/Sun Shaft Caster")]
    public sealed class SunShaftCaster : MonoBehaviour
    {
        private void OnEnable()
        {
            SunShaftCasterManager.RegisterCaster(this);
        }

        private void OnDisable()
        {
            SunShaftCasterManager.UnregisterCaster(this);
        }
    }
}
