using System.Linq;
using UnityEditor;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PRTProbeVolumeAsset))]
    internal class PRTVolumeAssetEditor : PropertyFetchEditor<PRTProbeVolumeAsset>
    {
        private const string MemoryFoldoutKey = "PRTProbeVolumeAsset_Memory";

        public override void OnInspectorGUI()
        {
            if (!Target.TryValidate(out string reason))
            {
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
                return;
            }

            PRTProbeGrid grid = Target.Grid;
            PRTSectorData[] sectors = Target.Sectors;
            var buffers = new (string label, long count, int stride)[]
            {
                ("Probe Metadata", Target.Probes.Length, 16),
                ("Geometry Surfels", sectors.Sum(s => (long)s.surfels.Length), Surfel.Stride),
                ("Bricks", sectors.Sum(s => (long)s.bricks.Length), SurfelIndices.Stride),
                ("SH9 Transfers", sectors.Sum(s => (long)s.factors.Length), BrickFactor.Stride),
                ("Sector Probe Ranges", sectors.Sum(s => (long)s.probes.Length), PRTProbeData.Stride),
                ("Sector Probe IDs", sectors.Sum(s => (long)s.probeIds.Length), 4),
                ("Sky Directions", sectors.Sum(s => (long)s.skySamples.Length), PRTSkySample.Stride)
            };
            long total = buffers.Sum(b => b.count * b.stride);

            EditorGUILayout.LabelField("Probes", $"{grid.ProbeCount:N0}  ({grid.count.x} × {grid.count.y} × {grid.count.z}, {grid.spacing:0.##} m spacing)");
            EditorGUILayout.LabelField("Sectors", $"{sectors.Length}  ({Target.SectorWidth} × {Target.SectorWidth} probe columns)");
            EditorGUILayout.LabelField("Sampling", $"{Target.Signature.sampleCount} samples per probe, seed {Target.Signature.seed}");
            EditorGUILayout.LabelField("Data Size", PRTProbeVolumeEditor.FormatBytes(total));

            bool expanded = SessionState.GetBool(MemoryFoldoutKey, false);
            bool next = EditorGUILayout.Foldout(expanded, "Memory Breakdown", true);
            if (next != expanded) SessionState.SetBool(MemoryFoldoutKey, next);
            if (!next) return;

            EditorGUI.indentLevel++;
            foreach (var (label, count, stride) in buffers)
                EditorGUILayout.LabelField(label, $"{count:N0}  ·  {PRTProbeVolumeEditor.FormatBytes(count * stride)}");
            EditorGUI.indentLevel--;
        }
    }
}
