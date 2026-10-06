using System;
using System.Collections.Generic;
using System.Text;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CasinoMares.Core
{
    /// <summary>
    /// <c>?debug=perf</c>: finds out what makes a view slow on a device we can't profile directly (a phone browser).
    /// A few seconds after the player sits down, it switches one thing off at a time (the UI, post-processing,
    /// shadows, the extra lights, light cones and particles, the slot machines, half resolution, HDR), measures the
    /// frame rate and CPU time for each, puts it back, and shows the results in <see cref="DebugOverlay"/> and in a box
    /// the web page draws over the game (the game's own HUD covers the overlay's corner on a phone). Whatever brings
    /// the frame rate back up is what costs the time. Meanwhile <see cref="AutoRenderScale"/> keeps the render scale it
    /// had settled on, so the steps are measured alike.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        const float SettleSeconds = 1f;
        const float MeasureSeconds = 2f;
        const float StartAfterSitting = 3f;

        /// <summary>Lines for the overlay: the step being measured, then each result so far.</summary>
        public static string Summary { get; private set; } = "perf: sit down to start";

        static readonly (string name, Func<Action> off)[] Steps =
        {
            ("base", () => () => { }),
            ("no UI", HideUi),
            ("no post", NoPostProcessing),
            ("no shadows", NoShadows),
            ("no lights", NoExtraLights),
            ("no FX", NoEffects),
            ("no slots", NoSlotMachines),
            ("half res", HalfResolution),
            ("no HDR", NoHdr),
        };

        readonly StringBuilder _results = new();
        float _seatedSince = -1f;
        bool _done;
        int _step = -1;
        float _stepStartedAt;
        Action _restore;
        int _framesAtStart;
        double _cpuAtStart;
        float _timeAtStart;
        bool _measuring;

        void Update()
        {
            var seated = SeatingController.LocalTable != null;
            if (!seated)
            {
                if (_step >= 0)
                    Abort();
                _seatedSince = -1f;
                _done = false;
                return;
            }

            if (_seatedSince < 0f)
            {
                _seatedSince = Time.unscaledTime;
                Summary = "perf: starting in a moment, keep still";
                if (!_done)
                    Debug.Log("[PerfProbe] seated: starting in a moment");
            }

            if (_done || Time.unscaledTime - _seatedSince < StartAfterSitting)
                return;

            if (_step < 0)
            {
                _results.Clear();
                AutoRenderScale.Suspended = true;
                Debug.Log("[PerfProbe] started: keep still for about 30 s");
                Begin(0);
                return;
            }

            var elapsed = Time.unscaledTime - _stepStartedAt;
            if (!_measuring && elapsed >= SettleSeconds)
            {
                _measuring = true;
                _framesAtStart = FrameTimer.FrameCount;
                _cpuAtStart = FrameTimer.CpuSeconds;
                _timeAtStart = Time.unscaledTime;
            }

            if (!_measuring || Time.unscaledTime - _timeAtStart < MeasureSeconds)
                return;

            var frames = Math.Max(1, FrameTimer.FrameCount - _framesAtStart);
            var fps = frames / (Time.unscaledTime - _timeAtStart);
            var cpu = (FrameTimer.CpuSeconds - _cpuAtStart) * 1000.0 / frames;
            var line = $"{Steps[_step].name}: {fps:0} fps, cpu {cpu:0.0} ms";
            _results.AppendLine(line);
            Debug.Log("[PerfProbe] " + line);

            _restore?.Invoke();
            _restore = null;
            if (_step + 1 < Steps.Length)
            {
                Begin(_step + 1);
                return;
            }

            _step = -1;
            _done = true;
            AutoRenderScale.Suspended = false;
            Debug.Log("[PerfProbe] done (stand up and sit down to repeat)");
            Summary = "perf (stand up and sit to repeat):\n" + _results.ToString().TrimEnd();
        }

        void Begin(int step)
        {
            _step = step;
            _stepStartedAt = Time.unscaledTime;
            _measuring = false;
            _restore = Steps[step].off();
            Summary = $"perf: measuring \"{Steps[step].name}\" ({step + 1}/{Steps.Length})\n" + _results.ToString().TrimEnd();
        }

        void Abort()
        {
            _restore?.Invoke();
            _restore = null;
            _step = -1;
            AutoRenderScale.Suspended = false;
            Summary = "perf: stopped (stood up)";
            Debug.Log("[PerfProbe] stopped: stood up");
        }

        void OnDestroy()
        {
            _restore?.Invoke();
            AutoRenderScale.Suspended = false;
        }

        // ---------------------------------------------------------------- What each step switches off; each returns how to put it back

        static Action HideUi()
        {
            var roots = new List<VisualElement>();
            foreach (var document in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                var root = document.rootVisualElement;
                if (root == null || !root.visible)
                    continue;
                root.visible = false;
                roots.Add(root);
            }

            return () =>
            {
                foreach (var root in roots)
                    root.visible = true;
            };
        }

        static Action NoPostProcessing()
        {
            var cameras = new List<UniversalAdditionalCameraData>();
            foreach (var camera in Camera.allCameras)
            {
                var data = camera.GetUniversalAdditionalCameraData();
                if (data == null || !data.renderPostProcessing)
                    continue;
                data.renderPostProcessing = false;
                cameras.Add(data);
            }

            return () =>
            {
                foreach (var data in cameras)
                    data.renderPostProcessing = true;
            };
        }

        static Action NoShadows()
        {
            var lights = new List<(Light light, LightShadows shadows)>();
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.shadows == LightShadows.None)
                    continue;
                lights.Add((light, light.shadows));
                light.shadows = LightShadows.None;
            }

            return () =>
            {
                foreach (var (light, shadows) in lights)
                    light.shadows = shadows;
            };
        }

        static Action NoExtraLights()
        {
            var lights = new List<Light>();
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type == LightType.Directional || !light.enabled)
                    continue;
                light.enabled = false;
                lights.Add(light);
            }

            return () =>
            {
                foreach (var light in lights)
                    light.enabled = true;
            };
        }

        static Action NoEffects()
        {
            var renderers = new List<Renderer>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                var effect = renderer is ParticleSystemRenderer ||
                             (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "CasinoMares/LightCone");
                if (!effect || !renderer.enabled)
                    continue;
                renderer.enabled = false;
                renderers.Add(renderer);
            }

            return () =>
            {
                foreach (var renderer in renderers)
                    renderer.enabled = true;
            };
        }

        static Action NoSlotMachines()
        {
            var renderers = new List<Renderer>();
            foreach (var machine in Slots.SlotMachine.All)
            {
                if (machine == null)
                    continue;
                foreach (var renderer in machine.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled)
                        continue;
                    renderer.enabled = false;
                    renderers.Add(renderer);
                }
            }

            return () =>
            {
                foreach (var renderer in renderers)
                    renderer.enabled = true;
            };
        }

        static Action HalfResolution()
        {
            var pipeline = UniversalRenderPipeline.asset;
            if (pipeline == null)
                return () => { };
            var scale = pipeline.renderScale;
            pipeline.renderScale = scale * 0.5f;
            return () => pipeline.renderScale = scale;
        }

        static Action NoHdr()
        {
            var pipeline = UniversalRenderPipeline.asset;
            if (pipeline == null || !pipeline.supportsHDR)
                return () => { };
            pipeline.supportsHDR = false;
            return () => pipeline.supportsHDR = true;
        }
    }
}
