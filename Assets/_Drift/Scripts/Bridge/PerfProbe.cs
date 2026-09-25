using System.Collections;
using System.Collections.Generic;
using System.IO;
using Drift.SaveSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Bridge
{
    // Development builds only: finds out what the phone's GPU spends its time on. Started by a file "perfprobe" in
    // persistentDataPath (pushed over adb with run-as), once a run is playing. It holds each setting for a few
    // seconds - the baseline, a lower render scale, no depth texture, no UI, then every shader switched off in turn -
    // and logs "Drift-PROBE <step> fps .. gpu .. cpu .." (FrameTimingManager) for each. Deletes the file when done.
    public sealed class PerfProbe : MonoBehaviour
    {
        const string FlagName = "perfprobe";
        const float Settle = 1.5f, Hold = 6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Debug.isDebugBuild || !Application.isMobilePlatform) return;
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("PerfProbe") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<PerfProbe>();
        }

        static string FlagPath => Path.Combine(Application.persistentDataPath, FlagName);

        readonly FrameTiming[] _timing = new FrameTiming[1];

        IEnumerator Start()
        {
            GameSession session = null;
            float playing = 0f;
            while (playing < 5f)
            {
                if (session == null) session = FindAnyObjectByType<GameSession>();
                playing = session != null && session.Current == GameSession.State.Playing ? playing + Time.unscaledDeltaTime : 0f;
                yield return null;
            }
            Debug.Log("Drift-PROBE start mode " + Drift.Core.GameModes.Current + " " + Screen.width + "x" + Screen.height);

            yield return Measure("baseline", null, null);

            var rp = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            var scaleProp = rp != null ? rp.GetType().GetProperty("renderScale") : null;
            if (scaleProp != null)
            {
                object was = scaleProp.GetValue(rp);
                yield return Measure("renderScale0.6", () => scaleProp.SetValue(rp, 0.6f), () => scaleProp.SetValue(rp, was));
                yield return Measure("renderScale1.0", () => scaleProp.SetValue(rp, 1.0f), () => scaleProp.SetValue(rp, was));
            }
            var msaaProp = rp != null ? rp.GetType().GetProperty("msaaSampleCount") : null;
            if (msaaProp != null)
            {
                object was = msaaProp.GetValue(rp);
                yield return Measure("noMSAA", () => msaaProp.SetValue(rp, 1), () => msaaProp.SetValue(rp, was));
            }
            // Depth from a prepass instead of a copy of the attachment (Adreno showed square tiles in the depth-based
            // shore foam); the renderer data lives in the asset's private list, the pipeline is rebuilt by reassigning.
            var listField = rp != null ? rp.GetType().GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
            var renderers = listField != null ? listField.GetValue(rp) as System.Array : null;
            object rd = renderers != null && renderers.Length > 0 ? renderers.GetValue(0) : null;
            var depthModeProp = rd != null ? rd.GetType().GetProperty("copyDepthMode") : null;
            if (depthModeProp != null)
            {
                object was = depthModeProp.GetValue(rd);
                object prepass = System.Enum.ToObject(depthModeProp.PropertyType, 2);
                yield return Measure("depthPrepass",
                    () => { depthModeProp.SetValue(rd, prepass); QualitySettings.renderPipeline = null; QualitySettings.renderPipeline = rp; },
                    () => { depthModeProp.SetValue(rd, was); QualitySettings.renderPipeline = null; QualitySettings.renderPipeline = rp; });
            }
            else Debug.Log("Drift-PROBE depthPrepass unavailable rd=" + (rd != null ? rd.GetType().Name : "null"));
            var depthProp = rp != null ? rp.GetType().GetProperty("supportsCameraDepthTexture") : null;
            if (depthProp != null)
            {
                object was = depthProp.GetValue(rp);
                yield return Measure("noDepthTexture", () => depthProp.SetValue(rp, false), () => depthProp.SetValue(rp, was));
            }

            var canvases = new List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c.enabled && c.isRootCanvas) canvases.Add(c);
            yield return Measure("noUI", () => { foreach (var c in canvases) if (c != null) c.enabled = false; },
                () => { foreach (var c in canvases) if (c != null) c.enabled = true; });

            // Every shader in view, by how many renderers use it: off one at a time.
            var byShader = new Dictionary<string, List<Renderer>>();
            // FindObjectsByType misses the hidden, generated renderers (plants, animals, clouds, fish): walk them all.
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || !r.gameObject.scene.IsValid()) continue;
                if (r.sharedMaterial == null || r.sharedMaterial.shader == null) continue;
                string key = r.sharedMaterial.shader.name;
                if (!byShader.TryGetValue(key, out var list)) byShader[key] = list = new List<Renderer>();
                list.Add(r);
            }
            var ordered = new List<KeyValuePair<string, List<Renderer>>>(byShader);
            ordered.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            Debug.Log("Drift-PROBE shaders " + ordered.Count);
            foreach (var kv in ordered)
            {
                var list = kv.Value;
                yield return Measure("no:" + kv.Key + "(" + list.Count + ")",
                    () => { foreach (var r in list) if (r != null) r.enabled = false; },
                    () => { foreach (var r in list) if (r != null) r.enabled = true; });
            }

            yield return Measure("baseline-end", null, null);
            Debug.Log("Drift-PROBE done");
            try { File.Delete(FlagPath); } catch (IOException) { }
            Destroy(gameObject);
        }

        IEnumerator Measure(string step, System.Action apply, System.Action undo)
        {
            apply?.Invoke();
            // A logcat watcher takes a screenshot inside each step (visual checks such as the shore foam).
            Debug.Log("Drift-PROBE begin " + step);
            float t = 0f;
            while (t < Settle) { t += Time.unscaledDeltaTime; yield return null; }
            int frames = 0, gpuN = 0;
            double gpu = 0, cpu = 0;
            t = 0f;
            while (t < Hold)
            {
                t += Time.unscaledDeltaTime;
                frames++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    if (_timing[0].gpuFrameTime > 0) { gpu += _timing[0].gpuFrameTime; gpuN++; }
                    cpu += _timing[0].cpuMainThreadFrameTime;
                }
                yield return null;
            }
            undo?.Invoke();
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Drift-PROBE {0} fps {1:F1} gpu {2:F2}ms cpu {3:F2}ms", step, frames / t, gpuN > 0 ? gpu / gpuN : -1, frames > 0 ? cpu / frames : -1));
        }
    }
}
