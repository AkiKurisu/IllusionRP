using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    [Serializable]
    internal sealed class GeneratorCache
    {
        internal const string NamespacePlaceholder = "PathTracingInlineUnitNamespace";

        private static readonly string CachePath = Path.Combine("Library", "IllusionRP", "PathTracingInlineCache.json");
        private static readonly Regex s_LineDirective = new(@"^\s*#line\s+\d+\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

        [Serializable]
        internal sealed class Entry
        {
            public string Key;
            public string Error;
            public bool Checked;
            public string Body;
            public string AnyHitBody;
            public string[] Files = Array.Empty<string>();
            public long[] Stamps = Array.Empty<long>();
            public string[] ConstantNames = Array.Empty<string>();
            public string[] ConstantTypes = Array.Empty<string>();
            public int[] ConstantComponents = Array.Empty<int>();
            public int[] ConstantCounts = Array.Empty<int>();
            public int[] ConstantOffsets = Array.Empty<int>();
            public string[] Textures = Array.Empty<string>();
            public string[] HoistedKeys = Array.Empty<string>();
            public string[] HoistedTexts = Array.Empty<string>();
            public string[] ResourceNames = Array.Empty<string>();
            public string[] ResourceTypes = Array.Empty<string>();
            public bool HasClosestHit;
            public bool HasAnyHit;
            public bool HasObjectConstants;
            public int ConstantsOffset;
            public int RecordSize;

            public PathTracingInlineUnitTransform.Unit ToUnit(string unitNamespace)
            {
                if (Body == null)
                    return null;
                var unit = new PathTracingInlineUnitTransform.Unit
                {
                    Body = Body.Replace(NamespacePlaceholder, unitNamespace),
                    AnyHitBody = AnyHitBody?.Replace(NamespacePlaceholder, unitNamespace),
                    HasClosestHit = HasClosestHit,
                    HasAnyHit = HasAnyHit,
                    HasObjectConstants = HasObjectConstants,
                    ConstantsOffset = ConstantsOffset,
                    RecordSize = RecordSize
                };
                for (int i = 0; i < ConstantNames.Length; i++)
                {
                    unit.Constants.Add(new PathTracingInlineUnitTransform.Constant
                    {
                        Name = ConstantNames[i],
                        Type = ConstantTypes[i],
                        Components = ConstantComponents[i],
                        Count = ConstantCounts[i],
                        Offset = ConstantOffsets[i]
                    });
                }
                unit.Textures.AddRange(Textures);
                for (int i = 0; i < HoistedKeys.Length; i++)
                    unit.Hoisted.Add(new KeyValuePair<string, string>(HoistedKeys[i], HoistedTexts[i]));
                for (int i = 0; i < ResourceNames.Length; i++)
                    unit.HoistedResources[ResourceNames[i]] = ResourceTypes[i];
                return unit;
            }
        }

        public string Version;
        public List<Entry> Entries = new();

        [NonSerialized] private Dictionary<string, Entry> _byKey;

        private static string GeneratorVersion([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = null)
        {
            string folder = Path.GetDirectoryName(sourcePath ?? string.Empty);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return typeof(GeneratorCache).Assembly.ManifestModule.ModuleVersionId.ToString("N");
            using var sha = System.Security.Cryptography.SHA256.Create();
            foreach (string name in new[]
            {
                "DxcCompiler.cs", "PathTracingInlineGenerator.cs", "PathTracingInlineGeneratorCache.cs",
                "PathTracingInlineKernels.cs", "PathTracingInlineUnitTransform.cs"
            })
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(folder, name));
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", string.Empty);
        }

        public static GeneratorCache Load()
        {
            string version = GeneratorVersion();
            GeneratorCache cache = null;
            try
            {
                if (File.Exists(CachePath))
                    cache = JsonUtility.FromJson<GeneratorCache>(File.ReadAllText(CachePath));
            }
            catch (Exception)
            {
                cache = null;
            }
            if (cache == null || cache.Version != version)
                cache = new GeneratorCache { Version = version };
            cache._byKey = cache.Entries.ToDictionary(e => e.Key);
            return cache;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            File.WriteAllText(CachePath, JsonUtility.ToJson(this));
        }

        public bool TryGet(string key, out Entry entry)
        {
            if (!_byKey.TryGetValue(key, out entry))
                return false;
            for (int i = 0; i < entry.Files.Length; i++)
            {
                if (Stamp(entry.Files[i]) != entry.Stamps[i])
                {
                    entry = null;
                    return false;
                }
            }
            return true;
        }

        public void Put(string key, PathTracingInlineUnitTransform.Unit unit, string error, string[] files)
        {
            var entry = new Entry
            {
                Key = key,
                Error = error,
                Files = files,
                Stamps = files.Select(Stamp).ToArray()
            };
            if (unit != null)
            {
                entry.Body = unit.Body;
                entry.AnyHitBody = unit.AnyHitBody;
                entry.ConstantNames = unit.Constants.Select(c => c.Name).ToArray();
                entry.ConstantTypes = unit.Constants.Select(c => c.Type).ToArray();
                entry.ConstantComponents = unit.Constants.Select(c => c.Components).ToArray();
                entry.ConstantCounts = unit.Constants.Select(c => c.Count).ToArray();
                entry.ConstantOffsets = unit.Constants.Select(c => c.Offset).ToArray();
                entry.Textures = unit.Textures.ToArray();
                entry.HoistedKeys = unit.Hoisted.Select(h => h.Key).ToArray();
                entry.HoistedTexts = unit.Hoisted.Select(h => h.Value).ToArray();
                entry.ResourceNames = unit.HoistedResources.Keys.ToArray();
                entry.ResourceTypes = unit.HoistedResources.Values.ToArray();
                entry.HasClosestHit = unit.HasClosestHit;
                entry.HasAnyHit = unit.HasAnyHit;
                entry.HasObjectConstants = unit.HasObjectConstants;
                entry.ConstantsOffset = unit.ConstantsOffset;
                entry.RecordSize = unit.RecordSize;
            }
            _byKey[key] = entry;
        }

        public bool IsChecked(string key) => key != null && _byKey.TryGetValue(key, out Entry entry) && entry.Checked;

        public void SetChecked(string key, string error)
        {
            if (key == null || !_byKey.TryGetValue(key, out Entry entry))
                return;
            entry.Checked = true;
            entry.Error = error;
        }

        public void Retain(IEnumerable<string> keys)
        {
            var keep = new HashSet<string>(keys.Where(k => k != null));
            Entries = _byKey.Values.Where(e => keep.Contains(e.Key)).ToList();
        }

        public static string[] Dependencies(string preprocessed, string sourcePath)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(preprocessed))
            {
                foreach (Match m in s_LineDirective.Matches(preprocessed))
                {
                    string file = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (File.Exists(file))
                        files.Add(Path.GetFullPath(file));
                }
            }
            return files.OrderBy(f => f, StringComparer.Ordinal).ToArray();
        }

        public static long Stamp(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc.Ticks ^ info.Length : 0;
        }
    }

    [Serializable]
    internal sealed class MaterialScanCache
    {
        private static readonly string CachePath = Path.Combine("Library", "IllusionRP", "PathTracingInlineMaterials.json");

        public List<string> Paths = new();
        public List<long> Stamps = new();
        public List<string> Shaders = new();
        public List<string> Keywords = new();

        [NonSerialized] private Dictionary<string, int> _index;

        public static MaterialScanCache Load()
        {
            MaterialScanCache cache = null;
            try
            {
                if (File.Exists(CachePath))
                    cache = JsonUtility.FromJson<MaterialScanCache>(File.ReadAllText(CachePath));
            }
            catch (Exception)
            {
                cache = null;
            }
            cache ??= new MaterialScanCache();
            cache._index = new Dictionary<string, int>();
            for (int i = 0; i < cache.Paths.Count; i++)
                cache._index[cache.Paths[i]] = i;
            return cache;
        }

        public bool TryGet(string path, long stamp, out string shader, out string keywords)
        {
            shader = keywords = null;
            if (!_index.TryGetValue(path, out int i) || Stamps[i] != stamp)
                return false;
            shader = Shaders[i];
            keywords = Keywords[i];
            return true;
        }

        public void Put(string path, long stamp, string shader, string keywords)
        {
            if (_index.TryGetValue(path, out int i))
            {
                Stamps[i] = stamp;
                Shaders[i] = shader;
                Keywords[i] = keywords;
                return;
            }
            _index[path] = Paths.Count;
            Paths.Add(path);
            Stamps.Add(stamp);
            Shaders.Add(shader);
            Keywords.Add(keywords);
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            File.WriteAllText(CachePath, JsonUtility.ToJson(this));
        }

        public IEnumerable<(string Path, string Shader, string Keywords)> Materials()
        {
            for (int i = 0; i < Paths.Count; i++)
                yield return (Paths[i], Shaders[i], Keywords[i]);
        }
    }

    [Serializable]
    internal sealed class GeneratorReport
    {
        private static readonly string ReportPath = Path.Combine("Library", "IllusionRP", "PathTracingInlineReport.json");

        [Serializable]
        public struct Failure
        {
            public string Shader;
            public string Keywords;
            public string Error;
        }

        public List<Failure> Failures = new();

        public static GeneratorReport Load()
        {
            try
            {
                if (File.Exists(ReportPath))
                    return JsonUtility.FromJson<GeneratorReport>(File.ReadAllText(ReportPath)) ?? new GeneratorReport();
            }
            catch (Exception)
            {
                return new GeneratorReport();
            }
            return new GeneratorReport();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(this));
        }
    }
}
