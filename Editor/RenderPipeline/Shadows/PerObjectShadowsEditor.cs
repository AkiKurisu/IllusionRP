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
        private SerializedDataParameter _perObjectShadowLengthOffset;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<PerObjectShadows>(serializedObject);

            _perObjectShadowDepthBits = Unpack(o.Find(x => x.perObjectShadowDepthBits));
            _perObjectShadowTileResolution = Unpack(o.Find(x => x.perObjectShadowTileResolution));
            _perObjectShadowLengthOffset = Unpack(o.Find(x => x.perObjectShadowLengthOffset));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(_perObjectShadowDepthBits, EditorGUIUtility.TrTextContent("Depth Bits", "Sets the depth buffer precision for the per-object shadow map."));
            PropertyField(_perObjectShadowTileResolution, EditorGUIUtility.TrTextContent("Tile Resolution", "Sets the resolution for each tile in the per-object shadow atlas."));
            PropertyField(_perObjectShadowLengthOffset, EditorGUIUtility.TrTextContent("Shadow Length Offset", "Controls the offset distance for shadow length calculation."));
        }
    }
}

