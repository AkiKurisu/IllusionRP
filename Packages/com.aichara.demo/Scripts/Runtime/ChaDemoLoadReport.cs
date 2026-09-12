using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIChara
{
    [Serializable]
    public sealed class ChaDemoLoadReport
    {
        public bool succeeded;
        public string contentPath;
        public string characterAddress;
        public string message;
        public int rendererCount;
        public int skinnedRendererCount;
        public int staticRendererCount;
        public int remappedBoneCount;
        public int preservedBoneCount;
        public int attachedObjectCount;
        public List<string> warnings = new();
        public List<string> errors = new();
        public List<string> missingBones = new();

        public bool HasErrors => errors.Count > 0;

        public void Begin(string resolvedContentPath, string address)
        {
            succeeded = false;
            contentPath = resolvedContentPath;
            characterAddress = address;
            message = string.Empty;
            rendererCount = 0;
            skinnedRendererCount = 0;
            staticRendererCount = 0;
            remappedBoneCount = 0;
            preservedBoneCount = 0;
            attachedObjectCount = 0;
            warnings.Clear();
            errors.Clear();
            missingBones.Clear();
        }

        public void Complete()
        {
            succeeded = !HasErrors;
            if (string.IsNullOrEmpty(message))
            {
                message = succeeded ? "AIChara demo character loaded." : "AIChara demo character failed to load.";
            }
        }

        public void AddWarning(string warning)
        {
            if (!string.IsNullOrEmpty(warning))
            {
                warnings.Add(warning);
                Debug.LogWarning($"[AIChara Demo] {warning}");
            }
        }

        public void AddError(string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                errors.Add(error);
                Debug.LogError($"[AIChara Demo] {error}");
            }
        }

        public void AddMissingBone(string bone)
        {
            if (!string.IsNullOrEmpty(bone))
            {
                missingBones.Add(bone);
            }
        }
    }
}
