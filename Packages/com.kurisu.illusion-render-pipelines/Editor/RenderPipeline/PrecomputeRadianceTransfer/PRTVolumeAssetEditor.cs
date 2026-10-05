using UnityEngine;
using System.Linq;
using UnityEditor;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PRTProbeVolumeAsset))]
    internal class PRTVolumeAssetEditor : PropertyFetchEditor<PRTProbeVolumeAsset>
    {
        public override void OnInspectorGUI()
        {
            if (!Target.TryValidate(out string reason)) EditorGUILayout.HelpBox(reason, MessageType.Warning);
            var sectors = Target.Sectors;
            if (sectors == null) return;
            long bytes = 0;
            EditorGUILayout.IntField("Sectors", sectors.Length);
            EditorGUILayout.IntField("Sector Width", Target.SectorWidth);
            DrawCount("Global Probe Metadata", Target.Probes.Length, 16, ref bytes);
            DrawCount("Geometry Surfels", sectors.Sum(s => s.surfels.Length), Surfel.Stride, ref bytes);
            DrawCount("Bricks", sectors.Sum(s => s.bricks.Length), SurfelIndices.Stride, ref bytes);
            DrawCount("SH9 Transfers", sectors.Sum(s => s.factors.Length), BrickFactor.Stride, ref bytes);
            DrawCount("Local Probe Ranges", sectors.Sum(s => s.probes.Length), PRTProbeData.Stride, ref bytes);
            DrawCount("Global Probe IDs", sectors.Sum(s => s.probeIds.Length), 4, ref bytes);
            DrawCount("Sky Directions", sectors.Sum(s => s.skySamples.Length), PRTSkySample.Stride, ref bytes);
            EditorGUILayout.LabelField("Array Elements", $"{bytes:N0} bytes ({bytes / 1048576.0:F2} MiB)");
            if (Target.HasValidData)
            {
                EditorGUILayout.LabelField("Backend", Target.Signature.backend);
                EditorGUILayout.IntField("Samples Per Probe", Target.Signature.sampleCount);
                EditorGUILayout.LongField("Seed", Target.Signature.seed);
                EditorGUILayout.Vector3IntField("Grid", Target.Grid.count);
                EditorGUILayout.FloatField("Spacing", Target.Grid.spacing);
                EditorGUILayout.Vector3Field("Origin", Target.Grid.origin);
                EditorGUILayout.LabelField("Geometry Signature", Target.Signature.geometry.ToString());
                EditorGUILayout.LabelField("Material Signature", Target.Signature.materials.ToString());
                EditorGUILayout.LabelField("Settings Signature", Target.Signature.settings.ToString());
                EditorGUILayout.LabelField("Authoring Input Signature", Target.Signature.authoringInputs.ToString());
            }
        }
        private static void DrawCount(string label, int count, int stride, ref long bytes)
        {
            long size = (long)count * stride;
            bytes += size;
            EditorGUILayout.LabelField(label, $"{count:N0} × {stride} bytes = {size:N0}");
        }
    }
}
