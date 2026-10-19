using System.Collections.Generic;
using CasinoMares.Player;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CasinoMares.CameraRig
{
    /// <summary>
    /// What the local player sees after three glasses of wine (UX spec §1.1): a fisheye lens with colour fringing,
    /// a wine-dark vignette and more colour, plus a slow Cinemachine sway on whichever camera is live. One runtime
    /// volume profile holds the "very drunk" values; the drink count only sets the volume weight and the noise
    /// amplitude, ramped through a single smoothstepped float. Costs nothing while sober: the volume is disabled at
    /// weight 0 and the noise components are off. Creates itself at startup; "Tipsy visuals" in the pause menu turns
    /// the whole thing off (the HUD indicator and the bonus stay).
    /// </summary>
    public class TipsyEffect : MonoBehaviour
    {
        const string VisualsKey = "TipsyVisuals";

        /// <summary>Volume weight and noise gain per tier: tipsy (3 glasses), drunk (4), very drunk (5+).</summary>
        const float TipsyGain = 0.5f;
        const float DrunkGain = 0.75f;
        const float VeryDrunkGain = 1f;
        const int VeryDrunkAt = 5;

        /// <summary>Ramp seconds: into a tier, out when sobering, out when the toggle is switched off.</summary>
        const float RampIn = 3f;
        const float RampOutSober = 6f;
        const float RampOutToggle = 1f;

        /// <summary>Post-processing at weight 1 (5+ glasses).</summary>
        const float LensIntensity = -0.22f;
        const float LensScale = 1.06f;
        const float Aberration = 0.45f;
        const float Saturation = 22f;
        const float VignetteIntensity = 0.5f;
        const float VignetteSmoothness = 0.6f;
        static readonly Color VignetteColor = new(30f / 255f, 8f / 255f, 16f / 255f);
        const float BloomIntensity = 1.2f;
        const float BloomScatter = 0.8f;
        /// <summary>Above the scene's global volume (priority 0).</summary>
        const float VolumePriority = 10f;

        /// <summary>Orientation sway at gain 1: degrees and Hz per axis; periods of ~6, ~8 and ~11 s so they never line up.</summary>
        const float PitchAmplitude = 0.5f;
        const float PitchFrequency = 0.17f;
        const float YawAmplitude = 0.8f;
        const float YawFrequency = 0.12f;
        const float RollAmplitude = 1.5f;
        const float RollFrequency = 0.09f;

        public static TipsyEffect Instance { get; private set; }

        /// <summary>The pause-menu toggle: off leaves only the HUD, the two-total labels and the bonus.</summary>
        public static bool VisualsEnabled
        {
            get => _visualsEnabled ??= PlayerPrefs.GetInt(VisualsKey, 1) == 1;
            set
            {
                _visualsEnabled = value;
                PlayerPrefs.SetInt(VisualsKey, value ? 1 : 0);
            }
        }

        /// <summary>Read from PlayerPrefs once; the pause toggle writes through the setter.</summary>
        static bool? _visualsEnabled;

        /// <summary>The current blend, 0 (sober or off) to 1 (very drunk), for anything else that wants to follow it.</summary>
        public float Weight { get; private set; }

        Volume _volume;
        NoiseSettings _noise;
        readonly List<Sway> _sways = new();
        float _goal;
        float _rampFrom;
        float _rampStart;
        float _rampSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Instance != null)
                return;
            var effect = new GameObject("TipsyEffect").AddComponent<TipsyEffect>();
            DontDestroyOnLoad(effect.gameObject);
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            var goal = VisualsEnabled ? TierGain(LocalDrinkCount()) : 0f;
            if (goal != _goal)
            {
                _rampFrom = Weight;
                _rampStart = Time.time;
                _rampSeconds = goal > Weight ? RampIn : VisualsEnabled ? RampOutSober : RampOutToggle;
                _goal = goal;
            }

            var u = _rampSeconds > 0f ? Mathf.Clamp01((Time.time - _rampStart) / _rampSeconds) : 1f;
            Weight = Mathf.Lerp(_rampFrom, _goal, Mathf.SmoothStep(0f, 1f, u));

            ApplyVolume();
            ApplyNoise();
        }

        static int LocalDrinkCount()
        {
            var cat = PlayerCat.Local;
            return cat != null && cat.Object != null && cat.Object.IsValid ? cat.DrinkCount : 0;
        }

        static float TierGain(int drinks)
        {
            if (drinks >= VeryDrunkAt)
                return VeryDrunkGain;
            if (drinks == PlayerCat.DrunkAt + 1)
                return DrunkGain;
            return drinks >= PlayerCat.DrunkAt ? TipsyGain : 0f;
        }

        // ---------------------------------------------------------------- Post-processing

        void ApplyVolume()
        {
            if (Weight <= 0f)
            {
                if (_volume != null && _volume.enabled)
                {
                    _volume.weight = 0f;
                    _volume.enabled = false;
                }

                return;
            }

            if (_volume == null)
                _volume = CreateVolume();
            if (!_volume.enabled)
                _volume.enabled = true;
            _volume.weight = Weight;
        }

        Volume CreateVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "TipsyProfile";
            var lens = profile.Add<LensDistortion>(true);
            lens.intensity.Override(LensIntensity);
            lens.scale.Override(LensScale);
            lens.xMultiplier.Override(1f);
            lens.yMultiplier.Override(1f);
            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(Aberration);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(Saturation);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(VignetteSmoothness);
            vignette.color.Override(VignetteColor);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);

            var volume = new GameObject("TipsyVolume").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.weight = 0f;
            volume.profile = profile;
            return volume;
        }

        // ---------------------------------------------------------------- Camera sway

        /// <summary>A camera's noise component and what it had before the sway took it over, to put back when sober.</summary>
        class Sway
        {
            public CinemachineCamera Camera;
            public CinemachineBasicMultiChannelPerlin Perlin;
            public bool Active;
            public NoiseSettings Profile;
            public float Amplitude;
            public float Frequency;
            public bool WasEnabled;
        }

        /// <summary>
        /// The follow camera and the seat camera (if any) sway; a focus camera (the roulette wheel) gets none. A
        /// camera's noise is taken over when it starts swaying and handed back when it stops, not every frame.
        /// </summary>
        void ApplyNoise()
        {
            var rig = Weight > 0f ? PlayerCameraRig.Instance : null;
            var follow = rig != null ? rig.FollowCamera : null;
            var seat = rig != null ? rig.OverrideCamera : null;

            foreach (var sway in _sways)
            {
                var wanted = sway.Camera != null && (sway.Camera == follow || sway.Camera == seat);
                if (sway.Active && !wanted)
                    Release(sway);
            }

            if (follow != null)
                Hold(follow);
            if (seat != null)
                Hold(seat);
        }

        void Hold(CinemachineCamera camera)
        {
            var sway = SwayFor(camera);
            if (!sway.Active)
            {
                var perlin = sway.Perlin;
                sway.Profile = perlin.NoiseProfile;
                sway.Amplitude = perlin.AmplitudeGain;
                sway.Frequency = perlin.FrequencyGain;
                sway.WasEnabled = perlin.enabled;
                perlin.NoiseProfile = Noise();
                perlin.FrequencyGain = 1f;
                perlin.enabled = true;
                sway.Active = true;
            }

            sway.Perlin.AmplitudeGain = Weight;
        }

        static void Release(Sway sway)
        {
            sway.Active = false;
            var perlin = sway.Perlin;
            if (perlin == null)
                return;
            perlin.NoiseProfile = sway.Profile;
            perlin.AmplitudeGain = sway.Amplitude;
            perlin.FrequencyGain = sway.Frequency;
            perlin.enabled = sway.WasEnabled;
        }

        Sway SwayFor(CinemachineCamera camera)
        {
            foreach (var known in _sways)
            {
                if (known.Camera == camera && known.Perlin != null)
                    return known;
            }

            var perlin = camera.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (perlin == null)
            {
                // Added disabled, so handing it back leaves the camera as it was.
                perlin = camera.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
                perlin.enabled = false;
            }

            var sway = new Sway { Camera = camera, Perlin = perlin };
            _sways.RemoveAll(s => s.Camera == null || s.Camera == camera);
            _sways.Add(sway);
            return sway;
        }

        NoiseSettings Noise()
        {
            if (_noise != null)
                return _noise;
            _noise = ScriptableObject.CreateInstance<NoiseSettings>();
            _noise.name = "TipsySway";
            _noise.PositionNoise = System.Array.Empty<NoiseSettings.TransformNoiseParams>();
            _noise.OrientationNoise = new[]
            {
                new NoiseSettings.TransformNoiseParams
                {
                    X = new NoiseSettings.NoiseParams { Amplitude = PitchAmplitude, Frequency = PitchFrequency },
                    Y = new NoiseSettings.NoiseParams { Amplitude = YawAmplitude, Frequency = YawFrequency },
                    Z = new NoiseSettings.NoiseParams { Amplitude = RollAmplitude, Frequency = RollFrequency },
                },
            };
            return _noise;
        }
    }
}
