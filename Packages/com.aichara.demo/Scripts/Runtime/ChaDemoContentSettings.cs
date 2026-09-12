using System;
using System.IO;
using UnityEngine;

namespace AIChara
{
    [Serializable]
    public sealed class ChaDemoContentSettings
    {
        public const string DefaultContentFolder = "UserData";
        public const string DefaultCharacterAddress = "aichara-demo/character/demo-render";

        [SerializeField]
        private string contentPath = DefaultContentFolder;

        [SerializeField]
        private string characterAddress = DefaultCharacterAddress;

        [SerializeField]
        private bool loadOnStart = true;

        [SerializeField]
        private bool cleanupReferenceObjects = true;

        public string ContentPath
        {
            get => contentPath;
            set => contentPath = value;
        }

        public string CharacterAddress
        {
            get => characterAddress;
            set => characterAddress = value;
        }

        public bool LoadOnStart
        {
            get => loadOnStart;
            set => loadOnStart = value;
        }

        public bool CleanupReferenceObjects
        {
            get => cleanupReferenceObjects;
            set => cleanupReferenceObjects = value;
        }

        public string GetResolvedContentPath()
        {
            if (string.IsNullOrWhiteSpace(contentPath))
            {
                contentPath = DefaultContentFolder;
            }

            string normalizedPath = contentPath.Replace('\\', '/');
            if (Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            return Path.Combine(GetApplicationRootPath(), normalizedPath).Replace('\\', '/');
        }

        private static string GetApplicationRootPath()
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string rootPath = Directory.GetParent(dataPath)?.FullName;
            if (string.IsNullOrEmpty(rootPath))
            {
                rootPath = dataPath;
            }

            return rootPath.Replace('\\', '/');
        }

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(contentPath))
            {
                contentPath = DefaultContentFolder;
            }

            if (string.IsNullOrWhiteSpace(characterAddress))
            {
                characterAddress = DefaultCharacterAddress;
            }
        }
    }
}
