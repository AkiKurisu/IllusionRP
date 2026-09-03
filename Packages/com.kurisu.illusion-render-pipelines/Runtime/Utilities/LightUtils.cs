using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    public class LightUtils
    {
        private static float s_LuminanceToEvFactor => Mathf.Log(100f / ColorUtils.s_LightMeterCalibrationConstant, 2);

        private static float s_EvToLuminanceFactor => -Mathf.Log(100f / ColorUtils.s_LightMeterCalibrationConstant, 2);
        
        /// <summary>
        /// Convert EV100 to Luminance(nits)
        /// </summary>
        /// <param name="ev"></param>
        /// <returns></returns>
        public static float ConvertEvToLuminance(float ev)
        {
            return Mathf.Pow(2, ev + s_EvToLuminanceFactor);
        }
    }
}