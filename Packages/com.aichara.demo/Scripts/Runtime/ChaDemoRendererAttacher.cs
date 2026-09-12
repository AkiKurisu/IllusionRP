using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIChara
{
    public static class ChaDemoRendererAttacher
    {
        public static ChaDemoAttachResult Attach(
            Transform targetRoot,
            GameObject attachmentRoot,
            ChaDemoLoadReport report,
            bool cleanupReferenceObjects)
        {
            var result = new ChaDemoAttachResult();
            if (!targetRoot)
            {
                report.AddError("Target skeleton root is missing.");
                return result;
            }

            if (!attachmentRoot)
            {
                report.AddError("Attachment prefab instance is missing.");
                return result;
            }

            Transform sourceRoot = attachmentRoot.transform;
            var sourcePaths = BuildPathLookup(sourceRoot);
            var targetMaps = BuildTargetMaps(targetRoot);
            var movedRoots = new HashSet<Transform>();

            var skinnedRenderers = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in skinnedRenderers)
            {
                AttachSkinnedRenderer(renderer, sourceRoot, sourcePaths, targetRoot, targetMaps, movedRoots, result, report);
            }

            var meshRenderers = sourceRoot.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.GetComponent<MeshFilter>())
                .ToArray();
            foreach (var renderer in meshRenderers)
            {
                AttachStaticRenderer(renderer.transform, sourceRoot, sourcePaths, targetRoot, targetMaps, movedRoots, result);
            }

            report.rendererCount = skinnedRenderers.Length + meshRenderers.Length;
            report.skinnedRendererCount = skinnedRenderers.Length;
            report.staticRendererCount = meshRenderers.Length;
            report.attachedObjectCount = result.AttachedObjects.Count;

            if (cleanupReferenceObjects)
            {
                DestroyRemainingChildren(sourceRoot);
            }

            attachmentRoot.SetActive(false);
            return result;
        }

        private static void AttachSkinnedRenderer(
            SkinnedMeshRenderer renderer,
            Transform sourceRoot,
            IReadOnlyDictionary<Transform, string> sourcePaths,
            Transform targetRoot,
            TargetMaps targetMaps,
            HashSet<Transform> movedRoots,
            ChaDemoAttachResult result,
            ChaDemoLoadReport report)
        {
            Transform[] sourceBones = renderer.bones;
            var targetBones = new Transform[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                targetBones[i] = ResolveBone(sourceBones[i], sourceRoot, sourcePaths, targetRoot, targetMaps, movedRoots, result, report);
            }

            renderer.bones = targetBones;
            if (renderer.rootBone)
            {
                renderer.rootBone = ResolveBone(renderer.rootBone, sourceRoot, sourcePaths, targetRoot, targetMaps, movedRoots, result, report);
            }

            report.remappedBoneCount += targetBones.Count(bone => bone && !IsChildOf(bone, sourceRoot));
            AttachStaticRenderer(renderer.transform, sourceRoot, sourcePaths, targetRoot, targetMaps, movedRoots, result);
        }

        private static Transform ResolveBone(
            Transform sourceBone,
            Transform sourceRoot,
            IReadOnlyDictionary<Transform, string> sourcePaths,
            Transform targetRoot,
            TargetMaps targetMaps,
            HashSet<Transform> movedRoots,
            ChaDemoAttachResult result,
            ChaDemoLoadReport report)
        {
            if (!sourceBone)
            {
                return null;
            }

            if (TryFindTarget(sourceBone, sourcePaths, targetMaps, out Transform targetBone))
            {
                return targetBone;
            }

            report.AddMissingBone(GetDisplayPath(sourceRoot, sourceBone));
            MoveUnmappedSubtree(sourceBone, sourceRoot, sourcePaths, targetRoot, targetMaps, movedRoots, result);
            report.preservedBoneCount++;
            return sourceBone;
        }

        private static void AttachStaticRenderer(
            Transform rendererTransform,
            Transform sourceRoot,
            IReadOnlyDictionary<Transform, string> sourcePaths,
            Transform targetRoot,
            TargetMaps targetMaps,
            HashSet<Transform> movedRoots,
            ChaDemoAttachResult result)
        {
            Transform targetParent = ResolveTargetParent(rendererTransform.parent, sourceRoot, sourcePaths, targetRoot, targetMaps);
            ReparentPreserveLocal(rendererTransform, targetParent);
            AddAttachedObject(rendererTransform, movedRoots, result);
        }

        private static Transform ResolveTargetParent(
            Transform sourceParent,
            Transform sourceRoot,
            IReadOnlyDictionary<Transform, string> sourcePaths,
            Transform targetRoot,
            TargetMaps targetMaps)
        {
            for (Transform cursor = sourceParent; cursor && cursor != sourceRoot; cursor = cursor.parent)
            {
                if (TryFindTarget(cursor, sourcePaths, targetMaps, out Transform target))
                {
                    return target;
                }
            }

            return targetRoot;
        }

        private static void MoveUnmappedSubtree(
            Transform sourceBone,
            Transform sourceRoot,
            IReadOnlyDictionary<Transform, string> sourcePaths,
            Transform targetRoot,
            TargetMaps targetMaps,
            HashSet<Transform> movedRoots,
            ChaDemoAttachResult result)
        {
            Transform subtreeRoot = sourceBone;
            Transform targetParent = targetRoot;
            for (Transform cursor = sourceBone.parent; cursor && cursor != sourceRoot; cursor = cursor.parent)
            {
                if (TryFindTarget(cursor, sourcePaths, targetMaps, out Transform target))
                {
                    targetParent = target;
                    break;
                }

                subtreeRoot = cursor;
            }

            if (movedRoots.Contains(subtreeRoot))
            {
                return;
            }

            ReparentPreserveLocal(subtreeRoot, targetParent);
            AddAttachedObject(subtreeRoot, movedRoots, result);
        }

        private static void AddAttachedObject(Transform root, HashSet<Transform> movedRoots, ChaDemoAttachResult result)
        {
            if (movedRoots.Add(root))
            {
                result.AttachedObjects.Add(root.gameObject);
            }
        }

        private static void ReparentPreserveLocal(Transform child, Transform parent)
        {
            Vector3 localPosition = child.localPosition;
            Quaternion localRotation = child.localRotation;
            Vector3 localScale = child.localScale;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            child.localRotation = localRotation;
            child.localScale = localScale;
        }

        private static bool TryFindTarget(
            Transform source,
            IReadOnlyDictionary<Transform, string> sourcePaths,
            TargetMaps targetMaps,
            out Transform target)
        {
            if (sourcePaths.TryGetValue(source, out string path) &&
                targetMaps.ByPath.TryGetValue(path, out target))
            {
                return true;
            }

            return targetMaps.UniqueByName.TryGetValue(source.name, out target);
        }

        private static Dictionary<Transform, string> BuildPathLookup(Transform root)
        {
            var result = new Dictionary<Transform, string>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                result[transform] = GetRelativePath(root, transform);
            }

            return result;
        }

        private static TargetMaps BuildTargetMaps(Transform root)
        {
            var byPath = new Dictionary<string, Transform>();
            var nameBuckets = new Dictionary<string, List<Transform>>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                byPath[GetRelativePath(root, transform)] = transform;
                if (!nameBuckets.TryGetValue(transform.name, out var bucket))
                {
                    bucket = new List<Transform>();
                    nameBuckets.Add(transform.name, bucket);
                }

                bucket.Add(transform);
            }

            var uniqueByName = nameBuckets
                .Where(pair => pair.Value.Count == 1)
                .ToDictionary(pair => pair.Key, pair => pair.Value[0]);
            return new TargetMaps(byPath, uniqueByName);
        }

        private static string GetRelativePath(Transform root, Transform transform)
        {
            if (transform == root)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            for (Transform cursor = transform; cursor && cursor != root; cursor = cursor.parent)
            {
                names.Push(cursor.name);
            }

            return string.Join("/", names);
        }

        private static string GetDisplayPath(Transform root, Transform transform)
        {
            string path = GetRelativePath(root, transform);
            return string.IsNullOrEmpty(path) ? transform.name : path;
        }

        private static bool IsChildOf(Transform child, Transform root)
        {
            for (Transform cursor = child; cursor; cursor = cursor.parent)
            {
                if (cursor == root)
                {
                    return true;
                }
            }

            return false;
        }

        private static void DestroyRemainingChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(root.GetChild(i).gameObject);
            }
        }

        private readonly struct TargetMaps
        {
            public readonly IReadOnlyDictionary<string, Transform> ByPath;
            public readonly IReadOnlyDictionary<string, Transform> UniqueByName;

            public TargetMaps(
                IReadOnlyDictionary<string, Transform> byPath,
                IReadOnlyDictionary<string, Transform> uniqueByName)
            {
                ByPath = byPath;
                UniqueByName = uniqueByName;
            }
        }
    }

    public sealed class ChaDemoAttachResult
    {
        public List<GameObject> AttachedObjects { get; } = new();
    }
}
