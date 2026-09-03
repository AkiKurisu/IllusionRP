using System;
using System.Collections.Generic;
using UnityRhi.Interop;
using UnityEditor;
using UnityEngine;

namespace UnityRhi.EditorTools
{
    public sealed class UnityRhiFrameDebuggerWindow : EditorWindow
    {
        private CommandFrameCapture _capture;
        private ResourceInfo[] _resources = Array.Empty<ResourceInfo>();
        private readonly Dictionary<ulong, ResourceInfo> _resourceLookup = new Dictionary<ulong, ResourceInfo>();
        private EffectiveState[][] _effectiveStates = Array.Empty<EffectiveState[]>();
        private string[][] _markerPaths = Array.Empty<string[]>();
        private Vector2 _treeScroll;
        private Vector2 _detailsScroll;
        private int _selectedList = -1;
        private int _selectedEvent = -1;
        private string _search = "";
        private readonly HashSet<string> _collapsed = new HashSet<string>();
        private readonly List<DetailRow> _detailRows = new List<DetailRow>(64);
        private readonly List<TreeRow> _treeRows = new List<TreeRow>(256);

        [MenuItem("Window/Analysis/UnityRHI Frame Debugger")]
        public static void Open()
        {
            var window = GetWindow<UnityRhiFrameDebuggerWindow>("UnityRHI Frame Debugger");
            window.minSize = new Vector2(820, 520);
        }

        private void OnEnable() => EditorApplication.update += OnEditorUpdate;

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (CommandList.FrameCaptureRequested)
                CommandList.CancelFrameCapture();
        }

