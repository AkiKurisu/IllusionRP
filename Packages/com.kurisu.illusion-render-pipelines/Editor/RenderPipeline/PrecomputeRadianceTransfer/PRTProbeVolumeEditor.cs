using UnityEngine;
using UnityEditor;
using Illusion.Rendering.PRTGI;

namespace Illusion.Rendering.Editor
{
    [CustomEditor(typeof(PRTProbeVolume))]
    internal partial class PRTProbeVolumeEditor : PropertyFetchEditor<PRTProbeVolume>
    {
        private const double StatsRepaintInterval = 0.2;

        private double _nextStatsRepaintTime;

        private SerializedProperty _probeSizeX;
        private SerializedProperty _probeSizeY;
        private SerializedProperty _probeSizeZ;
        private SerializedProperty _probeGridSize;

        private SerializedProperty _enableBakePreprocess;
        private SerializedProperty _virtualOffset;
        private SerializedProperty _geometryBias;
        private SerializedProperty _rayOriginBias;

        private SerializedProperty _voxelProbeSize;
        private SerializedProperty _cascadeCount;
        private SerializedProperty _sectorsPerFrame;
        private SerializedProperty _uploadBudgetMiB;
        private SerializedProperty _sectorBudgetMiB;
        private SerializedProperty _enableRelightShadow;

        protected override void OnEnable()
        {
            base.OnEnable();

            _probeSizeX = Properties.Find(volume => volume.probeSizeX);
            _probeSizeY = Properties.Find(volume => volume.probeSizeY);
            _probeSizeZ = Properties.Find(volume => volume.probeSizeZ);
            _probeGridSize = Properties.Find(volume => volume.probeGridSize);

            _enableBakePreprocess = Properties.Find(volume => volume.enableBakePreprocess);
            _virtualOffset = Properties.Find(volume => volume.virtualOffset);
            _geometryBias = Properties.Find(volume => volume.geometryBias);
            _rayOriginBias = Properties.Find(volume => volume.rayOriginBias);

            _voxelProbeSize = Properties.Find(volume => volume.voxelProbeSize);
            _cascadeCount = Properties.Find(volume => volume.cascadeCount);
            _sectorsPerFrame = Properties.Find(volume => volume.sectorsPerFrame);
            _uploadBudgetMiB = Properties.Find(volume => volume.uploadBudgetMiB);
            _sectorBudgetMiB = Properties.Find(volume => volume.sectorBudgetMiB);
            _enableRelightShadow = Properties.Find(volume => volume.enableRelightShadow);

            InitializeBakeProperties();
            InitializeDebugProperties();

            EditorApplication.update += RepaintStatsInspector;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintStatsInspector;
        }

