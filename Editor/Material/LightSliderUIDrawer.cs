using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    internal static class LightSliderUIDrawer
    {
        private static readonly PiecewiseLightUnitSlider ExposureSlider;

        static LightSliderUIDrawer()
        {
            // Exposure is in EV100, but we load a separate due to the different icon set.
            ExposureSlider = new PiecewiseLightUnitSlider(LightUnitSliderDescriptors.ExposureDescriptor);
        }

        // Need to cache the serialized object on the slider, to add support for the preset selection context menu (need to apply changes to serialized)
        public static void SetSerializedObject(SerializedObject serializedObject)
        {
            ExposureSlider.SetSerializedObject(serializedObject);
        }
        
        public static void DrawExposureSlider(SerializedProperty value, Rect rect)
        {
            using (new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel))
            {
                float val = value.floatValue;
                ExposureSlider.Draw(rect, value, ref val);
                if (val != value.floatValue)
                    value.floatValue = val;
            }
        }
    }
}