        private void OnEditorUpdate()
        {
            CommandFrameCapture capture = CommandList.GetFrameCapture();
            if (capture != null && (_capture == null || capture.FrameIndex != _capture.FrameIndex))
            {
                _capture = capture;
                _resources = capture.Resources ?? Array.Empty<ResourceInfo>();
                _resourceLookup.Clear();
                foreach (ResourceInfo resource in _resources)
                    _resourceLookup[resource.Handle] = resource;
                BuildCaptureCaches();
                _selectedList = capture.CommandLists.Length == 0 ? -1 : 0;
                _selectedEvent = -1;
                Repaint();
            }
            else if (CommandList.FrameCaptureRequested)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (!RhiCore.IsD3D12Active)
            {
                EditorGUILayout.HelpBox("UnityRHI native D3D12 device is not active.", MessageType.Warning);
                return;
            }
            if (_capture == null)
            {
                EditorGUILayout.HelpBox(CommandList.FrameCaptureRequested
                    ? "Capturing the next UnityRHI frame..."
                    : "Click Capture Next Frame to record submitted CmdLists, marker hierarchy and command arguments.",
                    MessageType.Info);
                return;
            }

            Rect content = EditorGUILayout.GetControlRect(false, Mathf.Max(300, position.height - 48));
            float leftWidth = Mathf.Clamp(content.width * 0.34f, 300, 430);
            Rect tree = new Rect(content.x, content.y, leftWidth - 2, content.height);
            Rect right = new Rect(tree.xMax + 4, content.y, content.width - leftWidth - 2, content.height);
            float previewHeight = Mathf.Clamp(right.height * 0.36f, 180, 250);
            Rect preview = new Rect(right.x, right.y, right.width, previewHeight);
            Rect details = new Rect(right.x, preview.yMax + 4, right.width, right.height - previewHeight - 4);
            DrawTree(tree);
            DrawPreview(preview);
            DrawDetails(details);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(CommandList.FrameCaptureRequested || !RhiCore.IsD3D12Active))
                {
                    if (GUILayout.Button("Capture", EditorStyles.toolbarButton, GUILayout.Width(72)))
                    {
                        _capture = null;
                        _selectedList = -1;
                        _selectedEvent = -1;
                        CommandList.RequestFrameCapture();
                    }
                }
                if (CommandList.FrameCaptureRequested &&
                    GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    CommandList.CancelFrameCapture();
                GUILayout.Space(8);
                GUILayout.Label("Editor", EditorStyles.toolbarPopup, GUILayout.Width(150));
                GUILayout.FlexibleSpace();
                if (_capture != null)
                {
                    GUILayout.Label($"Frame {_capture.FrameIndex:N0}", EditorStyles.miniLabel);
                    GUILayout.Space(8);
                    GUILayout.Label($"CmdLists {_capture.CommandLists.Length:N0}", EditorStyles.miniLabel);
                    GUILayout.Space(8);
                    GUILayout.Label($"Events {TotalEvents():N0}", EditorStyles.miniLabel);
                    GUILayout.Space(8);
                    GUILayout.Label($"Buffer uploads {FormatBytes(TotalBufferUploadBytes())}", EditorStyles.miniLabel);
                    GUILayout.Space(8);
                    using (new EditorGUI.DisabledScope(!TryGetSelection(out _, out _)))
                    {
                        if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(25))) SelectAdjacent(-1);
                        if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(25))) SelectAdjacent(1);
                    }
                }
                else GUILayout.Label(CommandList.FrameCaptureRequested ? "Capturing next frame..." : "Not captured",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawTree(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect toolbar = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, 20);
            GUI.Box(toolbar, GUIContent.none, EditorStyles.toolbar);
            _search = GUI.TextField(new Rect(toolbar.x + 3, toolbar.y + 2, toolbar.width - 6, 17),
                _search, EditorStyles.toolbarSearchField);

            BuildTreeRows();
            Rect scrollRect = new Rect(rect.x + 4, toolbar.yMax + 2,
                rect.width - 8, rect.height - toolbar.height - 10);
            const float rowHeight = 19;
            float rowsHeight = _treeRows.Count * rowHeight;
            bool needsVerticalScrollbar = rowsHeight > scrollRect.height;
            float viewWidth = Mathf.Max(100, scrollRect.width - (needsVerticalScrollbar ? 16 : 0));
            Rect view = new Rect(0, 0, viewWidth, Mathf.Max(scrollRect.height, rowsHeight));
            _treeScroll = GUI.BeginScrollView(scrollRect, _treeScroll, view, false, needsVerticalScrollbar);
            for (int i = 0; i < _treeRows.Count; ++i)
            {
                TreeRow item = _treeRows[i];
                DrawTreeRow(new Rect(0, i * rowHeight, viewWidth, rowHeight), item);
            }
            GUI.EndScrollView();
        }

        private void BuildTreeRows()
        {
            _treeRows.Clear();
            for (int listIndex = 0; listIndex < _capture.CommandLists.Length; ++listIndex)
            {
                CommandListFrameStats list = _capture.CommandLists[listIndex];
                string listKey = $"list:{listIndex}";
                bool listExpanded = !_collapsed.Contains(listKey);
                _treeRows.Add(new TreeRow(0, $"CmdList {list.Index}", list.Events.Length.ToString(),
                    FormatBytes(list.ByteSize), true, listKey, listExpanded, listIndex, -1));

                bool[] visibleDepth = new bool[64];
                visibleDepth[0] = listExpanded;
                for (int eventIndex = 0; eventIndex < list.Events.Length; ++eventIndex)
                {
                    CommandTraceEvent trace = list.Events[eventIndex];
                    if (trace.Opcode == CommandOpcode.EndMarker)
                        continue;
                    int depth = Mathf.Clamp((int)trace.Depth, 0, visibleDepth.Length - 2);
                    string eventLabel = EventLabel(trace, listIndex, eventIndex);
                    bool visible = string.IsNullOrWhiteSpace(_search)
                        ? visibleDepth[depth]
                        : EventMatches(trace, eventLabel, _search);
                    bool marker = trace.Opcode == CommandOpcode.BeginMarker;
                    string markerKey = $"{listIndex}:{eventIndex}";
                    bool expanded = !_collapsed.Contains(markerKey);
                    if (marker)
                        visibleDepth[depth + 1] = visible && expanded;
                    if (!visible)
                        continue;
                    string containedEventCount = marker
                        ? CountMarkerCommands(list.Events, eventIndex).ToString("N0")
                        : "";
                    _treeRows.Add(new TreeRow(depth + 1, eventLabel, containedEventCount,
                        EventSizeLabel(trace), marker, markerKey, expanded, listIndex, eventIndex));
                }
            }
        }

        private void DrawTreeRow(Rect row, TreeRow item)
        {
            bool selected = _selectedList == item.ListIndex && _selectedEvent == item.EventIndex;
            if (selected) EditorGUI.DrawRect(row, new Color(0.20f, 0.43f, 0.70f, 0.55f));
            else if (((int)row.y & 1) == 0) EditorGUI.DrawRect(row, new Color(0, 0, 0, 0.035f));
            float x = row.x + 4 + item.Depth * 14;
            if (item.Group)
            {
                if (GUI.Button(new Rect(x, row.y + 1, 14, 17), item.Expanded ? "▼" : "▶", EditorStyles.miniLabel))
                {
                    if (item.Expanded) _collapsed.Add(item.Key); else _collapsed.Remove(item.Key);
                }
            }
            // Reserve the foldout glyph width for leaf rows as well. Without
            // this placeholder, commands one level below a marker visually
            // align with the marker label instead of appearing as children.
            x += 15;
            const float countWidth = 42;
            const float sizeWidth = 82;
            const float columnGap = 4;
            float countX = row.xMax - sizeWidth - countWidth - columnGap;
            float labelWidth = Mathf.Max(20, countX - x - columnGap);
            if (GUI.Button(new Rect(x, row.y, labelWidth, row.height),
                new GUIContent(item.Label, item.Label), item.Group ? TreeGroupLabelStyle : TreeLabelStyle))
            {
                _selectedList = item.ListIndex;
                _selectedEvent = item.EventIndex;
            }
            if (!string.IsNullOrEmpty(item.Count))
                GUI.Label(new Rect(countX, row.y, countWidth, row.height), item.Count, RightMiniStyle);
            GUI.Label(new Rect(row.xMax - sizeWidth, row.y, sizeWidth - 4, row.height), item.Size, RightMiniStyle);
        }

        private void DrawPreview(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(rect.x + 7, rect.y + 5, rect.width - 14, 19), "Event Summary", EditorStyles.boldLabel);
            Rect inner = new Rect(rect.x + 8, rect.y + 27, rect.width - 16, rect.height - 35);
            EditorGUI.DrawRect(inner, new Color(0.105f, 0.105f, 0.105f, 1));
            if (!TryGetSelection(out CommandListFrameStats list, out CommandTraceEvent trace))
            {
                DrawFrameSummary(inner);
                return;
            }
            float ratio = list.ByteSize == 0 ? 0 : trace.ByteSize / (float)list.ByteSize;
            EffectiveState state = GetEffectiveState(_selectedList, _selectedEvent);
            Rect bar = new Rect(inner.x + 22, inner.yMax - 38, inner.width - 44, 12);
            EditorGUI.DrawRect(bar, new Color(0.18f, 0.18f, 0.18f, 1));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height),
                new Color(0.20f, 0.52f, 0.84f, 0.9f));
            GUI.Label(new Rect(inner.x + 12, inner.y + 10, inner.width - 24, 25), TraceLabel(trace), PreviewTitleStyle);
            GUI.Label(new Rect(inner.x + 16, inner.y + 42, inner.width - 32, 18),
                $"Event #{_selectedEvent + 1:N0}  •  {Category(trace.Opcode)}  •  {MarkerPath(_selectedList, _selectedEvent)}",
                CenteredMiniStyle);
            GUI.Label(new Rect(inner.x + 16, inner.y + 66, inner.width - 32, 18),
                trace.Opcode == CommandOpcode.WriteBuffer
                    ? $"Upload: {FormatBytes(trace.UploadByteSize)}  →  {ResourceName(Argument(trace, 0))}"
                    : $"Pipeline: {ResourceName(state.Pipeline)}", CenteredMiniStyle);
            GUI.Label(new Rect(inner.x + 16, inner.y + 86, inner.width - 32, 18),
                trace.Opcode == CommandOpcode.WriteBuffer
                    ? $"Destination offset: {FormatBytes(Argument(trace, 1))}  •  Ticket: {Hex(Argument(trace, 2))}"
                    : state.Framebuffer == 0 ? $"Bindings: {state.BindingCount:N0}" :
                        $"Framebuffer: {ResourceName(state.Framebuffer)}  •  Bindings: {state.BindingCount:N0}", CenteredMiniStyle);
            GUI.Label(new Rect(bar.x, bar.yMax + 2, bar.width, 18),
                $"Encoded {FormatBytes(trace.ByteSize)} / {FormatBytes(list.ByteSize)} ({ratio * 100:0.00}%)",
                CenteredMiniStyle);
        }

        private void DrawDetails(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect content = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16);
            BuildDetailRows();
            float contentHeight = 6;
            foreach (DetailRow row in _detailRows)
                contentHeight += row.Section ? 27 : 20;
            contentHeight = Mathf.Max(content.height, contentHeight + 6);
            Rect view = new Rect(0, 0, Mathf.Max(100, content.width - 16), contentHeight);
            _detailsScroll = GUI.BeginScrollView(content, _detailsScroll, view);
            float y = 4;
            for (int i = 0; i < _detailRows.Count; ++i)
            {
                DetailRow row = _detailRows[i];
                float height = row.Section ? 27 : 20;
                Rect line = new Rect(4, y, view.width - 8, height);
                if (row.Section)
                {
                    if (i != 0) GUI.Box(new Rect(line.x, line.y + 2, line.width, 1), GUIContent.none);
                    GUI.Label(new Rect(line.x + 2, line.y + 7, line.width - 4, 19), row.Label, EditorStyles.boldLabel);
                }
                else
                {
                    if ((i & 1) != 0) EditorGUI.DrawRect(line, new Color(0, 0, 0, 0.045f));
                    float labelWidth = Mathf.Clamp(line.width * 0.31f, 120, 210);
                    GUI.Label(new Rect(line.x + 3, line.y + 1, labelWidth - 6, 18),
                        new GUIContent(row.Label, row.Label), EditorStyles.miniLabel);
                    GUI.Label(new Rect(line.x + labelWidth, line.y + 1, line.width - labelWidth - 4, 18),
                        new GUIContent(row.Value, row.Value), EditorStyles.label);
                }
                y += height;
            }
            GUI.EndScrollView();
        }

        private void BuildDetailRows()
        {
            _detailRows.Clear();
            if (_selectedList < 0 || _selectedList >= _capture.CommandLists.Length)
            {
                Detail("Selection", "Select a CmdList or command.");
                return;
            }
            CommandListFrameStats list = _capture.CommandLists[_selectedList];
            if (_selectedEvent < 0 || _selectedEvent >= list.Events.Length)
            {
                Section($"CmdList {list.Index}");
                Detail("Stream size", FormatBytes(list.ByteSize));
                Detail("Upload payload", FormatBytes(list.UploadBytes));
                Detail("Buffer upload", $"{FormatBytes(BufferUploadBytes(list))}  •  {Count(list, CommandOpcode.WriteBuffer):N0} writes");
                Detail("Commands", list.Events.Length.ToString("N0"));
                Detail("Markers", Count(list, CommandOpcode.BeginMarker).ToString("N0"));
                Detail("Draw calls", CountCategory(list, "Draw").ToString("N0"));
                Detail("Dispatches", CountCategory(list, "Dispatch").ToString("N0"));
                Section("Command breakdown");
                foreach (CommandOpcodeFrameStats command in list.Commands)
                    Detail(command.Opcode.ToString(), $"{command.Count:N0}  •  {FormatBytes(command.ByteSize)}");
                return;
            }

            CommandTraceEvent trace = list.Events[_selectedEvent];
            Section(TraceLabel(trace));
            Detail("Event", $"#{_selectedEvent + 1:N0} of {list.Events.Length:N0}");
            Detail("Category", Category(trace.Opcode));
            Detail("Marker path", MarkerPath(_selectedList, _selectedEvent));
            Detail("Opcode", $"{trace.Opcode} ({(uint)trace.Opcode})");
            Detail("Stream offset", $"{trace.ByteOffset:N0} B");
            Detail("Encoded size", FormatBytes(trace.ByteSize));
            Detail("CmdList share", list.ByteSize == 0 ? "0%" :
                $"{trace.ByteSize * 100.0 / list.ByteSize:0.00}%");
            Detail("Marker depth", trace.Depth.ToString());
            DrawArguments(trace);
            DrawEffectiveState(trace);
            DrawReferencedResources(trace);
        }

        private bool TryGetSelection(out CommandListFrameStats list, out CommandTraceEvent trace)
        {
            list = default;
            trace = default;
            if (_capture == null || _selectedList < 0 || _selectedList >= _capture.CommandLists.Length)
                return false;
            list = _capture.CommandLists[_selectedList];
            if (_selectedEvent < 0 || _selectedEvent >= list.Events.Length)
                return false;
            trace = list.Events[_selectedEvent];
            return true;
        }

        private void DrawArguments(CommandTraceEvent e)
        {
            ulong[] a = e.Arguments ?? Array.Empty<ulong>();
            Func<int, ulong> value = i => i < a.Length ? a[i] : 0;
            Section("Arguments");
            switch (e.Opcode)
            {
                case CommandOpcode.BeginMarker: Detail("Name", e.Label); Detail("UTF-8 bytes", value(0).ToString()); break;
                case CommandOpcode.CopyBuffer:
                    Resource("Destination", value(0)); Detail("Destination offset", FormatBytes(value(1)));
                    Resource("Source", value(2)); Detail("Source offset", FormatBytes(value(3)));
                    Detail("Copy size", FormatBytes(value(4))); break;
                case CommandOpcode.Dispatch:
                case CommandOpcode.DispatchRays:
                    Detail("X", value(0).ToString()); Detail("Y", value(1).ToString()); Detail("Z", value(2).ToString()); break;
                case CommandOpcode.SetComputeState:
                    Resource("Pipeline", value(0)); Resource("Indirect parameters", value(1));
                    Detail("Binding sets", value(2).ToString()); Detail("Reuse bindings", value(3) != 0 ? "Yes" : "No"); break;
                case CommandOpcode.SetGraphicsState:
                    Resource("Pipeline", value(0)); Resource("Framebuffer", value(1)); Resource("Index buffer", value(2));
                    Detail("Vertex buffers", value(3).ToString()); Resource("Indirect parameters", value(4));
                    Resource("Indirect count buffer", value(5)); Detail("Binding sets", value(6).ToString());
                    Detail("Index format / offset", $"{(Format)(uint)value(7)} / {FormatBytes(value(8))}");
                    Detail("Viewport X", $"{Float(value(9)):0.###} .. {Float(value(10)):0.###}");
                    Detail("Viewport Y", $"{Float(value(11)):0.###} .. {Float(value(12)):0.###}");
                    Detail("Depth range", $"{Float(value(13)):0.###} .. {Float(value(14)):0.###}");
                    Detail("Blend constant", $"({Float(value(15)):0.###}, {Float(value(16)):0.###}, {Float(value(17)):0.###}, {Float(value(18)):0.###})"); break;
                case CommandOpcode.SetRayTracingState:
                    Resource("Shader table", value(0)); Detail("Binding sets", value(1).ToString()); break;
                case CommandOpcode.WriteBuffer:
                    Resource("Buffer", value(0)); Detail("Destination offset", FormatBytes(value(1)));
                    Detail("Upload size", FormatBytes(e.UploadByteSize));
                    Detail("Upload ticket", Hex(value(2))); break;
                case CommandOpcode.WriteTexture:
                    Resource("Texture", value(0)); Detail("Array slice", value(1).ToString());
                    Detail("Mip level", value(2).ToString()); Detail("Upload size", FormatBytes(e.UploadByteSize));
                    Detail("Upload ticket", Hex(value(3))); break;
                case CommandOpcode.SetPushConstants: Detail("Payload size", FormatBytes(value(0))); break;
                case CommandOpcode.BuildBottomLevelAccelStruct:
                case CommandOpcode.BuildTopLevelAccelStruct:
                    Resource("Acceleration structure", value(0)); Detail("Elements", value(1).ToString());
                    Detail("Build flags", Hex(value(2))); break;
                case CommandOpcode.BuildTopLevelAccelStructFromBuffer:
                    Resource("Acceleration structure", value(0)); Resource("Instance buffer", value(1));
                    Detail("Buffer offset", FormatBytes(value(2))); Detail("Instances", value(3).ToString());
                    Detail("Build flags", Hex(value(4))); break;
                case CommandOpcode.Draw:
                    Detail("Vertex count", value(0).ToString()); Detail("Instance count", value(1).ToString());
                    Detail("Start vertex", value(2).ToString()); Detail("Start instance", value(3).ToString()); break;
                case CommandOpcode.DrawIndexed:
                    Detail("Index count", value(0).ToString()); Detail("Instance count", value(1).ToString());
                    Detail("Start index", value(2).ToString()); Detail("Base vertex", value(3).ToString());
                    Detail("Start instance", value(4).ToString()); break;
                case CommandOpcode.CopyTextureToBuffer:
                    Resource("Destination buffer", value(0)); Detail("Destination offset", FormatBytes(value(1)));
                    Resource("Source texture", value(2)); Detail("Array slice", value(3).ToString());
                    Detail("Mip level", value(4).ToString()); break;
                case CommandOpcode.CopyTexture:
                case CommandOpcode.CopyTextureFromStaging:
                case CommandOpcode.CopyTextureToStaging:
                    Resource("Destination", value(0)); Resource("Source", value(1));
                    Detail("Destination mip / slice", $"{value(2)} / {value(3)}");
                    Detail("Source mip / slice", $"{value(4)} / {value(5)}");
                    Detail("Source extent", $"{value(6)} × {value(7)}"); break;
                case CommandOpcode.ResolveTexture:
                    Resource("Destination", value(0)); Resource("Source", value(1));
                    Detail("Destination mip", value(2).ToString()); Detail("Source mip", value(3).ToString()); break;
                case CommandOpcode.BeginTrackingTextureState:
                case CommandOpcode.SetTextureSubresourceState:
                    Resource("Texture", value(0)); Detail("State", ((ResourceStates)(uint)value(1)).ToString());
                    Detail("Mip range", $"{value(2)} + {value(3)}");
                    Detail("Array range", $"{value(4)} + {value(5)}"); break;
                case CommandOpcode.SetBufferState:
                case CommandOpcode.SetTextureState:
                case CommandOpcode.BeginTrackingBufferState:
                case CommandOpcode.SetPermanentTextureState:
                case CommandOpcode.SetPermanentBufferState:
                    Resource("Resource", value(0)); Detail("State", ((ResourceStates)(uint)value(1)).ToString()); break;
                case CommandOpcode.SetEnableAutomaticBarriers:
                    Detail("Automatic barriers", value(0) != 0 ? "Enabled" : "Disabled"); break;
                case CommandOpcode.SetEnableUavBarriersForTexture:
                case CommandOpcode.SetEnableUavBarriersForBuffer:
                    Resource("Resource", value(0)); Detail("UAV barriers", value(1) != 0 ? "Enabled" : "Disabled"); break;
                case CommandOpcode.DrawIndirect:
                case CommandOpcode.DrawIndexedIndirect:
                    Detail("Argument offset", FormatBytes(value(0))); Detail("Draw count", value(1).ToString("N0")); break;
                case CommandOpcode.DrawIndexedIndirectCount:
                    Detail("Argument offset", FormatBytes(value(0))); Detail("Maximum draws", value(1).ToString("N0"));
                    Detail("Count offset", FormatBytes(value(2))); break;
                case CommandOpcode.ClearTextureFloat:
                    Resource("Texture", value(0));
                    Detail("Clear color", $"({Float(value(1)):0.###}, {Float(value(2)):0.###}, {Float(value(3)):0.###}, {Float(value(4)):0.###})"); break;
                case CommandOpcode.ClearTextureFloatSubresources:
                    Resource("Texture", value(0)); DrawSubresources(value);
                    Detail("Clear color", $"({Float(value(5)):0.###}, {Float(value(6)):0.###}, {Float(value(7)):0.###}, {Float(value(8)):0.###})"); break;
                case CommandOpcode.ClearTextureUInt:
                    Resource("Texture", value(0)); DrawSubresources(value); Detail("Clear value", value(5).ToString()); break;
                case CommandOpcode.ClearDepthStencilTexture:
                    Resource("Texture", value(0)); Detail("Clear depth", value(1) != 0 ? Float(value(2)).ToString("0.###") : "No");
                    Detail("Clear stencil", value(3) != 0 ? value(4).ToString() : "No"); break;
                case CommandOpcode.ClearDepthStencilTextureSubresources:
                    Resource("Texture", value(0)); DrawSubresources(value);
                    Detail("Clear depth", value(5) != 0 ? Float(value(6)).ToString("0.###") : "No");
                    Detail("Clear stencil", value(7) != 0 ? value(8).ToString() : "No"); break;
                case CommandOpcode.UavBarrier:
                case CommandOpcode.DispatchIndirect:
                case CommandOpcode.BeginTimerQuery:
                case CommandOpcode.EndTimerQuery:
                    Resource("Resource", value(0)); break;
                default:
                    bool any = false;
                    for (int i = 0; i < a.Length; ++i)
                        if (a[i] != 0) { Detail($"Argument {i}", $"{a[i]:N0}  ({Hex(a[i])})"); any = true; }
                    if (!any) Detail("Payload", "No payload arguments");
                    break;
            }
        }

        private void Resource(string label, ulong handle)
        {
            if (handle == 0) { Detail(label, "None"); return; }
            if (_resourceLookup.TryGetValue(handle, out ResourceInfo resource))
            {
                Detail(label, $"{resource.DebugName ?? "<unnamed>"}  [{resource.Kind}]  {Hex(handle)}");
                return;
            }
            Detail(label, Hex(handle));
        }

        private static string TraceLabel(CommandTraceEvent trace) =>
            trace.Opcode == CommandOpcode.BeginMarker
                ? trace.Label
                : trace.Opcode.ToString();

        private string EventLabel(CommandTraceEvent trace, int listIndex, int eventIndex)
        {
            string suffix = "";
            EffectiveState state = GetEffectiveState(listIndex, eventIndex);
            switch (trace.Opcode)
            {
                case CommandOpcode.Draw:
                case CommandOpcode.DrawIndexed:
                case CommandOpcode.DrawIndirect:
                case CommandOpcode.DrawIndexedIndirect:
                case CommandOpcode.DrawIndexedIndirectCount:
                case CommandOpcode.Dispatch:
                case CommandOpcode.DispatchIndirect:
                case CommandOpcode.DispatchRays:
                    suffix = state.Pipeline == 0 ? "" : $" — {ResourceName(state.Pipeline)}";
                    break;
                case CommandOpcode.WriteBuffer:
                    suffix = $" — {ResourceName(Argument(trace, 0))}";
                    break;
            }
            return $"{TraceLabel(trace)}{suffix}";
        }

        private bool EventMatches(CommandTraceEvent trace, string eventLabel, string search)
        {
            if (eventLabel.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            foreach (ulong handle in ReferencedHandles(trace))
                if (ResourceName(handle).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private void DrawFrameSummary(Rect rect)
        {
            ulong streamBytes = 0, uploadBytes = 0, bufferUploadBytes = 0;
            int draws = 0, dispatches = 0, markers = 0, bufferWrites = 0;
            foreach (CommandListFrameStats list in _capture.CommandLists)
            {
                streamBytes += list.ByteSize;
                uploadBytes += list.UploadBytes;
                bufferUploadBytes += BufferUploadBytes(list);
                draws += CountCategory(list, "Draw");
                dispatches += CountCategory(list, "Dispatch");
                markers += Count(list, CommandOpcode.BeginMarker);
                bufferWrites += Count(list, CommandOpcode.WriteBuffer);
            }
            GUI.Label(new Rect(rect.x + 12, rect.y + 12, rect.width - 24, 25),
                $"Captured Frame {_capture.FrameIndex:N0}", PreviewTitleStyle);
            GUI.Label(new Rect(rect.x + 12, rect.y + 48, rect.width - 24, 20),
                $"{TotalEvents():N0} events  •  {draws:N0} draws  •  {dispatches:N0} dispatches  •  {markers:N0} markers",
                CenteredMiniStyle);
            GUI.Label(new Rect(rect.x + 12, rect.y + 73, rect.width - 24, 20),
                $"Command stream {FormatBytes(streamBytes)}  •  Upload payload {FormatBytes(uploadBytes)}  •  {_resources.Length:N0} live resources",
                CenteredMiniStyle);
            GUI.Label(new Rect(rect.x + 12, rect.y + 98, rect.width - 24, 20),
                $"Buffer uploads {FormatBytes(bufferUploadBytes)}  •  {bufferWrites:N0} WriteBuffer commands",
                CenteredMiniStyle);
            GUI.Label(new Rect(rect.x + 12, rect.yMax - 33, rect.width - 24, 20),
                "Select an event to inspect its arguments, inherited pipeline state, and resources.", CenteredMiniStyle);
        }

        private void DrawEffectiveState(CommandTraceEvent trace)
        {
            EffectiveState state = GetEffectiveState(_selectedList, _selectedEvent);
            if (state.Pipeline == 0 && state.Framebuffer == 0 && state.BindingCount == 0)
                return;
            Section("Effective pipeline state");
            Resource("Pipeline", state.Pipeline);
            if (state.Framebuffer != 0) Resource("Framebuffer", state.Framebuffer);
            if (state.IndexBuffer != 0) Resource("Index buffer", state.IndexBuffer);
            if (state.IndirectParameters != 0) Resource("Indirect parameters", state.IndirectParameters);
            Detail("Binding sets", state.BindingCount.ToString("N0"));
            if (trace.Opcode != CommandOpcode.SetComputeState &&
                trace.Opcode != CommandOpcode.SetGraphicsState && trace.Opcode != CommandOpcode.SetRayTracingState)
                Detail("State source", state.SourceEvent < 0 ? "None" : $"Event #{state.SourceEvent + 1:N0}");
        }

        private void DrawReferencedResources(CommandTraceEvent trace)
        {
            ulong[] handles = ReferencedHandles(trace);
            EffectiveState state = GetEffectiveState(_selectedList, _selectedEvent);
            if (Category(trace.Opcode) == "Draw" || Category(trace.Opcode) == "Dispatch")
                handles = MergeHandles(handles, state.RelatedResources);
            if (handles.Length == 0)
                return;
            Section($"Referenced resources ({handles.Length:N0})");
            foreach (ulong handle in handles)
            {
                if (!_resourceLookup.TryGetValue(handle, out ResourceInfo resource))
                {
                    Detail(Hex(handle), "Not present in post-frame resource snapshot");
                    continue;
                }
                string name = string.IsNullOrEmpty(resource.DebugName) ? "<unnamed>" : resource.DebugName;
                Detail(name, $"{resource.Kind}  •  {ResourceSummary(resource)}");
                Detail("  Handle", Hex(resource.Handle));
                Detail("  Memory", $"logical {FormatBytes(resource.LogicalSize)}, allocation {FormatBytes(resource.AllocationSize)}");
                Detail("  State", $"initial {resource.InitialState}, permanent {resource.PermanentState}");
                Detail("  Flags", resource.Flags.ToString());
            }
        }

        private ulong[] ReferencedHandles(CommandTraceEvent trace)
        {
            var result = new List<ulong>();
            var seen = new HashSet<ulong>();
            Action<ulong> add = handle => { if (handle != 0 && seen.Add(handle)) result.Add(handle); };
            foreach (ulong handle in trace.Resources ?? Array.Empty<ulong>()) add(handle);
            ulong[] a = trace.Arguments ?? Array.Empty<ulong>();
            Func<int, ulong> value = i => i < a.Length ? a[i] : 0;
            switch (trace.Opcode)
            {
                case CommandOpcode.CopyBuffer: add(value(0)); add(value(2)); break;
                case CommandOpcode.CopyTexture:
                case CommandOpcode.CopyTextureFromStaging:
                case CommandOpcode.CopyTextureToStaging:
                case CommandOpcode.ResolveTexture: add(value(0)); add(value(1)); break;
                case CommandOpcode.CopyTextureToBuffer: add(value(0)); add(value(2)); break;
                case CommandOpcode.BuildTopLevelAccelStructFromBuffer: add(value(0)); add(value(1)); break;
                case CommandOpcode.SetComputeState: add(value(0)); add(value(1)); break;
                case CommandOpcode.SetGraphicsState:
                    add(value(0)); add(value(1)); add(value(2)); add(value(4)); add(value(5)); break;
                case CommandOpcode.SetRayTracingState:
                case CommandOpcode.WriteBuffer:
                case CommandOpcode.WriteTexture:
                case CommandOpcode.BuildBottomLevelAccelStruct:
                case CommandOpcode.BuildTopLevelAccelStruct:
                case CommandOpcode.SetBufferState:
                case CommandOpcode.SetTextureState:
                case CommandOpcode.BeginTrackingTextureState:
                case CommandOpcode.BeginTrackingBufferState:
                case CommandOpcode.SetPermanentTextureState:
                case CommandOpcode.SetPermanentBufferState:
                case CommandOpcode.SetEnableUavBarriersForTexture:
                case CommandOpcode.SetEnableUavBarriersForBuffer:
                case CommandOpcode.SetTextureSubresourceState:
                case CommandOpcode.UavBarrier:
                case CommandOpcode.DispatchIndirect:
                case CommandOpcode.BeginTimerQuery:
                case CommandOpcode.EndTimerQuery:
                case CommandOpcode.ClearBufferUInt:
                case CommandOpcode.ClearTextureFloat:
                case CommandOpcode.ClearTextureFloatSubresources:
                case CommandOpcode.ClearTextureUInt:
                case CommandOpcode.ClearDepthStencilTexture:
                case CommandOpcode.ClearDepthStencilTextureSubresources: add(value(0)); break;
            }
            return result.ToArray();
        }

        private EffectiveState GetEffectiveState(int listIndex, int eventIndex)
        {
            if (listIndex < 0 || listIndex >= _effectiveStates.Length || eventIndex < 0 ||
                eventIndex >= _effectiveStates[listIndex].Length)
                return new EffectiveState { SourceEvent = -1, RelatedResources = Array.Empty<ulong>() };
            return _effectiveStates[listIndex][eventIndex];
        }

        private string MarkerPath(int listIndex, int eventIndex)
        {
            if (listIndex < 0 || listIndex >= _markerPaths.Length || eventIndex < 0 ||
                eventIndex >= _markerPaths[listIndex].Length)
                return "<root>";
            return _markerPaths[listIndex][eventIndex];
        }

        private void BuildCaptureCaches()
        {
            _effectiveStates = new EffectiveState[_capture.CommandLists.Length][];
            _markerPaths = new string[_capture.CommandLists.Length][];
            for (int listIndex = 0; listIndex < _capture.CommandLists.Length; ++listIndex)
            {
                CommandTraceEvent[] events = _capture.CommandLists[listIndex].Events;
                _effectiveStates[listIndex] = new EffectiveState[events.Length];
                _markerPaths[listIndex] = new string[events.Length];
                var state = new EffectiveState { SourceEvent = -1, RelatedResources = Array.Empty<ulong>() };
                var markers = new List<string>();
                for (int eventIndex = 0; eventIndex < events.Length; ++eventIndex)
                {
                    CommandTraceEvent trace = events[eventIndex];
                    ulong[] a = trace.Arguments ?? Array.Empty<ulong>();
                    Func<int, ulong> value = n => n < a.Length ? a[n] : 0;
                    if (trace.Opcode == CommandOpcode.BeginMarker) markers.Add(trace.Label);
                    else if (trace.Opcode == CommandOpcode.EndMarker && markers.Count != 0) markers.RemoveAt(markers.Count - 1);
                    _markerPaths[listIndex][eventIndex] = markers.Count == 0 ? "<root>" : string.Join(" / ", markers.ToArray());

                    if (trace.Opcode == CommandOpcode.ClearState)
                        state = new EffectiveState { SourceEvent = -1, RelatedResources = Array.Empty<ulong>() };
                    else if (trace.Opcode == CommandOpcode.SetComputeState)
                    {
                        bool reuseBindings = value(3) != 0;
                        ulong[] reusedResources = reuseBindings
                            ? RemoveHandles(state.RelatedResources, state.Pipeline, state.Framebuffer,
                                state.IndexBuffer, state.IndirectParameters)
                            : Array.Empty<ulong>();
                        state.Pipeline = value(0); state.Framebuffer = 0; state.IndexBuffer = 0;
                        state.IndirectParameters = value(1); state.BindingCount = (uint)value(2); state.SourceEvent = eventIndex;
                        state.RelatedResources = reuseBindings
                            ? MergeHandles(trace.Resources, reusedResources)
                            : trace.Resources ?? Array.Empty<ulong>();
                    }
                    else if (trace.Opcode == CommandOpcode.SetGraphicsState)
                    {
                        state.Pipeline = value(0); state.Framebuffer = value(1); state.IndexBuffer = value(2);
                        state.IndirectParameters = value(4); state.BindingCount = (uint)value(6); state.SourceEvent = eventIndex;
                        state.RelatedResources = trace.Resources ?? Array.Empty<ulong>();
                    }
                    else if (trace.Opcode == CommandOpcode.SetRayTracingState)
                    {
                        state.Pipeline = value(0); state.Framebuffer = 0; state.IndexBuffer = 0;
                        state.IndirectParameters = 0; state.BindingCount = (uint)value(1); state.SourceEvent = eventIndex;
                        state.RelatedResources = trace.Resources ?? Array.Empty<ulong>();
                    }
                    _effectiveStates[listIndex][eventIndex] = state;
                }
            }
        }

        private static ulong[] MergeHandles(ulong[] first, ulong[] second)
        {
            var result = new List<ulong>();
            var seen = new HashSet<ulong>();
            foreach (ulong handle in first ?? Array.Empty<ulong>()) if (handle != 0 && seen.Add(handle)) result.Add(handle);
            foreach (ulong handle in second ?? Array.Empty<ulong>()) if (handle != 0 && seen.Add(handle)) result.Add(handle);
            return result.ToArray();
        }

        private static ulong[] RemoveHandles(ulong[] source, params ulong[] removed)
        {
            var remove = new HashSet<ulong>(removed);
            var result = new List<ulong>();
            foreach (ulong handle in source ?? Array.Empty<ulong>())
                if (handle != 0 && !remove.Contains(handle)) result.Add(handle);
            return result.ToArray();
        }

        private void SelectAdjacent(int direction)
        {
            if (_capture == null) return;
            int list = _selectedList, index = _selectedEvent;
            while (list >= 0 && list < _capture.CommandLists.Length)
            {
                index += direction;
                if (index < 0) { if (--list < 0) break; index = _capture.CommandLists[list].Events.Length - 1; }
                else if (index >= _capture.CommandLists[list].Events.Length) { if (++list >= _capture.CommandLists.Length) break; index = 0; }
                if (_capture.CommandLists[list].Events[index].Opcode == CommandOpcode.EndMarker) continue;
                _selectedList = list; _selectedEvent = index; Repaint(); return;
            }
        }

        private int TotalEvents()
        {
            int total = 0;
            if (_capture != null) foreach (CommandListFrameStats list in _capture.CommandLists) total += list.Events.Length;
            return total;
        }

        private ulong TotalBufferUploadBytes()
        {
            ulong total = 0;
            if (_capture != null)
                foreach (CommandListFrameStats list in _capture.CommandLists)
                    total += BufferUploadBytes(list);
            return total;
        }

        private static ulong BufferUploadBytes(CommandListFrameStats list)
        {
            ulong total = 0;
            foreach (CommandTraceEvent trace in list.Events)
                if (trace.Opcode == CommandOpcode.WriteBuffer)
                    total += trace.UploadByteSize;
            return total;
        }

        private static string EventSizeLabel(CommandTraceEvent trace) =>
            trace.Opcode == CommandOpcode.WriteBuffer
                ? $"↑ {FormatBytes(trace.UploadByteSize)}"
                : FormatBytes(trace.ByteSize);

        private static ulong Argument(CommandTraceEvent trace, int index) =>
            trace.Arguments != null && index >= 0 && index < trace.Arguments.Length
                ? trace.Arguments[index]
                : 0;

        private static int Count(CommandListFrameStats list, CommandOpcode opcode)
        {
            int count = 0;
            foreach (CommandTraceEvent trace in list.Events) if (trace.Opcode == opcode) ++count;
            return count;
        }

        private static int CountMarkerCommands(CommandTraceEvent[] events, int beginMarkerIndex)
        {
            int nestedMarkers = 1;
            int commands = 0;
            for (int i = beginMarkerIndex + 1; i < events.Length; ++i)
            {
                switch (events[i].Opcode)
                {
                    case CommandOpcode.BeginMarker:
                        ++nestedMarkers;
                        break;
                    case CommandOpcode.EndMarker:
                        if (--nestedMarkers == 0)
                            return commands;
                        break;
                    default:
                        ++commands;
                        break;
                }
            }
            return commands;
        }

        private static int CountCategory(CommandListFrameStats list, string category)
        {
            int count = 0;
            foreach (CommandTraceEvent trace in list.Events) if (Category(trace.Opcode) == category) ++count;
            return count;
        }

        private static string Category(CommandOpcode opcode)
        {
            switch (opcode)
            {
                case CommandOpcode.Draw:
                case CommandOpcode.DrawIndexed:
                case CommandOpcode.DrawIndirect:
                case CommandOpcode.DrawIndexedIndirect:
                case CommandOpcode.DrawIndexedIndirectCount: return "Draw";
                case CommandOpcode.Dispatch:
                case CommandOpcode.DispatchIndirect:
                case CommandOpcode.DispatchRays: return "Dispatch";
                case CommandOpcode.CopyBuffer:
                case CommandOpcode.CopyTexture:
                case CommandOpcode.CopyTextureToBuffer:
                case CommandOpcode.CopyTextureFromStaging:
                case CommandOpcode.CopyTextureToStaging:
                case CommandOpcode.ResolveTexture:
                case CommandOpcode.WriteBuffer:
                case CommandOpcode.WriteTexture: return "Transfer";
                case CommandOpcode.SetBufferState:
                case CommandOpcode.SetTextureState:
                case CommandOpcode.SetTextureSubresourceState:
                case CommandOpcode.CommitBarriers:
                case CommandOpcode.UavBarrier: return "Barrier";
                case CommandOpcode.BeginMarker:
                case CommandOpcode.EndMarker: return "Marker";
                case CommandOpcode.BuildBottomLevelAccelStruct:
                case CommandOpcode.BuildTopLevelAccelStruct:
                case CommandOpcode.BuildTopLevelAccelStructFromBuffer: return "Acceleration Structure";
                default: return "State / Utility";
            }
        }

        private string ResourceName(ulong handle)
        {
            if (handle == 0) return "None";
            if (!_resourceLookup.TryGetValue(handle, out ResourceInfo resource)) return Hex(handle);
            return string.IsNullOrEmpty(resource.DebugName) ? $"<{resource.Kind}>" : resource.DebugName;
        }

        private static string ResourceSummary(ResourceInfo resource)
        {
            switch (resource.Kind)
            {
                case ResourceKind.Texture:
                case ResourceKind.StagingTexture:
                    return $"{resource.Width}×{resource.Height}×{resource.Depth}, {resource.Format}, mips {resource.MipLevels}";
                case ResourceKind.Buffer:
                    return $"{FormatBytes(resource.LogicalSize)}, stride {resource.Detail0}";
                case ResourceKind.Framebuffer:
                    return $"{resource.Width}×{resource.Height}, {resource.Detail0} color attachment(s)";
                case ResourceKind.BindingSet:
                case ResourceKind.BindingLayout:
                    return $"{resource.Detail0} bindings, {resource.Detail1} descriptors";
                default:
                    return resource.LogicalSize == 0 ? resource.Flags.ToString() : FormatBytes(resource.LogicalSize);
            }
        }

        private struct EffectiveState
        {
            internal ulong Pipeline, Framebuffer, IndexBuffer, IndirectParameters;
            internal uint BindingCount;
            internal int SourceEvent;
            internal ulong[] RelatedResources;
        }

        private static GUIStyle _treeLabelStyle;
        private static GUIStyle TreeLabelStyle => _treeLabelStyle ??
            (_treeLabelStyle = new GUIStyle(EditorStyles.label)
            { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(2, 2, 0, 0) });

        private static GUIStyle _treeGroupLabelStyle;
        private static GUIStyle TreeGroupLabelStyle => _treeGroupLabelStyle ??
            (_treeGroupLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(2, 2, 0, 0) });

        private static GUIStyle _rightMiniStyle;
        private static GUIStyle RightMiniStyle => _rightMiniStyle ??
            (_rightMiniStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight });

        private static GUIStyle _centeredLabelStyle;
        private static GUIStyle CenteredLabelStyle => _centeredLabelStyle ??
            (_centeredLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });

        private static GUIStyle _centeredMiniStyle;
        private static GUIStyle CenteredMiniStyle => _centeredMiniStyle ??
            (_centeredMiniStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });

        private static GUIStyle _previewTitleStyle;
        private static GUIStyle PreviewTitleStyle => _previewTitleStyle ??
            (_previewTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleCenter, fontSize = 15 });

        private static string Hex(ulong value) => $"0x{value:X}";
        private static float Float(ulong bits) => BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits));
        private void DrawSubresources(Func<int, ulong> value)
        {
            Detail("Mip range", $"{value(1)} + {value(2)}");
            Detail("Array range", $"{value(3)} + {value(4)}");
        }
        private void Section(string label) => _detailRows.Add(new DetailRow(label, "", true));
        private void Detail(string label, string value) => _detailRows.Add(new DetailRow(label, value ?? "", false));

        private readonly struct DetailRow
        {
            internal readonly string Label;
            internal readonly string Value;
            internal readonly bool Section;

            internal DetailRow(string label, string value, bool section)
            {
                Label = label ?? "";
                Value = value ?? "";
                Section = section;
            }
        }

        private readonly struct TreeRow
        {
            internal readonly int Depth;
            internal readonly string Label;
            internal readonly string Count;
            internal readonly string Size;
            internal readonly bool Group;
            internal readonly string Key;
            internal readonly bool Expanded;
            internal readonly int ListIndex;
            internal readonly int EventIndex;

            internal TreeRow(int depth, string label, string count, string size,
                bool group, string key, bool expanded, int listIndex, int eventIndex)
            {
                Depth = depth;
                Label = label ?? "";
                Count = count ?? "";
                Size = size ?? "";
                Group = group;
                Key = key ?? "";
                Expanded = expanded;
                ListIndex = listIndex;
                EventIndex = eventIndex;
            }
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
    }
}
