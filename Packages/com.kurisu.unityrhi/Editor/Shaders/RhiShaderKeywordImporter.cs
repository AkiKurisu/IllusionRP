using System;
using System.Collections.Generic;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    /// <summary>
    /// Imports an empty .rhikeywords marker file. Its keyword-space definition
    /// is stored in .meta; concrete variants are selected by editor usage or by
    /// build variant providers, not by this asset.
    /// </summary>
    [ScriptedImporter(2, Extension)]
    internal sealed class RhiShaderKeywordImporter : ScriptedImporter
    {
        internal const string Extension = "rhikeywords";

        [SerializeField] internal RhiShaderKeywordDefinition[] keywords =
            Array.Empty<RhiShaderKeywordDefinition>();

        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                Validate(ctx.assetPath);
                RhiShaderKeywordSpace space = CreateKeywordSpace();
                space.name = System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath);
                ctx.AddObjectToAsset("keywords", space);
                ctx.SetMainObject(space);
            }
            catch (Exception exception)
            {
                ctx.LogImportError(exception.Message);
                var empty = ScriptableObject.CreateInstance<RhiShaderKeywordSpace>();
                empty.name = System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath);
                ctx.AddObjectToAsset("keywords", empty);
                ctx.SetMainObject(empty);
            }
        }

        internal RhiShaderKeywordSpace CreateKeywordSpace()
        {
            Validate(assetPath);
            var space = ScriptableObject.CreateInstance<RhiShaderKeywordSpace>();
            space.Initialize(keywords ?? Array.Empty<RhiShaderKeywordDefinition>());
            return space;
        }

        private void Validate(string sourcePath)
        {
            RhiShaderKeywordDefinition[] definitions =
                keywords ?? Array.Empty<RhiShaderKeywordDefinition>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (RhiShaderKeywordDefinition keyword in
                definitions)
            {
                if (keyword == null)
                    throw new InvalidOperationException($"{sourcePath}: a keyword is null.");
                if (string.IsNullOrWhiteSpace(keyword.name))
                    throw new InvalidOperationException($"{sourcePath}: a keyword has no name.");
                if (!names.Add(keyword.name))
                    throw new InvalidOperationException(
                        $"{sourcePath}: keyword '{keyword.name}' is declared more than once.");
                if (keyword.mode == RhiShaderKeywordMode.MultiCompile &&
                    keyword.allowCustomValue)
                    throw new InvalidOperationException(
                        $"{sourcePath}: multi_compile keyword '{keyword.name}' cannot " +
                        "accept custom values.");
                if ((keyword.options == null || keyword.options.Length == 0) &&
                    !keyword.allowCustomValue)
                    throw new InvalidOperationException(
                        $"{sourcePath}: keyword '{keyword.name}' has no options.");
                if (!keyword.Accepts(keyword.defaultValue))
                    throw new InvalidOperationException(
                        $"{sourcePath}: keyword '{keyword.name}' does not accept its default " +
                        $"value '{keyword.defaultValue}'.");
            }
        }

    }
}
