using System.Collections.Generic;
using CasinoMares.Networking;
using CasinoMares.Player;
using Fusion;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace CasinoMares.Core
{
    /// <summary>
    /// <c>?debug=1</c> in the page URL shows render stats in the top-left corner: frame rate, how much of each frame
    /// the CPU was busy, quality level, texture format, render size, HDR and the GPU. It's for checking how the game
    /// looks and runs on a phone: when the frame rate is low but the CPU time is well under the frame time, the GPU is
    /// what's slow. Once connected, another line shows the network: round trip to the server, how far behind other
    /// cats are drawn (Fusion's snapshot buffer), and how often another cat froze in the last 10 s because its
    /// snapshots came late. <c>?debug=perf</c> adds <see cref="PerfProbe"/>, which measures what each part of a view
    /// costs once the player sits down.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        const float SampleSeconds = 0.5f;
        const float StallWindow = 10f;

        GUIStyle _style;
        string _text = "";
        int _frames;
        float _elapsed;
        double _cpuAtSample;
        int _framesAtSample;
        readonly Dictionary<PlayerCat, Vector3> _remotePositions = new();
        readonly Queue<float> _stallTimes = new();
        bool _stalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var mode = Platform.QueryParameter("debug");
            if (mode != "1" && mode != "perf")
                return;

            var overlay = new GameObject("DebugOverlay");
            DontDestroyOnLoad(overlay);
            overlay.AddComponent<DebugOverlay>();
            if (mode == "perf")
                overlay.AddComponent<PerfProbe>();
            FrameTimer.Install();
        }

        void Update()
        {
            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < SampleSeconds)
                return;

            var fps = _frames / _elapsed;
            var cpuMs = (FrameTimer.CpuSeconds - _cpuAtSample) * 1000.0 / Mathf.Max(1, FrameTimer.FrameCount - _framesAtSample);
            _frames = 0;
            _elapsed = 0f;
            _cpuAtSample = FrameTimer.CpuSeconds;
            _framesAtSample = FrameTimer.FrameCount;

            var pipeline = UniversalRenderPipeline.asset;
            var scale = pipeline != null ? pipeline.renderScale : 1f;
            // URP quietly renders without HDR where the GPU can't render to a float format.
            var hdr = pipeline != null && pipeline.supportsHDR &&
                      SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Render);
            _text = $"{fps:0} fps  cpu {cpuMs:0.0} ms  {QualitySettings.names[QualitySettings.GetQualityLevel()]}  " +
                    $"{Platform.TextureFormat}  {Screen.width}x{Screen.height} x{scale:0.##}  HDR {(hdr ? "on" : "off")}\n" +
                    SystemInfo.graphicsDeviceName;

            // Fusion's room settings (who the master client is) only arrive with the first state update, after joining.
            var connection = ConnectionManager.Instance;
            var runner = connection != null ? connection.Runner : null;
            if (connection != null && connection.State == ConnectionState.Connected && runner != null && runner.IsRunning)
            {
                var rtt = runner.GetPlayerRtt(runner.LocalPlayer) * 1000.0;
                var buffer = (runner.LocalRenderTime - runner.RemoteRenderTime) * 1000f;
                _text += $"\nRTT {rtt:0} ms  buffer {buffer:0} ms  stalls {_stallTimes.Count} in {StallWindow:0}s" +
                         $"{(runner.IsSharedModeMasterClient ? "  master" : "")}";
            }

            if (TryGetComponent<PerfProbe>(out _))
                _text += "\n" + PerfProbe.Summary;
        }

        void LateUpdate()
        {
            // A remote cat whose networked velocity says it's walking but that didn't move this frame has run out of
            // snapshots to interpolate between: that's the freeze-then-snap the buffer is meant to hide.
            var stalled = false;
            foreach (var cat in PlayerCat.All)
            {
                if (cat == null || cat.IsLocal || cat.Object == null || !cat.Object.IsValid)
                    continue;
                var position = cat.transform.position;
                var moved = !_remotePositions.TryGetValue(cat, out var last) || position != last;
                _remotePositions[cat] = position;
                var character = cat.GetComponent<NetworkCharacterController>();
                if (!moved && character != null && character.Velocity.sqrMagnitude > 0.25f)
                    stalled = true;
            }

            if (stalled && !_stalled)
                _stallTimes.Enqueue(Time.unscaledTime);
            _stalled = stalled;
            while (_stallTimes.Count > 0 && Time.unscaledTime - _stallTimes.Peek() > StallWindow)
                _stallTimes.Dequeue();
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
            // A phone screen is small and its pixels dense: keep the box to a corner there.
            _style.fontSize = Platform.IsTouch ? Mathf.Max(11, Screen.height / 60) : Mathf.Max(14, Screen.height / 45);
            var size = _style.CalcSize(new GUIContent(_text));
            GUI.Box(new Rect(8f, 8f, size.x, size.y), _text, _style);
        }
    }
}
