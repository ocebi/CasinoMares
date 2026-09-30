using System.Collections.Generic;
using UnityEngine;

namespace CasinoMares.Audio
{
    /// <summary>
    /// Plays one-shot sounds from the <see cref="SoundBank"/> through a small pool of AudioSources, positional
    /// when a position is given (GDD §11: nearby tables sound louder). Cat voices (meow, purr, hiss...) and prop
    /// sounds are synthesized at startup by <see cref="CatVoice"/>.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        const string VolumeKey = "MasterVolume";
        const int PoolSize = 24;

        [SerializeField] SoundBank _bank;

        static Sfx _instance;

        readonly List<AudioSource> _pool = new();
        readonly Dictionary<SoundId, (AudioClip[] clips, float volume, Vector2 pitch)> _synthesized = new();
        int _next;

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                AudioListener.volume = Mathf.Clamp01(value);
            }
        }

        void Awake()
        {
            _instance = this;
            AudioListener.volume = MasterVolume;
            for (var i = 0; i < PoolSize; i++)
            {
                var source = new GameObject($"Voice_{i}").AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1.5f;
                source.maxDistance = 14f;
                source.dopplerLevel = 0f;
                _pool.Add(source);
            }

            _synthesized[SoundId.Meow] = (new[] { CatVoice.Meow(1f, 0), CatVoice.Meow(1.18f, 1), CatVoice.Meow(0.86f, 2) }, 0.35f, new Vector2(0.92f, 1.1f));
            _synthesized[SoundId.Purr] = (new[] { CatVoice.Purr(0), CatVoice.Purr(1) }, 0.55f, new Vector2(0.95f, 1.05f));
            _synthesized[SoundId.Hiss] = (new[] { CatVoice.Hiss(0) }, 0.32f, new Vector2(0.9f, 1.1f));
            _synthesized[SoundId.Chirp] = (new[] { CatVoice.Chirp(0), CatVoice.Chirp(1) }, 0.35f, new Vector2(0.92f, 1.12f));
            _synthesized[SoundId.Chatter] = (new[] { CatVoice.Chatter(0) }, 0.3f, new Vector2(0.95f, 1.1f));
            _synthesized[SoundId.Yowl] = (new[] { CatVoice.Meow(0.72f, 3, 1.5f) }, 0.4f, new Vector2(0.95f, 1.02f));
            _synthesized[SoundId.Swish] = (new[] { CatVoice.Swish(0) }, 0.35f, new Vector2(0.9f, 1.15f));
            _synthesized[SoundId.PropClink] = (new[] { CatVoice.Clink(0), CatVoice.Clink(1) }, 0.45f, new Vector2(0.9f, 1.1f));
            _synthesized[SoundId.PropThud] = (new[] { CatVoice.Thud(0) }, 0.6f, new Vector2(0.85f, 1.15f));
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>2D sound (UI, your own results).</summary>
        public static void Play(SoundId id, float volume = 1f) => _instance?.PlayInternal(id, null, volume);

        /// <summary>3D sound at a world position.</summary>
        public static void PlayAt(SoundId id, Vector3 position, float volume = 1f) => _instance?.PlayInternal(id, position, volume);

        void PlayInternal(SoundId id, Vector3? position, float volume)
        {
            AudioClip clip;
            float baseVolume;
            Vector2 pitchRange;

            if (_synthesized.TryGetValue(id, out var synth))
            {
                clip = synth.clips[Random.Range(0, synth.clips.Length)];
                baseVolume = synth.volume;
                pitchRange = synth.pitch;
            }
            else if (_bank != null && _bank.TryGet(id, out var entry))
            {
                clip = entry.Clips[Random.Range(0, entry.Clips.Length)];
                baseVolume = entry.Volume <= 0f ? 1f : entry.Volume;
                pitchRange = entry.PitchRange == Vector2.zero ? Vector2.one : entry.PitchRange;
            }
            else
            {
                return;
            }

            var source = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            source.Stop();
            source.clip = clip;
            source.volume = baseVolume * volume;
            source.pitch = Random.Range(pitchRange.x, pitchRange.y);
            source.spatialBlend = position.HasValue ? 1f : 0f;
            if (position.HasValue)
                source.transform.position = position.Value;
            source.Play();
        }
    }

    /// <summary>
    /// Tiny procedural cat voices and prop sounds, so no audio files are needed for them. Everything is mono
    /// 22 kHz, built once at startup.
    /// </summary>
    static class CatVoice
    {
        const int SampleRate = 22050;

        /// <summary>A pitch-gliding harmonic tone with a vowel-like brightness sweep. Longer and lower is a yowl.</summary>
        public static AudioClip Meow(float pitch, int seed, float length = 1f)
        {
            var duration = 0.55f * length / Mathf.Sqrt(pitch);
            var samples = new float[(int)(SampleRate * duration)];
            var random = new System.Random(seed);
            var phase = 0.0;

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)samples.Length;
                // "m-EE-ow": pitch rises then falls; brightness opens up in the middle.
                var frequency = pitch * (420f + 380f * Mathf.Sin(t * Mathf.PI * 0.9f) - 120f * t);
                frequency *= 1f + (0.015f + 0.02f * (length - 1f)) * Mathf.Sin(i * 0.004f * length);
                phase += 2.0 * Mathf.PI * frequency / SampleRate;

                var brightness = Mathf.Sin(Mathf.Clamp01(t * 1.3f) * Mathf.PI);
                var value = 0f;
                for (var harmonic = 1; harmonic <= 7; harmonic++)
                {
                    var weight = harmonic == 1 ? 1f : Mathf.Pow(0.55f + 0.35f * brightness, harmonic - 1) / harmonic * 2.2f;
                    value += weight * Mathf.Sin((float)(phase * harmonic));
                }

                var envelope = Mathf.Min(1f, t * 14f) * Mathf.Pow(1f - t, 1.6f);
                var breath = Noise(random) * 0.04f;
                samples[i] = (value * 0.22f + breath) * envelope;
            }

            return Clip($"Meow_{seed}", samples);
        }

        /// <summary>Low noise pulses at ~25 Hz, in breathing cycles (in louder, out softer).</summary>
        public static AudioClip Purr(int seed)
        {
            var samples = new float[(int)(SampleRate * 2.6f)];
            var random = new System.Random(seed);
            var brown = 0f;
            var low = 0f;
            var phase = 0.0;
            const float breath = 0.85f;

            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (float)SampleRate;
                var inhale = Mathf.Repeat(time, breath * 2f) < breath;
                var cycle = Mathf.Repeat(time, breath) / breath;
                phase += 2.0 * Mathf.PI * (inhale ? 27f : 23f) / SampleRate;

                brown = brown * 0.985f + Noise(random) * 0.15f;
                low += (brown - low) * 0.12f;
                var pulse = Mathf.Pow(0.5f + 0.5f * Mathf.Sin((float)phase), 3f);
                var cycleEnvelope = Mathf.Sin(cycle * Mathf.PI) * (inhale ? 1f : 0.7f);
                var fade = Mathf.Min(1f, time * 6f) * Mathf.Min(1f, (samples.Length - i) / (SampleRate * 0.3f));
                samples[i] = low * pulse * cycleEnvelope * fade * 3.2f;
            }

            return Clip($"Purr_{seed}", samples);
        }

        /// <summary>A breathy burst of high-passed noise.</summary>
        public static AudioClip Hiss(int seed)
        {
            var samples = new float[(int)(SampleRate * 0.9f)];
            var random = new System.Random(seed);
            var previous = 0f;
            var smooth = 0f;

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)samples.Length;
                var white = Noise(random);
                var high = white - 0.92f * previous;
                previous = white;
                smooth += (high - smooth) * 0.55f;
                var envelope = Mathf.Min(1f, t * 18f) * Mathf.Pow(1f - t, 1.3f) * (0.85f + 0.15f * Mathf.Sin(t * 40f));
                samples[i] = smooth * envelope * 0.8f;
            }

            return Clip($"Hiss_{seed}", samples);
        }

        /// <summary>"Mrrp!": a short rising trill.</summary>
        public static AudioClip Chirp(int seed)
        {
            var samples = new float[(int)(SampleRate * 0.3f)];
            var phase = 0.0;
            var baseFrequency = seed == 0 ? 520f : 600f;

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)samples.Length;
                var frequency = baseFrequency + 420f * Mathf.SmoothStep(0f, 1f, t);
                phase += 2.0 * Mathf.PI * frequency / SampleRate;
                var trill = 0.55f + 0.45f * Mathf.Sin(i / (float)SampleRate * 2f * Mathf.PI * 34f);
                var value = Mathf.Sin((float)phase) + 0.45f * Mathf.Sin((float)(phase * 2)) + 0.2f * Mathf.Sin((float)(phase * 3));
                var envelope = Mathf.Min(1f, t * 12f) * Mathf.Pow(1f - t, 1.2f);
                samples[i] = value * trill * envelope * 0.35f;
            }

            return Clip($"Chirp_{seed}", samples);
        }

        /// <summary>The "ek-ek-ek" chatter a cat makes at birds: quick clicky tones.</summary>
        public static AudioClip Chatter(int seed)
        {
            var samples = new float[(int)(SampleRate * 0.95f)];
            var random = new System.Random(seed);
            const float spacing = 0.12f;
            const float length = 0.05f;

            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (float)SampleRate;
                var index = Mathf.FloorToInt(time / spacing);
                var local = time - index * spacing;
                if (index > 6 || local > length)
                    continue;
                var frequency = 950f + 90f * Mathf.Sin(index * 1.7f);
                var envelope = Mathf.Sin(local / length * Mathf.PI);
                samples[i] = (Mathf.Sin(2f * Mathf.PI * frequency * local) * 0.8f + Noise(random) * 0.25f) * envelope * 0.35f;
            }

            return Clip($"Chatter_{seed}", samples);
        }

        /// <summary>A quick whoosh for a paw swipe: noise through a sweeping low-pass.</summary>
        public static AudioClip Swish(int seed)
        {
            var samples = new float[(int)(SampleRate * 0.28f)];
            var random = new System.Random(seed);
            var low = 0f;

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)samples.Length;
                var cutoff = Mathf.Lerp(0.04f, 0.35f, Mathf.Sin(t * Mathf.PI));
                low += (Noise(random) - low) * cutoff;
                samples[i] = low * Mathf.Sin(t * Mathf.PI) * 0.9f;
            }

            return Clip($"Swish_{seed}", samples);
        }

        /// <summary>A glass clink: a few inharmonic partials ringing out.</summary>
        public static AudioClip Clink(int seed)
        {
            var samples = new float[(int)(SampleRate * 0.6f)];
            var random = new System.Random(seed);
            var partials = seed == 0 ? new[] { 2150f, 3320f, 4610f, 5890f } : new[] { 1880f, 2990f, 4210f, 6120f };

            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (float)SampleRate;
                var value = 0f;
                for (var p = 0; p < partials.Length; p++)
                    value += Mathf.Sin(2f * Mathf.PI * partials[p] * time) * Mathf.Exp(-time * (9f + p * 5f)) / (p + 1);
                var click = time < 0.004f ? Noise(random) * (1f - time / 0.004f) : 0f;
                samples[i] = (value * 0.5f + click * 0.4f) * 0.8f;
            }

            return Clip($"Clink_{seed}", samples);
        }

        /// <summary>A soft thud: a falling low tone plus a muffled noise hit.</summary>
        public static AudioClip Thud(int seed)
        {
            var samples = new float[(int)(SampleRate * 0.3f)];
            var random = new System.Random(seed);
            var low = 0f;
            var phase = 0.0;

            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (float)SampleRate;
                phase += 2.0 * Mathf.PI * Mathf.Lerp(120f, 65f, time / 0.3f) / SampleRate;
                low += (Noise(random) - low) * 0.08f;
                samples[i] = (Mathf.Sin((float)phase) * 0.7f + low * 1.5f) * Mathf.Exp(-time * 16f);
            }

            return Clip($"Thud_{seed}", samples);
        }

        static float Noise(System.Random random) => (float)random.NextDouble() * 2f - 1f;

        static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
