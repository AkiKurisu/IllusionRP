using UnityEngine;
using UnityEditor;
using System;

namespace Illusion.Rendering.Editor
{
    internal class DiffusionProfileDrawer : MaterialPropertyDrawer
    {
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor) => 0;

        public override void OnGUI(Rect position, MaterialProperty prop, String label, MaterialEditor editor)
        {
            // Find properties
            var assetProperty = MaterialEditor.GetMaterialProperty(editor.targets, prop.name + "_Asset");
            DiffusionProfileMaterialUI.OnGUI(assetProperty, prop, prop.displayName);
        }
    }

    internal static class DiffusionProfileMaterialUI
    {
        private const string DiffusionProfileNotAssigned = "The diffusion profile on this material is not assigned.\n" +
                                                           "The material will be rendered with default profile.";
        
        public static void OnGUI(MaterialProperty diffusionProfileAsset, MaterialProperty diffusionProfileHash, string displayName = "Diffusion Profile")
        {
            MaterialEditor.BeginProperty(diffusionProfileAsset);
            MaterialEditor.BeginProperty(diffusionProfileHash);

            // We can't cache these fields because of several edge cases like undo/redo or pressing escape in the object picker
            string guid = IllusionRenderingUtils.ConvertVector4ToGUID(diffusionProfileAsset.vectorValue);
            DiffusionProfileAsset diffusionProfile = AssetDatabase.LoadAssetAtPath<DiffusionProfileAsset>(AssetDatabase.GUIDToAssetPath(guid));

            // is it okay to do this every frame ?
            EditorGUI.BeginChangeCheck();
            diffusionProfile = (DiffusionProfileAsset)EditorGUILayout.ObjectField(displayName, diffusionProfile, typeof(DiffusionProfileAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                Vector4 newGuid = Vector4.zero;
                float hash = 0;

                if (diffusionProfile != null)
                {
                    guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(diffusionProfile));
                    newGuid = IllusionRenderingUtils.ConvertGUIDToVector4(guid);
                    hash = IllusionRenderingUtils.AsFloat(diffusionProfile.profile.hash);
                }

                // encode back GUID and it's hash
                diffusionProfileAsset.vectorValue = newGuid;
                diffusionProfileHash.floatValue = hash;

                // TODO: Link diffusion profile as dependency
                // Update external reference.
                // foreach (var target in materialEditor.targets)
                // {
                //     MaterialExternalReferences matExternalRefs = MaterialExternalReferences.GetMaterialExternalReferences(target as Material);
                //     matExternalRefs.SetDiffusionProfileReference(profileIndex, diffusionProfile);
                // }
            }

            MaterialEditor.EndProperty();
            MaterialEditor.EndProperty();

            DrawDiffusionProfileWarning(diffusionProfile);
        }

        private static void DrawDiffusionProfileWarning(DiffusionProfileAsset materialProfile)
        {
            if (materialProfile == null)
            {
                EditorGUILayout.HelpBox(DiffusionProfileNotAssigned, MessageType.Warning);
            }
        }
    }
}