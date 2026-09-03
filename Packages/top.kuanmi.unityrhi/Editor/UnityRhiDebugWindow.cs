using System;
using System.Collections.Generic;
using UnityRhi.Interop;
using UnityEditor;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    /// <summary>Live native resources, command-stream activity, and device health.</summary>
    public sealed class UnityRhiDebugWindow : EditorWindow
    {
        private enum ResourceColumn
        {
            Name, Type, Size, LogicalSize, Allocation, Status, LastUse,
            Dimensions, Format, Mips, ArraySize, Samples, Stride, InitialState,
            ShaderStage, Bindings, Descriptors, Layouts, Elements, Colors, Depth,
            HeapType, Capacity, FirstDescriptor, AllowUpdate, Compacted, Exports,
            LocalRoots, Entries, Version, Started, Resolved
        }

        private struct ColumnDefinition
        {
            public readonly ResourceColumn Column;
            public readonly string Label;
            public readonly float Width;

            public ColumnDefinition(ResourceColumn column, string label, float width)
            {
                Column = column;
                Label = label;
                Width = width;
            }
        }

        private const float TypeSidebarWidth = 154;
        private const float NameMinimumWidth = 210;
        private const float RowHeight = 20;

        private double _lastSampleTime;
        private ulong _lastStreamCount;
        private float _streamsPerSecond;
        private ResourceInfo[] _resources = Array.Empty<ResourceInfo>();
        private readonly List<ResourceInfo> _visibleResources = new List<ResourceInfo>();
        private Vector2 _typeScroll;
        private Vector2 _tableScroll;
        private Vector2 _detailsScroll;
        private Vector2 _commandScroll;
        private bool _showCommandBreakdown = true;
        private string _search = "";
        private int _kindFilter = (int)ResourceKind.Buffer + 1;
        private int _statusFilter;
        private ResourceColumn _sortColumn = ResourceColumn.Allocation;
        private bool _sortAscending;
        private ulong _selectedHandle;
        private bool _selectedPending;
        private GUIStyle _rightAlignedCellStyle;
        private static GUIStyle _detailsTitleStyle;
        private static GUIStyle _detailsValueStyle;
        private static float _detailsContentWidth = 240;

        private static readonly string[] StatusFilters = { "All statuses", "Live", "Pending", "Never used", "In flight", "Idle" };

        [MenuItem("Window/Analysis/UnityRHI Debug Window")]
        public static void Open()
        {
            var window = GetWindow<UnityRhiDebugWindow>("UnityRHI");
            window.minSize = new Vector2(760, 520);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            CommandList.DetailedFrameStatsEnabled = true;
            _lastSampleTime = EditorApplication.timeSinceStartup - 1.0;
            Sample();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CommandList.DetailedFrameStatsEnabled = false;
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastSampleTime < 0.25)
                return;
            Sample();
            Repaint();
        }

        private void Sample()
        {
            double now = EditorApplication.timeSinceStartup;
            double elapsed = Math.Max(0.001, now - _lastSampleTime);
            if (RhiCore.IsD3D12Active)
            {
                ulong streams = RhiCore.CommandStreamEventCount;
                _streamsPerSecond = (float)((streams - _lastStreamCount) / elapsed);
                _lastStreamCount = streams;
                _resources = Device.Instance.GetResourceSnapshot();
            }
            else
            {
                _resources = Array.Empty<ResourceInfo>();
            }
            _lastSampleTime = now;
        }

        private void OnGUI()
        {
            // IMGUI color state can be left tinted by other editor GUI code,
            // especially with domain reload disabled. Keep this window's text
            // readable and restore the caller's state when drawing finishes.
            Color previousColor = GUI.color;
            Color previousContentColor = GUI.contentColor;
            Color previousBackgroundColor = GUI.backgroundColor;
            bool previousEnabled = GUI.enabled;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            try
            {
                if (!RhiCore.IsD3D12Active)
                {
                    EditorGUILayout.HelpBox(
                        "UnityRHI native device is not active. The editor must run Direct3D12 " +
                        "(Player Settings > Other Settings > Graphics APIs for Windows, then restart).", MessageType.Warning);
                    return;
                }

                DrawDeviceAndSummary();
                DrawFrameTraffic();
                DrawCommandBreakdown();
                EditorGUILayout.Space(4);
                DrawToolbar();

                Rect content = EditorGUILayout.GetControlRect(false,
                    Mathf.Max(160, position.height - (_showCommandBreakdown ? 480 : 300)));
                float detailsWidth = _selectedHandle == 0 ? 0 : Mathf.Clamp(content.width * 0.34f, 280, 520);
                float mainWidth = content.width - detailsWidth;
                Rect typesRect = new Rect(content.x, content.y, TypeSidebarWidth, content.height);
                Rect tableRect = new Rect(typesRect.xMax + 4, content.y,
                    Mathf.Max(100, mainWidth - TypeSidebarWidth - 4), content.height);
                DrawTypeSidebar(typesRect);
                DrawResourceTable(tableRect);
                if (detailsWidth > 0)
                    DrawDetails(new Rect(tableRect.xMax + 4, content.y, detailsWidth - 4, content.height));

                EditorGUILayout.Space(4);
                DrawFooter();
            }
            finally
            {
                GUI.color = previousColor;
                GUI.contentColor = previousContentColor;
                GUI.backgroundColor = previousBackgroundColor;
                GUI.enabled = previousEnabled;
            }
        }

        private void DrawDeviceAndSummary()
        {
            int removedReason = RhiCore.DeviceRemovedReason;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Device  API {RhiCore.NativeApiVersion}", EditorStyles.boldLabel, GUILayout.Width(150));
                GUILayout.Label(removedReason == 0 ? "Health: OK" : $"REMOVED 0x{removedReason:X8}",
                    removedReason == 0 ? EditorStyles.label : EditorStyles.boldLabel, GUILayout.Width(155));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Streams {_lastStreamCount:N0}  ({_streamsPerSecond:0.0}/s)");
                GUILayout.Space(12);
                GUILayout.Label($"Dropped {RhiCore.DroppedCommandStreamCount:N0}");
            }

            ulong committed = 0, placedRequirements = 0, unityOwned = 0, pending = 0;
            int liveCount = 0, pendingCount = 0;
            foreach (ResourceInfo resource in _resources)
            {
                if (resource.IsPending) { ++pendingCount; pending += PhysicalContribution(resource); }
                else ++liveCount;
                if (resource.IsUnityOwned) unityOwned += ValidAllocation(resource);
                else if (resource.IsPlaced) placedRequirements += ValidAllocation(resource);
                else committed += PhysicalContribution(resource);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMetric("Resources", $"{liveCount:N0} live / {pendingCount:N0} pending");
                DrawMetric("Plugin physical", FormatBytes(committed));
                DrawMetric("Placed requirements", FormatBytes(placedRequirements));
                DrawMetric("Unity-owned estimate", FormatBytes(unityOwned));
                DrawMetric("Pending physical", FormatBytes(pending));
            }
        }

        private static void DrawMetric(string title, string value)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(115)))
            {
                GUILayout.Label(title, EditorStyles.miniLabel);
                GUILayout.Label(value, EditorStyles.boldLabel);
            }
        }

        private static void DrawFrameTraffic()
        {
            CommandFrameStats frame = CommandList.FrameStats;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (frame.CommandListCount == 0)
                {
                    GUILayout.Label("Frame traffic: no command lists submitted", EditorStyles.miniLabel);
                    return;
                }

                GUILayout.Label($"Submitted frame {frame.FrameIndex:N0}", EditorStyles.boldLabel,
                    GUILayout.Width(150));
                GUILayout.Label($"Upload payload  {FormatBytes(frame.UploadBytes)}",
                    GUILayout.Width(175));
                GUILayout.Label($"Cmd streams  {FormatBytes(frame.CommandStreamBytes)}",
                    GUILayout.Width(165));
                GUILayout.Label($"Lists  {frame.CommandListCount:N0}", GUILayout.Width(80));
                GUILayout.Label($"Largest  {FormatBytes(frame.MaxCommandListBytes)}");
            }
        }

        private void DrawCommandBreakdown()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _showCommandBreakdown = EditorGUILayout.Foldout(_showCommandBreakdown,
                    "Submitted CmdList contents", true);
                GUILayout.FlexibleSpace();
                GUILayout.Label("bars use encoded stream bytes", EditorStyles.miniLabel);
            }
            if (!_showCommandBreakdown)
                return;

            Rect rect = EditorGUILayout.GetControlRect(false, 176);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            if (RhiCore.NativeApiVersion < 6)
            {
                GUI.Label(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 30),
                    "Detailed command bytes require UnityRHI native API 6. Rebuild/reload the native plugin.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            CommandListFrameStats[] lists = CommandList.FrameCommandLists;
            if (lists.Length == 0)
            {
                GUI.Label(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 20),
                    "Waiting for a command list submitted while this window is open...",
                    EditorStyles.miniLabel);
                return;
            }

            float viewHeight = 4;
            foreach (CommandListFrameStats list in lists)
                viewHeight += 24 + (list.Commands.Length + 1) * 18 + 5;
            Rect body = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
            Rect view = new Rect(0, 0, Mathf.Max(1, body.width - 16), viewHeight);
            uint largest = 1;
            foreach (CommandListFrameStats list in lists)
                largest = Math.Max(largest, list.ByteSize);

            _commandScroll = GUI.BeginScrollView(body, _commandScroll, view);
            float y = 2;
            foreach (CommandListFrameStats list in lists)
            {
                float listRatio = list.ByteSize / (float)largest;
                Rect listHeader = new Rect(0, y, view.width, 22);
                EditorGUI.DrawRect(new Rect(listHeader.x, listHeader.y,
                    listHeader.width * listRatio, listHeader.height),
                    new Color(0.18f, 0.48f, 0.78f, 0.35f));
                GUI.Label(new Rect(6, y + 2, view.width - 12, 18),
                    $"CmdList {list.Index}    stream {FormatBytes(list.ByteSize)}    " +
                    $"upload {FormatBytes(list.UploadBytes)}",
                    EditorStyles.boldLabel);
                y += 24;

                DrawCommandByteRow(view.width, ref y, "StreamHeader", 1, 16, list.ByteSize,
                    new Color(0.42f, 0.42f, 0.42f, 0.35f));
                foreach (CommandOpcodeFrameStats command in list.Commands)
                    DrawCommandByteRow(view.width, ref y, command.Opcode.ToString(), command.Count,
                        command.ByteSize, list.ByteSize, new Color(0.24f, 0.58f, 0.86f, 0.32f));
                y += 5;
            }
            GUI.EndScrollView();
        }

        private static void DrawCommandByteRow(float width, ref float y, string name, uint count,
            uint bytes, uint streamBytes, Color color)
        {
            const float height = 17;
            float ratio = streamBytes == 0 ? 0 : bytes / (float)streamBytes;
            Rect row = new Rect(0, y, width, height);
            EditorGUI.DrawRect(new Rect(row.x, row.y, row.width * ratio, row.height), color);
            GUI.Label(new Rect(8, y, Mathf.Max(80, width - 310), height), name, EditorStyles.miniLabel);
            GUI.Label(new Rect(Mathf.Max(90, width - 300), y, 82, height),
                $"× {count:N0}", RightMiniLabel);
            GUI.Label(new Rect(Mathf.Max(175, width - 215), y, 100, height),
                FormatBytes(bytes), RightMiniLabel);
            GUI.Label(new Rect(Mathf.Max(278, width - 112), y, 104, height),
                $"{ratio * 100:0.0}%", RightMiniLabel);
            y += 18;
        }

        private static GUIStyle _rightMiniLabel;
        private static GUIStyle RightMiniLabel => _rightMiniLabel ??
            (_rightMiniLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight });

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Filter", GUILayout.Width(34));
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
                _statusFilter = EditorGUILayout.Popup(_statusFilter, StatusFilters, EditorStyles.toolbarPopup, GUILayout.Width(110));
                BuildVisibleResources();
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_visibleResources.Count}/{_resources.Length}", EditorStyles.miniLabel);
            }
        }

        private void DrawTypeSidebar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect header = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, 21);
            GUI.Label(header, "Resource types", EditorStyles.toolbarButton);

            Array kinds = Enum.GetValues(typeof(ResourceKind));
            int[] counts = new int[kinds.Length];
            foreach (ResourceInfo resource in _resources)
                counts[(int)resource.Kind]++;

            Rect body = new Rect(rect.x + 2, rect.y + 25, rect.width - 4, rect.height - 27);
            Rect view = new Rect(0, 0, body.width - 16, kinds.Length * 23 + 2);
            _typeScroll = GUI.BeginScrollView(body, _typeScroll, view);
            for (int i = 0; i < kinds.Length; ++i)
            {
                ResourceKind kind = (ResourceKind)kinds.GetValue(i);
                bool selected = _kindFilter == (int)kind + 1;
                Rect button = new Rect(0, i * 23, view.width, 22);
                if (selected)
                    EditorGUI.DrawRect(button, new Color(0.20f, 0.42f, 0.68f, 0.45f));
                if (GUI.Button(button, $"{kind}  ({counts[(int)kind]:N0})",
                    selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton))
                {
                    SelectKind(kind);
                }
            }
            GUI.EndScrollView();
        }

        private void SelectKind(ResourceKind kind)
        {
            _kindFilter = (int)kind + 1;
            _tableScroll = Vector2.zero;
            ColumnDefinition[] columns = GetColumns(kind);
            if (!ContainsColumn(columns, _sortColumn))
            {
                _sortColumn = columns.Length > 1 ? columns[1].Column : ResourceColumn.Name;
                _sortAscending = false;
            }

            ResourceInfo? selected = FindSelected();
            if (selected.HasValue && selected.Value.Kind != kind)
                _selectedHandle = 0;
            BuildVisibleResources();
        }

        private void BuildVisibleResources()
        {
            _visibleResources.Clear();
            foreach (ResourceInfo resource in _resources)
            {
                if (_kindFilter > 0 && (int)resource.Kind != _kindFilter - 1)
                    continue;
                if (!string.IsNullOrWhiteSpace(_search) &&
                    (resource.DebugName == null || resource.DebugName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) &&
                    resource.Kind.ToString().IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!MatchesStatus(resource, _statusFilter))
                    continue;
                _visibleResources.Add(resource);
            }
            _visibleResources.Sort(CompareResources);
        }

        private void DrawResourceTable(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            const float headerHeight = 21;
            ResourceKind kind = (ResourceKind)(_kindFilter - 1);
            ColumnDefinition[] columns = GetColumns(kind);
            Rect body = new Rect(rect.x + 2, rect.y + headerHeight + 3, rect.width - 4, rect.height - headerHeight - 5);
            // Reserve the scrollbar strip for both header and rows. The old
            // header used rect.width while rows used body.width - 16, shifting
            // every column after Name by one scrollbar width.
            float viewportWidth = Mathf.Max(1, body.width - 16);
            float fixedWidth = GetFixedColumnsWidth(columns);
            float contentWidth = Mathf.Max(viewportWidth, NameMinimumWidth + fixedWidth);
            float nameWidth = Mathf.Max(NameMinimumWidth, contentWidth - fixedWidth);
            Rect header = new Rect(rect.x + 2, rect.y + 2, viewportWidth, headerHeight);
            GUI.BeginGroup(header);
            try
            {
                float x = -_tableScroll.x;
                foreach (ColumnDefinition column in columns)
                    DrawHeaderButton(ref x, column.Label, column.Column,
                        column.Column == ResourceColumn.Name ? nameWidth : column.Width, headerHeight);
            }
            finally
            {
                GUI.EndGroup();
            }

            Rect view = new Rect(0, 0, contentWidth, _visibleResources.Count * RowHeight + 2);
            _tableScroll = GUI.BeginScrollView(body, _tableScroll, view);
            for (int i = 0; i < _visibleResources.Count; ++i)
                DrawResourceRow(new Rect(0, i * RowHeight, view.width, RowHeight),
                    _visibleResources[i], i, columns, nameWidth);
            GUI.EndScrollView();
        }

        private void DrawHeaderButton(ref float x, string label, ResourceColumn column, float width, float height)
        {
            Rect cell = new Rect(x, 0, width, height);
            string arrow = _sortColumn == column ? (_sortAscending ? " ▲" : " ▼") : "";
            if (GUI.Button(cell, label + arrow, EditorStyles.toolbarButton))
            {
                if (_sortColumn == column) _sortAscending = !_sortAscending;
                else { _sortColumn = column; _sortAscending = true; }
            }
            x += width;
        }

        private void DrawResourceRow(Rect row, ResourceInfo resource, int index,
            ColumnDefinition[] columns, float nameWidth)
        {
            bool selected = resource.Handle == _selectedHandle && resource.IsPending == _selectedPending;
            if (selected) EditorGUI.DrawRect(row, new Color(0.20f, 0.42f, 0.68f, 0.45f));
            else if ((index & 1) != 0) EditorGUI.DrawRect(row, new Color(0, 0, 0, 0.08f));
            if (GUI.Button(row, GUIContent.none, GUIStyle.none))
            {
                _selectedHandle = resource.Handle;
                _selectedPending = resource.IsPending;
            }

            float x = row.x;
            foreach (ColumnDefinition column in columns)
                DrawTableCell(ref x, row.y,
                    column.Column == ResourceColumn.Name ? nameWidth : column.Width,
                    GetColumnText(resource, column.Column), IsRightAligned(column.Column));
        }

        private void DrawTableCell(ref float x, float y, float width, string text, bool rightAligned)
        {
            if (_rightAlignedCellStyle == null)
                _rightAlignedCellStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };
            GUIStyle style = rightAligned ? _rightAlignedCellStyle : EditorStyles.label;
            GUI.Label(new Rect(x + 4, y + 1, width - 8, 18), text, style);
            x += width;
        }

        private static ColumnDefinition[] GetColumns(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Buffer:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0),
                        C(ResourceColumn.LogicalSize, "Logical", 82),
                        C(ResourceColumn.Allocation, "Allocation", 88),
                        C(ResourceColumn.Stride, "Stride", 58),
                        C(ResourceColumn.Format, "Format", 104),
                        C(ResourceColumn.InitialState, "Initial state", 104),
                        C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.Texture:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0),
                        C(ResourceColumn.Dimensions, "Dimensions", 126),
                        C(ResourceColumn.Format, "Format", 104),
                        C(ResourceColumn.Mips, "Mips", 48),
                        C(ResourceColumn.ArraySize, "Array", 50),
                        C(ResourceColumn.Samples, "Samples", 58),
                        C(ResourceColumn.Allocation, "Allocation", 88),
                        C(ResourceColumn.InitialState, "Initial state", 104),
                        C(ResourceColumn.Status, "Status", 82));
                case ResourceKind.StagingTexture:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Dimensions, "Dimensions", 126),
                        C(ResourceColumn.Format, "Format", 104), C(ResourceColumn.Mips, "Mips", 48),
                        C(ResourceColumn.LogicalSize, "Footprint", 88), C(ResourceColumn.Status, "Status", 82));
                case ResourceKind.Shader:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.ShaderStage, "Stage", 96),
                        C(ResourceColumn.LogicalSize, "Bytecode", 84), C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.ShaderLibrary:
                    return StandardSizeColumns("Bytecode", ResourceColumn.LogicalSize);
                case ResourceKind.BindingLayout:
                case ResourceKind.BindingSet:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Bindings, "Bindings", 68),
                        C(ResourceColumn.Descriptors, "Descriptors", 82), C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.ComputePipeline:
                case ResourceKind.GraphicsPipeline:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Layouts, "Layouts", 64),
                        C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.InputLayout:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Elements, "Elements", 68),
                        C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.Framebuffer:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Dimensions, "Dimensions", 112),
                        C(ResourceColumn.Colors, "Colors", 54), C(ResourceColumn.Depth, "Depth", 54),
                        C(ResourceColumn.Samples, "Samples", 58), C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.Heap:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.HeapType, "Heap type", 94),
                        C(ResourceColumn.Allocation, "Capacity", 88), C(ResourceColumn.Status, "Status", 82));
                case ResourceKind.DescriptorTable:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Capacity, "Capacity", 68),
                        C(ResourceColumn.FirstDescriptor, "First descriptor", 102), C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.AccelStruct:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.LogicalSize, "Backing", 84),
                        C(ResourceColumn.AllowUpdate, "Allow update", 84), C(ResourceColumn.Compacted, "Compacted", 76),
                        C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.RayTracingPipeline:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Exports, "Exports", 62),
                        C(ResourceColumn.LocalRoots, "Local roots", 76), C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.ShaderTable:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Entries, "Entries", 62),
                        C(ResourceColumn.Version, "Version", 62), C(ResourceColumn.LogicalSize, "Backing", 84),
                        C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
                case ResourceKind.EventQuery:
                    return QueryColumns(false);
                case ResourceKind.TimerQuery:
                    return QueryColumns(true);
                default:
                    return Columns(
                        C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Size, "Size", 88),
                        C(ResourceColumn.Status, "Status", 82),
                        C(ResourceColumn.LastUse, "Last use", 72));
            }
        }

        private static ColumnDefinition[] StandardSizeColumns(string sizeLabel, ResourceColumn sizeColumn) => Columns(
            C(ResourceColumn.Name, "Name", 0), C(sizeColumn, sizeLabel, 84),
            C(ResourceColumn.Status, "Status", 82),
            C(ResourceColumn.LastUse, "Last use", 72));

        private static ColumnDefinition[] QueryColumns(bool timer) => timer
            ? Columns(C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Started, "Started", 62),
                C(ResourceColumn.Resolved, "Resolved", 68), C(ResourceColumn.Status, "Status", 82))
            : Columns(C(ResourceColumn.Name, "Name", 0), C(ResourceColumn.Started, "Started", 62),
                C(ResourceColumn.Status, "Status", 82));

        private static ColumnDefinition C(ResourceColumn column, string label, float width) =>
            new ColumnDefinition(column, label, width);

        private static ColumnDefinition[] Columns(params ColumnDefinition[] columns) => columns;

        private static float GetFixedColumnsWidth(ColumnDefinition[] columns)
        {
            float width = 0;
            foreach (ColumnDefinition column in columns)
                if (column.Column != ResourceColumn.Name)
                    width += column.Width;
            return width;
        }

        private static bool ContainsColumn(ColumnDefinition[] columns, ResourceColumn value)
        {
            foreach (ColumnDefinition column in columns)
                if (column.Column == value)
                    return true;
            return false;
        }

        private static bool IsRightAligned(ResourceColumn column)
        {
            switch (column)
            {
                case ResourceColumn.Name:
                case ResourceColumn.Type:
                case ResourceColumn.Status:
                case ResourceColumn.Format:
                case ResourceColumn.InitialState:
                case ResourceColumn.ShaderStage:
                case ResourceColumn.HeapType:
                    return false;
                default:
                    return true;
            }
        }

        private static string GetColumnText(ResourceInfo r, ResourceColumn column)
        {
            switch (column)
            {
                case ResourceColumn.Name: return string.IsNullOrEmpty(r.DebugName) ? "<unnamed>" : r.DebugName;
                case ResourceColumn.Type: return r.Kind.ToString();
                case ResourceColumn.Size: return FormatBytes(DisplaySize(r));
                case ResourceColumn.LogicalSize: return FormatBytes(r.LogicalSize);
                case ResourceColumn.Allocation: return FormatAllocation(r);
                case ResourceColumn.Status: return GetStatus(r);
                case ResourceColumn.LastUse: return r.LastUseInstance == 0 ? "-" : r.LastUseInstance.ToString();
                case ResourceColumn.Dimensions: return r.Depth > 1 ? $"{r.Width} × {r.Height} × {r.Depth}" : $"{r.Width} × {r.Height}";
                case ResourceColumn.Format: return r.Format.ToString();
                case ResourceColumn.Mips: return r.MipLevels.ToString();
                case ResourceColumn.ArraySize: return r.ArraySize.ToString();
                case ResourceColumn.Samples: return r.SampleCount.ToString();
                case ResourceColumn.Stride: return r.Detail0.ToString();
                case ResourceColumn.InitialState: return r.InitialState.ToString();
                case ResourceColumn.ShaderStage: return ((ShaderType)r.Detail0).ToString();
                case ResourceColumn.Bindings: return r.Detail0.ToString();
                case ResourceColumn.Descriptors: return r.Detail1.ToString();
                case ResourceColumn.Layouts: return r.Detail0.ToString();
                case ResourceColumn.Elements: return r.Detail0.ToString();
                case ResourceColumn.Colors: return r.Detail0.ToString();
                case ResourceColumn.Depth: return YesNo(r.Detail1 != 0);
                case ResourceColumn.HeapType: return ((HeapType)r.Detail0).ToString();
                case ResourceColumn.Capacity: return r.Detail0.ToString("N0");
                case ResourceColumn.FirstDescriptor: return r.Detail1.ToString("N0");
                case ResourceColumn.AllowUpdate: return YesNo(r.Detail0 != 0);
                case ResourceColumn.Compacted: return YesNo(r.Detail1 != 0);
                case ResourceColumn.Exports: return r.Detail0.ToString();
                case ResourceColumn.LocalRoots: return r.Detail1.ToString();
                case ResourceColumn.Entries: return r.Detail0.ToString();
                case ResourceColumn.Version: return r.Detail1.ToString();
                case ResourceColumn.Started: return YesNo(r.Detail0 != 0);
                case ResourceColumn.Resolved: return YesNo(r.Detail1 != 0);
                default: return "-";
            }
        }

        private static string YesNo(bool value) => value ? "Yes" : "No";

        private void DrawDetails(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            ResourceInfo? selected = FindSelected();
            if (!selected.HasValue)
            {
                _selectedHandle = 0;
                return;
            }

            ResourceInfo resource = selected.Value;
            // GUILayout areas inside an EditorWindow can be evaluated against
            // the window's layout group instead of the absolute GUI rect. Put
            // the panel in its own local-coordinate group so its content can
            // never jump to (0, 0) and cover the resource table.
            GUI.BeginGroup(rect);
            try
            {
                Rect inner = new Rect(8, 8, rect.width - 16, rect.height - 16);
                // Leave room for the vertical scrollbar and make every detail
                // row consume the actual panel width instead of GUILayout's
                // much smaller preferred width.
                _detailsContentWidth = Mathf.Max(120, inner.width - 18);
                GUILayout.BeginArea(inner);
                try
                {
                    using (var scroll = new EditorGUILayout.ScrollViewScope(_detailsScroll,
                        GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                    {
                        _detailsScroll = scroll.scrollPosition;
                        WrappedDetailLabel(string.IsNullOrEmpty(resource.DebugName) ? "<unnamed>" : resource.DebugName,
                            DetailsTitleStyle, _detailsContentWidth);
                        Detail("Type", resource.Kind.ToString());
                        Detail("Status", GetStatus(resource));
                        Detail("Handle", $"0x{resource.Handle:X}");
                        Detail("Last use instance", resource.LastUseInstance == 0 ? "Never" : resource.LastUseInstance.ToString());
                        if (resource.IsPending) Detail("Release instance", resource.ReleaseInstance.ToString());
                        Detail("Ownership", resource.IsUnityOwned ? "Unity owned" : "Plugin owned");
                        Detail("Flags", FormatFlags(resource.Flags));

                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Memory", EditorStyles.boldLabel);
                        Detail("Logical size", FormatBytes(resource.LogicalSize));
                        Detail("Allocation", FormatAllocation(resource));
                        Detail("Total accounting", AccountingDescription(resource));

                        DrawTypeDetails(resource);
                    }
                }
                finally
                {
                    GUILayout.EndArea();
                }
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        private static void DrawTypeDetails(ResourceInfo r)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Resource details", EditorStyles.boldLabel);
            switch (r.Kind)
            {
                case ResourceKind.Buffer:
                    Detail("Format", r.Format.ToString());
                    Detail("Struct stride", r.Detail0.ToString());
                    if (r.Detail0 != 0) Detail("Element capacity", (r.LogicalSize / r.Detail0).ToString("N0"));
                    Detail("Initial state", r.InitialState.ToString());
                    Detail("Permanent state", r.PermanentState.ToString());
                    break;
                case ResourceKind.Texture:
                    Detail("Dimensions", $"{r.Width} × {r.Height} × {r.Depth}");
                    Detail("Array / Mips", $"{r.ArraySize} / {r.MipLevels}");
                    Detail("Samples / Planes", $"{r.SampleCount} / {r.Detail1}");
                    Detail("Format", r.Format.ToString());
                    Detail("Dimension type", ((TextureDimension)r.Detail0).ToString());
                    Detail("Initial state", r.InitialState.ToString());
                    Detail("Permanent state", r.PermanentState.ToString());
                    break;
                case ResourceKind.StagingTexture:
                    Detail("Dimensions", $"{r.Width} × {r.Height} × {r.Depth}");
                    Detail("Array / Mips", $"{r.ArraySize} / {r.MipLevels}");
                    Detail("Format", r.Format.ToString());
                    break;
                case ResourceKind.Shader: Detail("Shader type", ((ShaderType)r.Detail0).ToString()); break;
                case ResourceKind.BindingLayout:
                case ResourceKind.BindingSet:
                    Detail("Bindings", r.Detail0.ToString()); Detail("Descriptors", r.Detail1.ToString()); break;
                case ResourceKind.DescriptorTable:
                    Detail("Capacity", r.Detail0.ToString()); Detail("First descriptor", r.Detail1.ToString()); break;
                case ResourceKind.Framebuffer:
                    Detail("Dimensions", $"{r.Width} × {r.Height}");
                    Detail("Color / Depth", $"{r.Detail0} / {(r.Detail1 != 0 ? "yes" : "no")}"); break;
                case ResourceKind.AccelStruct:
                    Detail("Allow update", r.Detail0 != 0 ? "Yes" : "No"); Detail("Compacted", r.Detail1 != 0 ? "Yes" : "No"); break;
                case ResourceKind.RayTracingPipeline:
                    Detail("Exports", r.Detail0.ToString()); Detail("Max local roots", r.Detail1.ToString()); break;
                case ResourceKind.ShaderTable:
                    Detail("Entries", r.Detail0.ToString()); Detail("Version", r.Detail1.ToString()); break;
                case ResourceKind.InputLayout: Detail("Elements", r.Detail0.ToString()); break;
                case ResourceKind.ComputePipeline:
                case ResourceKind.GraphicsPipeline: Detail("Binding layouts", r.Detail0.ToString()); break;
                case ResourceKind.Heap: Detail("Heap type", ((HeapType)r.Detail0).ToString()); break;
                case ResourceKind.EventQuery: Detail("Started", r.Detail0 != 0 ? "Yes" : "No"); break;
                case ResourceKind.TimerQuery:
                    Detail("Started", r.Detail0 != 0 ? "Yes" : "No"); Detail("Resolved", r.Detail1 != 0 ? "Yes" : "No"); break;
            }
        }

        private void DrawFooter()
        {
            CommandStreamAllocatorStats allocator = CommandList.AllocatorStats;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"Command memory  alloc {allocator.NativeAllocations:N0}  realloc {allocator.Reallocations:N0}  " +
                    $"pool hit/return {allocator.PoolRentHits:N0}/{allocator.PoolReturns:N0}  " +
                    $"peak {FormatBytes((ulong)Math.Max(0, allocator.PeakCapacity))}  stream {FormatBytes((ulong)Math.Max(0, allocator.PeakStreamBytes))}",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(65))) { _lastSampleTime = 0; Sample(); }
                if (GUILayout.Button("Run GC", GUILayout.Width(65))) Device.Instance.RunGarbageCollection();
                if (GUILayout.Button("Report", GUILayout.Width(65))) Device.Instance.ReportLiveResources();
            }
        }

        private ResourceInfo? FindSelected()
        {
            foreach (ResourceInfo resource in _resources)
                if (resource.Handle == _selectedHandle && resource.IsPending == _selectedPending)
                    return resource;
            return null;
        }

        private int CompareResources(ResourceInfo a, ResourceInfo b)
        {
            int result;
            switch (_sortColumn)
            {
                case ResourceColumn.Name: result = string.Compare(a.DebugName, b.DebugName, StringComparison.OrdinalIgnoreCase); break;
                case ResourceColumn.Type: result = a.Kind.CompareTo(b.Kind); break;
                case ResourceColumn.Size: result = DisplaySize(a).CompareTo(DisplaySize(b)); break;
                case ResourceColumn.LogicalSize: result = a.LogicalSize.CompareTo(b.LogicalSize); break;
                case ResourceColumn.Allocation: result = ValidAllocation(a).CompareTo(ValidAllocation(b)); break;
                case ResourceColumn.Status: result = string.Compare(GetStatus(a), GetStatus(b), StringComparison.Ordinal); break;
                case ResourceColumn.LastUse: result = a.LastUseInstance.CompareTo(b.LastUseInstance); break;
                case ResourceColumn.Dimensions:
                    result = ((ulong)a.Width * a.Height * Math.Max(1u, a.Depth)).CompareTo(
                        (ulong)b.Width * b.Height * Math.Max(1u, b.Depth)); break;
                case ResourceColumn.Format: result = a.Format.CompareTo(b.Format); break;
                case ResourceColumn.Mips: result = a.MipLevels.CompareTo(b.MipLevels); break;
                case ResourceColumn.ArraySize: result = a.ArraySize.CompareTo(b.ArraySize); break;
                case ResourceColumn.Samples: result = a.SampleCount.CompareTo(b.SampleCount); break;
                case ResourceColumn.Stride:
                case ResourceColumn.ShaderStage:
                case ResourceColumn.Bindings:
                case ResourceColumn.Layouts:
                case ResourceColumn.Elements:
                case ResourceColumn.Colors:
                case ResourceColumn.HeapType:
                case ResourceColumn.Capacity:
                case ResourceColumn.AllowUpdate:
                case ResourceColumn.Exports:
                case ResourceColumn.Entries:
                case ResourceColumn.Started: result = a.Detail0.CompareTo(b.Detail0); break;
                case ResourceColumn.Descriptors:
                case ResourceColumn.Depth:
                case ResourceColumn.FirstDescriptor:
                case ResourceColumn.Compacted:
                case ResourceColumn.LocalRoots:
                case ResourceColumn.Version:
                case ResourceColumn.Resolved: result = a.Detail1.CompareTo(b.Detail1); break;
                case ResourceColumn.InitialState: result = a.InitialState.CompareTo(b.InitialState); break;
                default: result = 0; break;
            }
            if (result == 0) result = string.Compare(a.DebugName, b.DebugName, StringComparison.OrdinalIgnoreCase);
            return _sortAscending ? result : -result;
        }

        private static bool MatchesStatus(ResourceInfo r, int filter)
        {
            switch (filter)
            {
                case 1: return !r.IsPending;
                case 2: return r.IsPending;
                case 3: return !r.IsPending && r.LastUseInstance == 0;
                case 4: return !r.IsPending && r.LastUseInstance != 0 && (r.Flags & ResourceInfoFlags.GpuUseCompleted) == 0;
                case 5: return !r.IsPending && (r.Flags & ResourceInfoFlags.GpuUseCompleted) != 0;
                default: return true;
            }
        }

        private static string GetStatus(ResourceInfo r)
        {
            if (r.IsPending) return "Pending";
            if (r.LastUseInstance == 0) return "Never used";
            return (r.Flags & ResourceInfoFlags.GpuUseCompleted) != 0 ? "Idle" : "In flight";
        }

        private static ulong DisplaySize(ResourceInfo r) => ValidAllocation(r) != 0 ? ValidAllocation(r) : r.LogicalSize;

        private static ulong ValidAllocation(ResourceInfo r) =>
            r.AllocationSize == ulong.MaxValue ? 0 : r.AllocationSize;

        private static ulong PhysicalContribution(ResourceInfo r)
        {
            if (r.IsUnityOwned || r.IsPlaced || r.HasInternalBacking || (r.Flags & ResourceInfoFlags.Virtual) != 0)
                return 0;
            if (r.Kind == ResourceKind.Heap)
                return ValidAllocation(r);
            return (r.Flags & ResourceInfoFlags.HasNativeResource) != 0 ? ValidAllocation(r) : 0;
        }

        private static string AccountingDescription(ResourceInfo r)
        {
            if (r.IsUnityOwned) return "External; excluded";
            if (r.IsPlaced) return "Counted in heap";
            if (r.HasInternalBacking) return "Counted by backing buffer";
            if ((r.Flags & ResourceInfoFlags.Virtual) != 0) return "Requirement only";
            return PhysicalContribution(r) != 0 ? "Included" : "Metadata / CPU";
        }

        private static string FormatFlags(ResourceInfoFlags flags)
        {
            ResourceInfoFlags display = flags & ~ResourceInfoFlags.GpuUseCompleted;
            return display == ResourceInfoFlags.None ? "None" : display.ToString();
        }

        private static GUIStyle DetailsTitleStyle => _detailsTitleStyle ??
            (_detailsTitleStyle = CreateReadableStyle(EditorStyles.boldLabel, true));

        private static GUIStyle DetailsValueStyle => _detailsValueStyle ??
            (_detailsValueStyle = CreateReadableStyle(EditorStyles.label, true));

        private static GUIStyle CreateReadableStyle(GUIStyle source, bool wordWrap)
        {
            var style = new GUIStyle(source) { wordWrap = wordWrap };
            Color color = EditorGUIUtility.isProSkin
                ? new Color(0.82f, 0.82f, 0.82f, 1f)
                : new Color(0.12f, 0.12f, 0.12f, 1f);
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private static void WrappedDetailLabel(string text, GUIStyle style, float width)
        {
            var content = new GUIContent(text ?? "");
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight, style.CalcHeight(content, width));
            Rect rect = GUILayoutUtility.GetRect(content, style,
                GUILayout.Width(width), GUILayout.Height(height));
            GUI.Label(rect, content, style);
        }

        private static void Detail(string label, string value)
        {
            const float gap = 8;
            float labelWidth = Mathf.Clamp(_detailsContentWidth * 0.31f, 96, 128);
            float valueWidth = Mathf.Max(40, _detailsContentWidth - labelWidth - gap);
            var labelContent = new GUIContent(label ?? "");
            var valueContent = new GUIContent(value ?? "");
            GUIStyle labelStyle = DetailsValueStyle;
            GUIStyle valueStyle = DetailsValueStyle;
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight,
                Mathf.Max(labelStyle.CalcHeight(labelContent, labelWidth),
                    valueStyle.CalcHeight(valueContent, valueWidth)));
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Width(_detailsContentWidth), GUILayout.Height(height));
            GUI.Label(new Rect(row.x, row.y, labelWidth, height), labelContent, labelStyle);
            GUI.Label(new Rect(row.x + labelWidth + gap, row.y, valueWidth, height), valueContent, valueStyle);
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0) return "0 B";
            string[] suffixes = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = bytes;
            int suffix = 0;
            while (value >= 1024 && suffix < suffixes.Length - 1) { value /= 1024; ++suffix; }
            return $"{value:0.##} {suffixes[suffix]}";
        }

        private static string FormatAllocation(ResourceInfo r) =>
            r.AllocationSize == ulong.MaxValue ? "Unavailable" : FormatBytes(r.AllocationSize);
    }
}