        private void RepaintStatsInspector()
        {
            if (!Target || !Target.isActiveAndEnabled ||
                Target.ResidentSectors == 0 && !Target.enableShadowCacheStats &&
                Target.debugMode != ProbeVolumeDebugMode.ShadowCache)
            {
                return;
            }

            double time = EditorApplication.timeSinceStartup;
            if (time < _nextStatsRepaintTime)
            {
                return;
            }

            _nextStatsRepaintTime = time + StatsRepaintInterval;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            if (!PRTProbeVolume.IsFeatureEnabled)
            {
                EditorGUILayout.HelpBox("Precomputed Radiance Transfer GI is not enabled in the Illusion Graphics renderer feature.",
                    MessageType.Info);
            }

            serializedObject.Update();
            DrawAssetStatus();

            using (new EditorGUI.DisabledScope(PRTVolumeManager.IsBaking))
            {
                DrawGridSettings();
                DrawPlacementSettings();
                DrawRelightSettings();
                DrawDebugSettings();
                DrawBakeSettings();
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawActionButtons();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAssetStatus()
        {
            string problem = Target.AssetInvalidReason;
            if (string.IsNullOrEmpty(problem) && Target.asset && !Target.asset.TryValidate(out string reason))
                problem = reason;
            if (!string.IsNullOrEmpty(problem))
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        private void DrawGridSettings()
        {
            if (Foldout("Probe Grid", true))
            {
                var count = new Vector3Int(_probeSizeX.intValue, _probeSizeY.intValue, _probeSizeZ.intValue);
                EditorGUI.BeginChangeCheck();
                count = EditorGUILayout.Vector3IntField(Styles.ProbeCount, count);
                if (EditorGUI.EndChangeCheck())
                {
                    _probeSizeX.intValue = Mathf.Clamp(count.x, 1, 128);
                    _probeSizeY.intValue = Mathf.Clamp(count.y, 1, 64);
                    _probeSizeZ.intValue = Mathf.Clamp(count.z, 1, 128);
                }

                EditorGUILayout.PropertyField(_probeGridSize, Styles.ProbeSpacing);
                DrawGridSummary();
            }

            EditorGUILayout.Space();
        }

        private void DrawGridSummary()
        {
            int x = _probeSizeX.intValue, y = _probeSizeY.intValue, z = _probeSizeZ.intValue;
            float spacing = _probeGridSize.floatValue;
            int width = Mathf.Max(1, _sectorWidth.intValue);
            int sectors = (x + width - 1) / width * ((z + width - 1) / width);
            string summary = $"{x * y * z:N0} probes  ·  {(x - 1) * spacing:0.#} × {(y - 1) * spacing:0.#} × {(z - 1) * spacing:0.#} m  ·  {sectors} sectors";
            EditorGUILayout.LabelField(" ", summary, EditorStyles.miniLabel);
        }

        private void DrawPlacementSettings()
        {
            if (Foldout("Probe Placement", true))
            {
                EditorGUILayout.PropertyField(_enableBakePreprocess, Styles.AutoPlacement);
                if (_enableBakePreprocess.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_virtualOffset, Styles.VirtualOffset);
                    EditorGUILayout.PropertyField(_geometryBias, Styles.GeometryBias);
                    EditorGUILayout.PropertyField(_rayOriginBias, Styles.RayOriginBias);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawRelightSettings()
        {
            if (Foldout("Relight", true))
            {
                EditorGUILayout.PropertyField(_voxelProbeSize, Styles.CameraWindow);
                EditorGUILayout.PropertyField(_cascadeCount, Styles.Cascades);
                EditorGUILayout.PropertyField(_sectorsPerFrame, Styles.SectorsPerFrame);
                EditorGUILayout.PropertyField(_uploadBudgetMiB, Styles.UploadBudget);
                EditorGUILayout.PropertyField(_sectorBudgetMiB, Styles.ResidentBudget);
                EditorGUILayout.PropertyField(_enableRelightShadow, Styles.RelightShadow);
            }

            EditorGUILayout.Space();
        }

        private void OnSceneGUI()
        {
            if (Target.Probes == null ||
                Target.debugMode != ProbeVolumeDebugMode.ProbeRadiance &&
                Target.debugMode != ProbeVolumeDebugMode.ShadowCache)
            {
                return;
            }

            var current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || current.alt)
                return;

            // Pick the nearest probe along the mouse ray; one handle per probe does not scale to large volumes.
            float radius = Target.probeHandleSize * (Target.debugMode == ProbeVolumeDebugMode.ShadowCache ? 0.1f : 0.5f);
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            int picked = -1;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < Target.Probes.Length; i++)
            {
                Vector3 offset = Target.Probes[i].Position - ray.origin;
                float along = Vector3.Dot(offset, ray.direction);
                if (along <= 0f || along >= nearest || (offset - ray.direction * along).sqrMagnitude > radius * radius)
                    continue;
                nearest = along;
                picked = i;
            }

            if (picked < 0)
                return;
            Target.selectedProbeIndex = picked;
            current.Use();
            Repaint();
        }

        private static class Styles
        {
            public static readonly GUIContent ProbeCount = new("Probe Count", "Number of probes along each axis.");
            public static readonly GUIContent ProbeSpacing = new("Probe Spacing", "Distance between neighboring probes in meters.");

            public static readonly GUIContent AutoPlacement = new("Auto Placement", "Search for a capture position outside geometry for each probe while baking. Adjustment Volumes only apply when enabled.");
            public static readonly GUIContent VirtualOffset = new("Offset", "Offset added to every probe before the placement search.");
            public static readonly GUIContent GeometryBias = new("Geometry Bias", "How far to push a probe's capture point out of geometry.");
            public static readonly GUIContent RayOriginBias = new("Ray Origin Bias", "Distance between a probe's center and the origin of its placement rays.");

            public static readonly GUIContent CameraWindow = new("Camera Window", "Probes per axis in each camera cascade. Clamped to the probe count.");
            public static readonly GUIContent Cascades = new("Cascades", "Nested camera windows; each one doubles the probe spacing. Coarser cascades are skipped once a finer one covers the whole volume.");
            public static readonly GUIContent SectorsPerFrame = new("Sectors Per Frame", "Maximum number of sectors relit each frame.");
            public static readonly GUIContent UploadBudget = new("Upload Budget (MiB)", "Maximum sector transport uploaded to the GPU each frame.");
            public static readonly GUIContent ResidentBudget = new("Resident Budget (MiB)", "Maximum sector transport kept resident on the GPU.");
            public static readonly GUIContent RelightShadow = new("Shadows", "Use existing shadow maps for visibility when relighting.");

            public static readonly GUIContent Asset = new("Asset", "Baked transport for this volume. Created next to the scene when empty.");
            public static readonly GUIContent BakeResolution = new("Capture Resolution", "Cubemap resolution used to capture surfels for each probe.");
            public static readonly GUIContent SampleCount = new("Samples Per Probe", "Number of directions integrated for each probe.");
            public static readonly GUIContent SampleSeed = new("Sampling Seed", "Seed of the deterministic sample directions.");
            public static readonly GUIContent SectorWidth = new("Sector Size", "Probe columns per sector along X and Z. Smaller sectors stream at a finer granularity.");
            public static readonly GUIContent GenerateLighting = EditorGUIUtility.TrTextContent("Generate Lighting", "Bake probe volume transport and reflection probe normalization data.");
            public static readonly string[] DetailActions =
            {
                "Bake Reflection Probes Normalization Data",
                "Clear Baked Data"
            };

            public static readonly GUIContent Visualization = new("Visualization", "Scene view visualization of this probe volume.");
            public static readonly GUIContent ProbeDebugMode = new("Selected Probe", "What to draw for the probe selected in the Scene view.");
            public static readonly GUIContent ProbeHandleSize = new("Handle Size", "Size of probe handles in the Scene view.");
            public static readonly GUIContent ShadowReadbackInterval = new("Readback Interval", "Frames between shadow cache GPU readbacks.");
            public static readonly GUIContent ShadowShowLabels = new("Show Labels", "Show shadow cache summary and selected probe labels in the Scene view.");
            public static readonly GUIContent ShadowSurfelSize = new("Surfel Size", "Size of the selected probe's surfel spheres.");
            public static readonly GUIContent PlacementPreview = new("Preview Placement", "Run the placement search without baking transport.");
            public static readonly GUIContent CollectStats = new("Statistics", "Read back solver residuals and shadow cache counters from the GPU.");
            public static readonly GUIContent StatsReadbackInterval = new("Readback Interval", "Frames between statistics readbacks.");
        }
    }
}
