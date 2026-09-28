using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace Drift.EditorTools.Playtest
{
    // Per-system CPU time and GC from the Editor profiler while the game plays (eval-friendly: Begin, let frames
    // pass, Collect in chunks to stay inside eval's time budget, then Report).
    public static class PlaytestProfiler
    {
        class Acc { public double ms, gc; public int frames; }

        static readonly Dictionary<string, Acc> _acc = new();
        static int _next = -1, _frames;
        static double _loopMs;
        static readonly List<(float ms, int frame, string top, float topMs)> _spikes = new();
        static float _frameLoop, _frameTopMs;
        static string _frameTop;

        public static string Begin()
        {
            _acc.Clear();
            _frames = 0;
            _loopMs = 0;
            _spikes.Clear();
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            Profiler.enabled = true;
            _next = -1;
            return "profiling";
        }

        public static string Stop()
        {
            ProfilerDriver.enabled = false;
            Profiler.enabled = false;
            return "stopped";
        }

        // Reads up to maxFrames of the frames recorded since the last call.
        public static string Collect(int maxFrames = 40)
        {
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            if (last < 0) return "no frames";
            if (_next < first) _next = Math.Max(first, last - 600);
            int end = Math.Min(last, _next + maxFrames - 1);
            for (int f = _next; f <= end; f++) Read(f);
            _next = end + 1;
            return $"read up to {end}, {last - end} left, {_frames} frames total";
        }

        static void Read(int frame)
        {
            using var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0,
                HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
            if (view == null || !view.valid) return;
            _frames++;
            _frameLoop = 0f; _frameTopMs = 0f; _frameTop = "";
            var kids = new List<int>();
            Walk(view, view.GetRootItemID(), 0, kids, false);
            _spikes.Add((_frameLoop, frame, _frameTop, _frameTopMs));
        }

        static void Walk(HierarchyFrameDataView view, int id, int depth, List<int> scratch, bool inLoop)
        {
            var children = new List<int>();
            view.GetItemChildren(id, children);
            foreach (int c in children)
            {
                string name = view.GetItemName(c);
                float ms = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
                float gc = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnGcMemory);
                bool loop = inLoop || name == "PlayerLoop";
                if (name == "PlayerLoop") { _loopMs += ms; _frameLoop += ms; }
                bool script = loop && name.EndsWith("()") || name.Contains("[Invoke]");
                bool render = loop && (name.StartsWith("RenderPipelineManager") || name.StartsWith("Inl_") || name == "UIEvents.WillRenderCanvases"
                                       || name.StartsWith("Canvas.") || name.StartsWith("UGUI.") || name == "Camera.Render");
                if (script || render)
                {
                    if (!_acc.TryGetValue(name, out var a)) _acc[name] = a = new Acc();
                    a.ms += ms;
                    a.gc += gc;
                    a.frames++;
                    if (script && ms > _frameTopMs) { _frameTopMs = ms; _frameTop = name; }
                }
                if (depth < 7) Walk(view, c, depth + 1, scratch, loop);
            }
        }

        public static string Report(int top = 25, bool byGc = false)
        {
            var list = new List<KeyValuePair<string, Acc>>(_acc);
            if (byGc) list.Sort((a, b) => b.Value.gc.CompareTo(a.Value.gc));
            else list.Sort((a, b) => b.Value.ms.CompareTo(a.Value.ms));
            var sb = new StringBuilder();
            int n = Math.Max(1, _frames);
            sb.AppendLine($"{_frames} frames, PlayerLoop {_loopMs / n:0.00} ms/frame");
            for (int i = 0; i < Math.Min(top, list.Count); i++)
            {
                var kv = list[i];
                sb.AppendLine($"{kv.Value.ms / n,7:0.000} ms  {kv.Value.gc / n,8:0} B  {kv.Key}");
            }
            _spikes.Sort((a, b) => b.ms.CompareTo(a.ms));
            sb.AppendLine("worst frames (PlayerLoop ms, heaviest script):");
            for (int i = 0; i < Math.Min(8, _spikes.Count); i++)
                sb.AppendLine($"  {_spikes[i].ms,6:0.00} ms  {_spikes[i].topMs,6:0.00} ms {_spikes[i].top}");
            return sb.ToString();
        }
    }
}
