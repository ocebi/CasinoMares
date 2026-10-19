using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CasinoMares.Core
{
    /// <summary>
    /// Renders the 3D view at a lower resolution on phones while the frame rate is low, and goes back up once it
    /// recovers. A phone that has run hot for a while slows its GPU right down (an iPhone seated at a Yarn Tumble went
    /// from 56 to 15 fps) and each frame is then held up by pixel work, so fewer pixels bring the frame rate back and
    /// slow the heating. The HUD isn't scaled and stays sharp. When the lowest step still leaves it under 45 fps for
    /// 10 s, the frame rate is held at 30 instead (<see cref="FrameRateCap"/>): steady beats juddering, and the phone
    /// cools. The cap comes off again for a look after 30 s, then 60, up to 4 minutes; if the frame rate drops within
    /// 15 s of that, it goes straight back on. On by default on phones and tablets in a browser; <c>?autoscale=0</c>
    /// turns it off, <c>?autoscale=1</c> turns it on in any browser. Only in a browser: in the editor the change
    /// would be saved into the pipeline asset.
    /// </summary>
    public class AutoRenderScale : MonoBehaviour
    {
        /// <summary>Each step down, against the pipeline's own render scale.</summary>
        static readonly float[] Scales = { 1f, 0.85f, 0.7f, 0.6f };

        const float LowFps = 45f;
        const float GoodFps = 57f;
        const float WindowSeconds = 2f;
        const float SettleSeconds = 1f;
        const float StartAfterSeconds = 5f;
        /// <summary>A frame this long is a one-off (loading, a shader compiling, the tab coming back), not a slow GPU.</summary>
        const float HitchSeconds = 0.25f;
        /// <summary>A step down that didn't raise the frame rate by this much wasn't what held it back.</summary>
        const float HelpedRatio = 1.1f;
        /// <summary>When the CPU is busy for this much of each frame, the GPU isn't what's slow and fewer pixels won't help.</summary>
        const float CpuBoundShare = 0.75f;
        /// <summary>A step down this soon after a step up means the step up was too early.</summary>
        const float TooSoonSeconds = 15f;
        const float FirstUpWait = 8f;
        const float FirstDownRetry = 30f;
        const float MaxWait = 240f;
        /// <summary>Under 45 fps for this long with no step down left before the frame rate is held at 30.</summary>
        const float SlowBeforeCapSeconds = 10f;
        /// <summary>Held at 30 for this long (and keeping it) before the cap is tried off; doubles each time it goes back on.</summary>
        const float FirstCapRetry = 30f;
        /// <summary>At the 30 cap, a frame rate this high means the phone keeps up with it.</summary>
        const float CapHoldsFps = 28f;

        /// <summary>Set while something else measures the frame rate (<see cref="PerfProbe"/>): keep the scale as it is.</summary>
        public static bool Suspended { get; set; }

        /// <summary>The step the scale is on now, 0 (full) to <see cref="MaxLevel"/>, for the debug overlay.</summary>
        public static int Level { get; private set; }

        public static int MaxLevel => Scales.Length - 1;

        float _baseScale;
        int _level;
        float _windowTime;
        int _windowFrames;
        double _cpuAtWindow;
        float _waitUntil;
        /// <summary>The frame rate before the last step down, while finding out whether it helped; otherwise 0.</summary>
        float _fpsBeforeStepDown;
        float _goodFor;
        float _steppedUpAt = float.NegativeInfinity;
        float _upWait = FirstUpWait;
        float _downRetry = FirstDownRetry;
        float _noDownUntil;
        /// <summary>Seconds under 45 fps with nothing left to step down to.</summary>
        float _slowFor;
        /// <summary>Seconds the phone has kept up with the 30 cap.</summary>
        float _capHeldFor;
        float _capRetry = FirstCapRetry;
        float _uncappedAt = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            Level = 0;
            var setting = Platform.QueryParameter("autoscale");
            var on = setting != null ? setting != "0" : Platform.IsTouch;
            if (!Platform.IsWeb || !on || UniversalRenderPipeline.asset == null)
                return;

            var scaler = new GameObject("AutoRenderScale");
            DontDestroyOnLoad(scaler);
            scaler.AddComponent<AutoRenderScale>();
            FrameTimer.Install();
        }

        void Start()
        {
            _baseScale = UniversalRenderPipeline.asset.renderScale;
            _waitUntil = Time.unscaledTime + StartAfterSeconds;
        }

        void Update()
        {
            // Held at 30 by the player's choice, the frame rate says nothing about the GPU: a steady 30 isn't slow.
            if (Suspended || FrameRateCap.UserCapped || Platform.PageHidden || Time.unscaledDeltaTime > HitchSeconds || Time.unscaledTime < _waitUntil)
            {
                RestartWindow();
                return;
            }

            _windowTime += Time.unscaledDeltaTime;
            _windowFrames++;
            if (_windowTime < WindowSeconds)
                return;

            var fps = _windowFrames / _windowTime;
            var cpuShare = (float)(FrameTimer.CpuSeconds - _cpuAtWindow) / _windowTime;
            var seconds = _windowTime;
            RestartWindow();
            Judge(fps, cpuShare, seconds);
        }

        void RestartWindow()
        {
            _windowTime = 0f;
            _windowFrames = 0;
            _cpuAtWindow = FrameTimer.CpuSeconds;
        }

        void Judge(float fps, float cpuShare, float seconds)
        {
            var now = Time.unscaledTime;
            if (FrameRateCap.AutoCapped)
            {
                JudgeCapped(fps, seconds, now);
                return;
            }

            if (_fpsBeforeStepDown > 0f)
            {
                var helped = fps >= _fpsBeforeStepDown * HelpedRatio;
                _fpsBeforeStepDown = 0f;
                if (!helped)
                {
                    // Something else holds it back (the browser capping the frame rate in low power mode, say): put
                    // the sharper picture back and try again later, less often each time.
                    SetLevel(_level - 1, fps, "didn't help");
                    _noDownUntil = now + _downRetry;
                    _downRetry = Mathf.Min(_downRetry * 2f, MaxWait);
                    return;
                }
            }

            if (fps < LowFps)
            {
                _goodFor = 0f;
                if (_level + 1 >= Scales.Length || now < _noDownUntil || cpuShare >= CpuBoundShare)
                {
                    // Nothing to step down to (or it wouldn't help): the frame rate cap is what's left.
                    ConsiderCap(fps, seconds, now);
                    return;
                }

                _slowFor = 0f;
                if (now - _steppedUpAt < TooSoonSeconds)
                    _upWait = Mathf.Min(_upWait * 2f, MaxWait);
                _fpsBeforeStepDown = fps;
                SetLevel(_level + 1, fps, "slow");
                return;
            }

            _slowFor = 0f;
            if (fps < GoodFps || _level == 0)
            {
                _goodFor = 0f;
                return;
            }

            _goodFor += seconds;
            if (_goodFor < _upWait)
                return;
            _goodFor = 0f;
            _steppedUpAt = now;
            SetLevel(_level - 1, fps, "recovered");
        }

        /// <summary>
        /// Held at 30: the frame rate can only say whether the phone keeps up with the cap. Once it has for a while,
        /// the cap comes off for a look; <see cref="ConsiderCap"/> puts it straight back if that was too soon.
        /// </summary>
        void JudgeCapped(float fps, float seconds, float now)
        {
            _capHeldFor = fps >= CapHoldsFps ? _capHeldFor + seconds : 0f;
            if (_capHeldFor < _capRetry)
                return;
            _capHeldFor = 0f;
            _uncappedAt = now;
            FrameRateCap.AutoCapped = false;
            _waitUntil = now + SettleSeconds;
            Debug.Log($"[RenderScale] cap off at {fps:0} fps (retry)");
        }

        void ConsiderCap(float fps, float seconds, float now)
        {
            if (!FrameRateCap.AutoAllowed)
                return;
            // Slow again right after the cap came off: the answer is known, back on and wait longer next time.
            var tooSoon = now - _uncappedAt < TooSoonSeconds;
            _slowFor += seconds;
            if (!tooSoon && _slowFor < SlowBeforeCapSeconds)
                return;
            if (tooSoon)
                _capRetry = Mathf.Min(_capRetry * 2f, MaxWait);
            _slowFor = 0f;
            _capHeldFor = 0f;
            FrameRateCap.AutoCapped = true;
            _waitUntil = now + SettleSeconds;
            Debug.Log($"[RenderScale] 30 fps at {fps:0} fps ({(tooSoon ? "still slow" : "slow")}, next try in {_capRetry:0} s)");
        }

        void SetLevel(int level, float fps, string reason)
        {
            _level = level;
            Level = level;
            UniversalRenderPipeline.asset.renderScale = _baseScale * Scales[level];
            _waitUntil = Time.unscaledTime + SettleSeconds;
            Debug.Log($"[RenderScale] x{Scales[level]:0.##} at {fps:0} fps ({reason})");
        }
    }
}
