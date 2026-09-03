#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using System;

namespace Illusion.Rendering
{
    // This class keep track of every diffusion profile in the project so it can generate unique uint hashes
    // for every asset, which are used to differentiate diffusion profiles in the shader
    [InitializeOnLoad]
    internal static class DiffusionProfileHashTable
    {
        [NonSerialized] 
        private static readonly Dictionary<int,  uint> DiffusionProfileHashes = new();

        static DiffusionProfileHashTable()
        {
            
        }
        
        // Stable hash to avoid having different result after upgrading mono
        // Source: https://github.com/Unity-Technologies/mono/blob/unity-2021.2-mbe-pre-upgrade/mcs/class/referencesource/mscorlib/system/string.cs#L824
        private static unsafe int MonoStringHash(string guid)
        {
            fixed(char *src = guid)
            {
                int hash1 = 5381;
                int hash2 = hash1;

                int     c;
                char *s = src;
                while ((c = s[0]) != 0)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ c;
                    c = s[1];
                    if (c == 0)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ c;
                    s += 2;
                }

                return hash1 + (hash2 * 1566083941);
            }
        }

        private static uint GetDiffusionProfileHash(DiffusionProfileAsset asset)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);

            // In case the diffusion profile is not yet saved on the disk, we don't generate the hash
            if (String.IsNullOrEmpty(assetPath))
                return 0;

            uint hash32 = (uint)MonoStringHash(AssetDatabase.AssetPathToGUID(assetPath));
            uint mantissa = hash32 & 0x7FFFFF;
            uint exponent = 0b10000000; // 0 as exponent

            // only store the first 23 bits so when the hash is converted to float, it doesn't write into
            // the exponent part of the float (which avoids having NaNs, inf or precisions issues)
            return (exponent << 23) | mantissa;
        }

        internal static uint GenerateUniqueHash(DiffusionProfileAsset asset)
        {
            uint hash = GetDiffusionProfileHash(asset);

            while (DiffusionProfileHashes.ContainsValue(hash))
            {
                Debug.LogWarning("Collision found in asset: " + asset + ", generating a new hash, previous hash: " + hash);
                hash++;
            }

            return hash;
        }

        internal static void UpdateDiffusionProfileHashNow(DiffusionProfileAsset profile)
        {
            uint hash = profile.profile.hash;

            // If the hash is 0, then we need to generate a new one (it means that the profile was just created)
            if (hash == 0)
            {
                profile.profile.hash = GenerateUniqueHash(profile);
                EditorUtility.SetDirty(profile);
                // We can't move the asset
            }
            // If the asset is not in the list, we regenerate it's hash using the GUID (which leads to the same result every time)
            else if (!DiffusionProfileHashes.ContainsKey(profile.GetInstanceID()))
            {
                uint newHash = GenerateUniqueHash(profile);
                if (newHash != profile.profile.hash)
                {
                    profile.profile.hash = newHash;
                    EditorUtility.SetDirty(profile);
                }
            }
            else // otherwise, no issue, we don't change the hash and we keep it to check for collisions
                DiffusionProfileHashes.Add(profile.GetInstanceID(), profile.profile.hash);
        }
    }
}
#endif
