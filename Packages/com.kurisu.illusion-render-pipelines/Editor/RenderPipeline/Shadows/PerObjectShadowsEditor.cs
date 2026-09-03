using Illusion.Rendering.Shadows;
using UnityEditor;
using UnityEditor.Rendering;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PerObjectShadows))]
    internal sealed class PerObjectShadowsEditor : VolumeComponentEditor
    {
        private SerializedDataParameter _perObjectShadowDepthBits;
        private SerializedDataParameter _perObjectShadowTileResolution;
        private SerializedDataParameter _adaptiveTileResolution;
        private SerializedDataParameter _adaptiveAtlasResolution;
        private SerializedDataParameter _maximumAdaptiveTileResolution;
        private SerializedDataParameter _perObjectShadowLengthOffset;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<PerObjectShadows>(serializedObject);

            _perObjectShadowDepthBits = Unpack(o.Find(x => x.perObjectShadowDepthBits));
            _perObjectShadowTileResolution = Unpack(o.Find(x => x.perObjectShadowTileResolution));
            _adaptiveTileResolution = Unpack(o.Find(x => x.adaptiveTileResolution));
            _adaptiveAtlasResolution = Unpack(o.Find(x => x.adaptiveAtlasResolution));
            _maximumAdaptiveTileResolution = Unpack(o.Find(x => x.maximumAdaptiveTileResolution));
            _perObjectShadowLengthOffset = Unpack(o.Find(x => x.perObjectShadowLengthOffset));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(_perObjectShadowDepthBits, EditorGUIUtility.TrTextContent("Depth Bits", "Sets the depth buffer precision for the per-object shadow map."));
            PropertyField(_adaptiveTileResolution, EditorGUIUtility.TrTextContent("Adaptive Tile Resolution", "Distributes a fixed atlas budget from caster screen coverage and priority."));
            if (_adaptiveTileResolution.value.boolValue)
            {
                PropertyField(_adaptiveAtlasResolution, EditorGUIUtility.TrTextContent("Adaptive Atlas Resolution", "Sets the fixed atlas budget used by adaptive allocation."));
                PropertyField(_maximumAdaptiveTileResolution, EditorGUIUtility.TrTextContent("Maximum Adaptive Tile Resolution", "Limits the resolution requested by one caster."));
            }
            else
            {
                PropertyField(_perObjectShadowTileResolution, EditorGUIUtility.TrTextContent("Tile Resolution", "Sets the fixed resolution for each tile in the per-object shadow atlas."));
            }
            PropertyField(_perObjectShadowLengthOffset, EditorGUIUtility.TrTextContent("Shadow Length Offset", "Controls the offset distance for shadow length calculation."));
        }
    }
}

