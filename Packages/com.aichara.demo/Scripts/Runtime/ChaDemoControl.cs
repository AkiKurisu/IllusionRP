using System;
using System.Collections.Generic;
using System.IO;
using Ceres.Resource;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AIChara
{
    [DisallowMultipleComponent]
    public sealed class ChaDemoControl : MonoBehaviour
    {
        [SerializeField]
        private Transform skeletonRoot;

        [SerializeField]
        private ChaDemoContentSettings contentSettings = new();

        [SerializeField]
        private ChaDemoLoadReport lastReport = new();

        private readonly List<GameObject> _attachedObjects = new();
        private ResourceHandle<GameObject> _attachmentHandle;
        private bool _loading;

        public Transform SkeletonRoot
        {
            get => skeletonRoot ? skeletonRoot : transform;
            set => skeletonRoot = value;
        }

        public ChaDemoContentSettings ContentSettings => contentSettings;

        public ChaDemoLoadReport LastReport => lastReport;

        private void Reset()
        {
            skeletonRoot = transform;
            contentSettings.EnsureDefaults();
        }

        private void OnValidate()
        {
            contentSettings ??= new ChaDemoContentSettings();
            contentSettings.EnsureDefaults();
            if (!skeletonRoot)
            {
                skeletonRoot = transform;
            }
        }

        private void Start()
        {
            if (contentSettings.LoadOnStart)
            {
                Reload();
            }
        }

        private void OnDestroy()
        {
            Unload();
        }

        public void Reload()
        {
            ReloadAsync().Forget();
        }

        public async UniTask<ChaDemoLoadReport> ReloadAsync()
        {
            if (_loading)
            {
                lastReport.AddWarning("A demo character load is already running.");
                return lastReport;
            }

            _loading = true;
            try
            {
                Unload();
                contentSettings.EnsureDefaults();

                string contentPath = contentSettings.GetResolvedContentPath();
                lastReport.Begin(contentPath, contentSettings.CharacterAddress);

                if (!SkeletonRoot)
                {
                    lastReport.AddError("Skeleton root is missing.");
                    lastReport.Complete();
                    return lastReport;
                }

                if (!Directory.Exists(contentPath))
                {
                    lastReport.AddError($"Content folder not found: {contentPath}");
                    lastReport.Complete();
                    return lastReport;
                }

                string catalogPath = Path.Combine(contentPath, $"catalog{ResourceSystem.GetCatalogExtension()}");
                if (!File.Exists(catalogPath))
                {
                    lastReport.AddError($"Content catalog not found: {catalogPath}");
                    lastReport.Complete();
                    return lastReport;
                }

                bool catalogLoaded = await ResourceSystem.LoadCatalogAsync(contentPath);
                if (!catalogLoaded)
                {
                    lastReport.AddError($"Failed to load content catalog: {contentPath}");
                    lastReport.Complete();
                    return lastReport;
                }

                _attachmentHandle = ResourceSystem.InstantiateAsync(contentSettings.CharacterAddress);
                GameObject attachment = await _attachmentHandle;
                if (!attachment)
                {
                    lastReport.AddError($"Failed to instantiate character attachment: {contentSettings.CharacterAddress}");
                    lastReport.Complete();
                    return lastReport;
                }

                attachment.name = $"{contentSettings.CharacterAddress} Attachment";
                ChaDemoAttachResult attachResult = ChaDemoRendererAttacher.Attach(
                    SkeletonRoot,
                    attachment,
                    lastReport,
                    contentSettings.CleanupReferenceObjects);

                _attachedObjects.AddRange(attachResult.AttachedObjects);
                lastReport.Complete();
                return lastReport;
            }
            catch (Exception exception)
            {
                lastReport.AddError(exception.Message);
                Debug.LogException(exception, this);
                lastReport.Complete();
                return lastReport;
            }
            finally
            {
                _loading = false;
            }
        }

        public ChaDemoLoadReport ValidateContent()
        {
            contentSettings.EnsureDefaults();
            string contentPath = contentSettings.GetResolvedContentPath();
            lastReport.Begin(contentPath, contentSettings.CharacterAddress);

            if (!SkeletonRoot)
            {
                lastReport.AddError("Skeleton root is missing.");
            }

            if (!Directory.Exists(contentPath))
            {
                lastReport.AddError($"Content folder not found: {contentPath}");
            }
            else
            {
                string catalogPath = Path.Combine(contentPath, $"catalog{ResourceSystem.GetCatalogExtension()}");
                if (!File.Exists(catalogPath))
                {
                    lastReport.AddError($"Content catalog not found: {catalogPath}");
                }
            }

            lastReport.Complete();
            return lastReport;
        }

        public void Unload()
        {
            for (int i = _attachedObjects.Count - 1; i >= 0; i--)
            {
                if (_attachedObjects[i])
                {
                    DestroyTarget(_attachedObjects[i]);
                }
            }

            _attachedObjects.Clear();

            if (_attachmentHandle.IsValid())
            {
                ResourceSystem.Release(_attachmentHandle);
                _attachmentHandle = default;
            }
        }

        private static void DestroyTarget(UnityEngine.Object target)
        {
            if (!target)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
