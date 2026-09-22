using UnityEngine;
using UnityEditor;
using System;

namespace Illusion.Rendering.Editor
{
    public static class DiffusionProfileMaterialUtility
    {
        public static void SetProfile(Material material, DiffusionProfileAsset profile,
            string propertyName = "_DiffusionProfile")
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));
            if (string.IsNullOrEmpty(propertyName))
                throw new ArgumentException("A diffusion profile property name is required.", nameof(propertyName));

            string assetPropertyName = propertyName + "_Asset";
            if (!material.HasProperty(propertyName) || !material.HasProperty(assetPropertyName))
            {
                throw new ArgumentException(
                    $"Material '{material.name}' does not expose {propertyName} and {assetPropertyName}.",
                    nameof(material));
            }

            EncodeProfile(profile, out Vector4 guid, out float hash);
            material.SetVector(assetPropertyName, guid);
            material.SetFloat(propertyName, hash);
        }

        internal static void EncodeProfile(DiffusionProfileAsset profile, out Vector4 guid, out float hash)
        {
            guid = Vector4.zero;
            hash = 0f;
            if (profile == null)
                return;

            string assetPath = AssetDatabase.GetAssetPath(profile);
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(assetGuid))
                throw new ArgumentException($"Diffusion profile '{profile.name}' is not a saved asset.", nameof(profile));

            guid = IllusionRenderingUtils.ConvertGUIDToVector4(assetGuid);
            hash = IllusionRenderingUtils.AsFloat(profile.profile.hash);
        }
    }

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
                DiffusionProfileMaterialUtility.EncodeProfile(diffusionProfile, out Vector4 newGuid, out float hash);

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
