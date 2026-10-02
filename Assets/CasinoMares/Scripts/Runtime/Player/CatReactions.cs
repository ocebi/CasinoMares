using System.Collections.Generic;
using CasinoMares.Audio;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Local cat reactions and emotes (GDD §5.8). Every client triggers these from the same networked results or
    /// RPCs. "Happy" is a procedural hop with a squash (the pack has no happy clip), "angry" plays the pounce, and
    /// the paw swipe plays the pounce's reaching foreleg on its own layer.
    /// </summary>
    public class CatReactions : MonoBehaviour
    {
        static readonly int AngryId = Animator.StringToHash("Angry");
        static readonly int EmoteId = Animator.StringToHash("Emote");
        static readonly int EmoteTriggerId = Animator.StringToHash("EmoteTrigger");
        static readonly int LocomotionState = Animator.StringToHash("Locomotion");
        static readonly int SwipeState = Animator.StringToHash("Swipe");

        /// <summary>Animator layer with only the right foreleg (see CasinoSceneBuilder).</summary>
        const int PawLayer = 1;
        /// <summary>Where the reach starts in the pounce clip, and how long the swipe lasts.</summary>
        const float SwipeClipStart = 0.45f;
        const float SwipeDuration = 0.55f;
        /// <summary>The cat rears up on its hind legs while swiping, so the paw reaches table height.</summary>
        const float SwipeRearDegrees = 24f;
        /// <summary>Roughly where the hind paws stand, in the cat's local space (the rear-up pivots there).</summary>
        static readonly Vector3 HindPivot = new(0f, 0f, -0.35f);

        [SerializeField] Transform _model;
        [SerializeField] Animator _animator;
        [SerializeField] float _hopHeight = 0.35f;
        [SerializeField] float _hopDuration = 0.45f;

        readonly List<(float time, SoundId sound, float volume)> _pendingSounds = new();
        Vector3 _restPosition;
        Quaternion _restRotation;
        Vector3 _restScale;
        float _hopTime = -1f;
        int _hopsLeft;
        float _hopHeightNow;
        float _squashNow = 1f;
        float _swipeTime = -1f;
        float _rearNow;
        float _happyAt = -1f;
        int _delayedHops;
        float _angryAt = -1f;

        /// <summary>A short random wait so a table of cats doesn't react in perfect unison.</summary>
        public static float StaggerDelay() => Random.Range(0.05f, 0.7f);

        void Awake()
        {
            if (_model == null)
                _model = transform;
            _restPosition = _model.localPosition;
            _restRotation = _model.localRotation;
            _restScale = _model.localScale;
        }

        /// <summary>A happy hop (or several for a blackjack), optionally after <paramref name="delay"/> seconds.</summary>
        public void PlayHappy(int hops = 1, float delay = 0f)
        {
            if (delay > 0f)
            {
                _happyAt = Time.time + delay;
                _delayedHops = hops;
                return;
            }

            _hopsLeft = Mathf.Max(1, hops);
            _hopTime = 0f;
            Sfx.PlayAt(SoundId.Meow, transform.position);
        }

        public void PlayAngry(float delay = 0f)
        {
            if (delay > 0f)
            {
                _angryAt = Time.time + delay;
                return;
            }

            if (_animator != null)
                _animator.SetTrigger(AngryId);
        }

        /// <summary>
        /// Play an emote (animation plus its sound). Ignored unless the cat is standing on the ground; returns whether
        /// it started.
        /// </summary>
        public bool PlayEmote(CatEmote emote)
        {
            if (emote == CatEmote.Happy)
            {
                PlayHappy(2);
                return true;
            }

            // Emotes start from the locomotion state only; a trigger set anywhere else would fire later.
            if (_animator == null || emote == CatEmote.None || _animator.IsInTransition(0) ||
                _animator.GetCurrentAnimatorStateInfo(0).shortNameHash != LocomotionState)
                return false;

            _animator.SetInteger(EmoteId, (int)emote);
            _animator.SetTrigger(EmoteTriggerId);
            if (CatEmotes.TryGetSound(emote, out var sound, out var delay, out var volume))
                _pendingSounds.Add((Time.time + delay, sound, volume));
            return true;
        }

        /// <summary>A quick swat with the right front paw, on top of whatever the cat is doing.</summary>
        public void PlaySwipe()
        {
            Sfx.PlayAt(SoundId.Swish, transform.position + transform.forward * 0.3f + Vector3.up * 0.4f);
            if (_animator == null || _animator.layerCount <= PawLayer)
                return;
            _animator.Play(SwipeState, PawLayer, SwipeClipStart);
            _swipeTime = 0f;
        }

        void Update()
        {
            if (_happyAt >= 0f && Time.time >= _happyAt)
            {
                _happyAt = -1f;
                PlayHappy(_delayedHops);
            }

            if (_angryAt >= 0f && Time.time >= _angryAt)
            {
                _angryAt = -1f;
                PlayAngry();
            }

            UpdateSounds();
            var posed = _swipeTime >= 0f || _hopTime >= 0f;
            UpdateSwipe();
            UpdateHop();
            if (posed)
                ApplyModelPose();
        }

        /// <summary>Hop (up and squash) and swipe rear-up (pitch around the hind paws) combined on the model.</summary>
        void ApplyModelPose()
        {
            var pitch = Quaternion.Euler(-_rearNow, 0f, 0f);
            _model.localRotation = _restRotation * pitch;
            _model.localPosition = _restPosition + Vector3.up * _hopHeightNow + (HindPivot - pitch * HindPivot);
            _model.localScale = new Vector3(_restScale.x / _squashNow, _restScale.y * _squashNow, _restScale.z / _squashNow);
        }

        void UpdateSounds()
        {
            for (var i = _pendingSounds.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pendingSounds[i].time)
                    continue;
                Sfx.PlayAt(_pendingSounds[i].sound, transform.position + Vector3.up * 0.4f, _pendingSounds[i].volume);
                _pendingSounds.RemoveAt(i);
            }
        }

        void UpdateSwipe()
        {
            if (_swipeTime < 0f || _animator == null)
                return;

            _swipeTime += Time.deltaTime;
            var t = _swipeTime / SwipeDuration;
            // Blend the paw layer in fast and out a little slower.
            var weight = t < 0.15f ? t / 0.15f : t > 0.7f ? Mathf.Clamp01((1f - t) / 0.3f) : 1f;
            _animator.SetLayerWeight(PawLayer, weight);
            _rearNow = SwipeRearDegrees * Mathf.SmoothStep(0f, 1f, weight);
            if (t >= 1f)
            {
                _animator.SetLayerWeight(PawLayer, 0f);
                _rearNow = 0f;
                _swipeTime = -1f;
            }
        }

        void UpdateHop()
        {
            if (_hopTime < 0f)
                return;

            _hopTime += Time.deltaTime;
            var t = Mathf.Clamp01(_hopTime / _hopDuration);
            _hopHeightNow = Mathf.Sin(t * Mathf.PI) * _hopHeight;
            _squashNow = 1f + Mathf.Sin(t * Mathf.PI * 2f) * 0.08f;

            if (t < 1f)
                return;

            _hopsLeft--;
            _hopTime = _hopsLeft > 0 ? 0f : -1f;
            if (_hopTime < 0f)
            {
                _hopHeightNow = 0f;
                _squashNow = 1f;
            }
        }
    }
}
