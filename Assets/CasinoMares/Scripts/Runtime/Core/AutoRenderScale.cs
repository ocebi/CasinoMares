using System.Globalization;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CasinoMares.Core
{
    /// <summary>
    /// Renders the 3D view at a lower resolution on phones while the frame rate is low, and goes back up once it
    /// recovers. A phone that has run hot for a while slows its GPU right down (an iPhone seated at a Yarn Tumble went
    /// from 56 to 15 fps) and each frame is then held up by pixel work, so fewer pixels bring the frame rate back and
    /// slow the heating. The HUD isn't scaled and stays sharp. The scale never goes below 0.7: lower than that looked
    /// pixelated. It steps down only after 5 s under 45 fps, and back up after 20 s at 50 or more (Safari holds a
    /// phone at 60, and a busy scene there measures 55–59, so a higher bar never let it come back up).
    /// It doesn't judge until 20 s after the cat spawns, nor while bundles load (<see cref="LazyContent"/>) or for
    /// 10 s after each: loading and warming up the scene stutter on every phone, and that isn't a slow GPU.
    /// When the lowest step still leaves it under 45 fps for 10 s, the frame rate is held at 30 instead
    /// (<see cref="FrameRateCap"/>): steady beats juddering, and the phone cools. The cap comes off again for a look
    /// after 30 s, then 60, up to 4 minutes; if the frame rate drops within 15 s of that, it goes straight back on.
    /// On by default on phones and tablets in a browser; <c>?autoscale=0</c> turns it off, <c>?autoscale=1</c> turns
    /// it on in any browser, and <c>?scale=1</c> (or any value from 0.7 to 1) pins the scale. Only in a browser: in
    /// the editor the change would be saved into the pipeline asset.
    /// </summary>
    public class AutoRenderScale : MonoBehaviour
    {
        /// <summary>Each step down, against the pipeline's own render scale.</summary>
        static readonly float[] Scales = { 1f, 0.85f, 0.7f };
        /// <summary>The render scale is never set lower than this, whatever the pipeline's own scale.</summary>
        public const float MinScale = 0.7f;

        const float LowFps = 45f;
        const float GoodFps = 50f;
        const float WindowSeconds = 1f;
        const float SettleSeconds = 1f;
        /// <summary>Under <see cref="LowFps"/> for this long without a break before a step down.</summary>
        const float LowBeforeStepDown = 5f;
        /// <summary>How long after a step down the frame rate is measured to tell whether it helped.</summary>
        const float HelpCheckSeconds = 3f;
        /// <summary>No judging this long after the local cat spawns: the scene is still warming up.</summary>
        const float AfterSpawnSeconds = 20f;
        /// <summary>No judging while a bundle loads (decompressing, uploading textures) or this long after.</summary>
        const float AfterLoadSeconds = 10f;
        /// <summary>A frame this long is a one-off (loading, a shader compiling, the tab coming back), not a slow GPU.</summary>
        const float HitchSeconds = 0.25f;
        /// <summary>A step down that didn't raise the frame rate by this much wasn't what held it back.</summary>
        const float HelpedRatio = 1.1f;
        /// <summary>When the CPU is busy for this much of each frame, the GPU isn't what's slow and fewer pixels won't help.</summary>
        const float CpuBoundShare = 0.75f;
        /// <summary>A step down this soon after a step up means the step up was too early.</summary>
        const float TooSoonSeconds = 15f;
        /// <summary>At 50 fps or more for this long before a step back up; doubles when a step up turns out too early.</summary>
        const float FirstUpWait = 20f;
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

        /// <summary>For the debug overlay: why the scale is what it is, and why it isn't being judged right now.</summary>
        public static string Status => _pinned ? "pinned by ?scale" : _paused != null ? $"{_reason}, waiting: {_paused}" : _reason;

        static bool _pinned;
        static string _reason = "off";
        static string _paused;

        float _baseScale;
        int _level;
        float _windowTime;
        int _windowFrames;
        double _cpuAtWindow;
        float _waitUntil;
        PlayerCat _spawned;
        float _spawnedAt;
        /// <summary>The frame rate before the last step down, while finding out whether it helped; otherwise 0.</summary>
        float _fpsBeforeStepDown;
        float _helpTime;
        float _helpFrames;
        float _lowFor;
        float _lowFrames;
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
            _pinned = false;
            _reason = "off";
            _paused = null;
            if (!Platform.IsWeb || UniversalRenderPipeline.asset == null)
                return;

            var pin = Platform.QueryParameter("scale");
            if (pin != null && float.TryParse(pin, NumberStyles.Float, CultureInfo.InvariantCulture, out var pinned))
            {
                _pinned = true;
                UniversalRenderPipeline.asset.renderScale = Mathf.Clamp(pinned, MinScale, 1f);
                Debug.Log($"[RenderScale] pinned at x{UniversalRenderPipeline.asset.renderScale:0.##} (?scale)");
                return;
            }

            var setting = Platform.QueryParameter("autoscale");
            var on = setting != null ? setting != "0" : Platform.IsTouch;
            if (!on)
                return;

            var scaler = new GameObject("AutoRenderScale");
            DontDestroyOnLoad(scaler);
            scaler.AddComponent<AutoRenderScale>();
            FrameTimer.Install();
        }

        void Start()
        {
            _baseScale = UniversalRenderPipeline.asset.renderScale;
            _reason = "full";
            Apply();
        }

        void Update()
        {
            var now = Time.unscaledTime;
            var local = PlayerCat.Local;
            if (local != _spawned)
            {
                _spawned = local;
                _spawnedAt = now;
            }

            // Held at 30 by the player's choice, the frame rate says nothing about the GPU: a steady 30 isn't slow.
            _paused = Suspended ? "perf probe"
                : FrameRateCap.UserCapped ? "30 fps chosen"
                : Platform.PageHidden ? "page hidden"
                : local == null ? "no cat yet"
                : now < _spawnedAt + AfterSpawnSeconds ? "warming up"
                : LazyContent.InFlight > 0 || now < LazyContent.LastFinishedAt + AfterLoadSeconds ? "loading"
                : null;
            if (_paused != null)
            {
                // Whatever was measured before the pause doesn't run on into what comes after it.
                ForgetRuns();
                RestartWindow();
                return;
            }

            if (Time.unscaledDeltaTime > HitchSeconds || now < _waitUntil)
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

        void ForgetRuns()
        {
            _lowFor = _lowFrames = 0f;
            _goodFor = 0f;
            _slowFor = 0f;
            _capHeldFor = 0f;
            _helpTime = _helpFrames = 0f;
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
                _helpTime += seconds;
                _helpFrames += fps * seconds;
                if (_helpTime < HelpCheckSeconds)
                    return;
                var after = _helpFrames / _helpTime;
                var helped = after >= _fpsBeforeStepDown * HelpedRatio;
                _fpsBeforeStepDown = 0f;
                _helpTime = _helpFrames = 0f;
                if (!helped)
                {
                    // Something else holds it back (the browser capping the frame rate in low power mode, say): put
                    // the sharper picture back and try again later, less often each time.
                    SetLevel(_level - 1, after, "didn't help");
                    _noDownUntil = now + _downRetry;
                    _downRetry = Mathf.Min(_downRetry * 2f, MaxWait);
                    return;
                }
            }

            if (fps < LowFps)
            {
                _goodFor = 0f;
                _lowFor += seconds;
                _lowFrames += fps * seconds;
                if (_lowFor < LowBeforeStepDown)
                    return;
                var low = _lowFrames / _lowFor;
                var lowFor = _lowFor;
                _lowFor = _lowFrames = 0f;
                if (_level + 1 >= Scales.Length || now < _noDownUntil || cpuShare >= CpuBoundShare)
                {
                    // Nothing to step down to (or it wouldn't help): the frame rate cap is what's left.
                    ConsiderCap(low, lowFor, now);
                    return;
                }

                _slowFor = 0f;
                if (now - _steppedUpAt < TooSoonSeconds)
                    _upWait = Mathf.Min(_upWait * 2f, MaxWait);
                _fpsBeforeStepDown = low;
                SetLevel(_level + 1, low, "slow");
                return;
            }

            _lowFor = _lowFrames = 0f;
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
            _reason = level == 0 ? "full" : $"{reason} at {fps:0} fps";
            Apply();
            _waitUntil = Time.unscaledTime + SettleSeconds;
            Debug.Log($"[RenderScale] x{UniversalRenderPipeline.asset.renderScale:0.##} at {fps:0} fps ({reason})");
        }

        void Apply()
        {
            Level = _level;
            UniversalRenderPipeline.asset.renderScale = Mathf.Max(MinScale, _baseScale * Scales[_level]);
        }
    }
}
