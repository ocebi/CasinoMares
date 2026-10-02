using CasinoMares.Audio;
using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Props
{
    /// <summary>
    /// The cat wall in the seaside hall: a cat tree, shelves, a rope bridge and stepping pads up the west wall to a
    /// cushion under the ceiling. Jumping off the rug at its foot starts a clock and reaching the cushion stops it;
    /// touching the floor on the way calls the climb off. Each player times their own climbs (nothing is networked),
    /// and a personal best is announced in the chat. <see cref="UI.CatWallHud"/> shows the clock.
    /// </summary>
    public class CatWall : MonoBehaviour
    {
        public enum Phase
        {
            /// <summary>Not on the wall (or seated somewhere).</summary>
            Idle,
            /// <summary>On the start rug: the clock starts on leaving it.</summary>
            Ready,
            Climbing,
            /// <summary>Just reached the top; the time stays up for a few seconds.</summary>
            Finished,
        }

        const string BestKey = "CatWallBest";
        /// <summary>Standing lower than this is standing on the floor.</summary>
        const float FloorHeight = 0.2f;
        const float GiveUpAfter = 300f;
        const float ShowFinishFor = 6f;

        [Tooltip("The rug at the foot of the wall, as a box in world space.")]
        [SerializeField] Bounds _start;
        [Tooltip("The cushion at the top.")]
        [SerializeField] Bounds _top;

        public static CatWall Instance { get; private set; }

        public Phase State { get; private set; }

        /// <summary>Seconds on the clock: running while climbing, the final time once finished.</summary>
        public float Elapsed => State == Phase.Climbing ? Time.time - _startTime : _time;

        /// <summary>The local player's best climb in seconds, or 0 before the first.</summary>
        public static float Best => PlayerPrefs.GetFloat(BestKey, 0f);

        /// <summary>The climb just finished beat the previous best.</summary>
        public bool NewBest { get; private set; }

        CatMovement _movement;
        float _startTime;
        float _time;
        float _finishedAt;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            var cat = PlayerCat.Local;
            if (cat == null || cat.Object == null || !cat.Object.IsValid || cat.IsSeated)
            {
                if (State != Phase.Finished)
                    State = Phase.Idle;
                _movement = null;
                return;
            }

            if (_movement == null)
                _movement = cat.GetComponent<CatMovement>();
            var position = cat.transform.position;
            var standing = _movement != null && !_movement.IsAirborne;
            var onRug = _start.Contains(position);

            switch (State)
            {
                case Phase.Idle:
                case Phase.Finished:
                    if (State == Phase.Finished && Time.time - _finishedAt < ShowFinishFor && !onRug)
                        break;
                    State = standing && onRug ? Phase.Ready : Phase.Idle;
                    break;

                case Phase.Ready:
                    if (!onRug)
                    {
                        State = Phase.Climbing;
                        _startTime = Time.time;
                        NewBest = false;
                    }

                    break;

                case Phase.Climbing:
                    if (standing && onRug)
                        State = Phase.Ready;
                    else if (standing && position.y < FloorHeight || Time.time - _startTime > GiveUpAfter)
                        State = Phase.Idle;
                    else if (standing && _top.Contains(position))
                        Finish(cat);
                    break;
            }
        }

        void Finish(PlayerCat cat)
        {
            _time = Time.time - _startTime;
            _finishedAt = Time.time;
            State = Phase.Finished;

            var best = Best;
            NewBest = best <= 0f || _time < best;
            Sfx.Play(SoundId.Win);
            if (!NewBest)
            {
                Toasts.Show($"Top of the cat wall in {Format(_time)}! (Best {Format(best)})");
                return;
            }

            PlayerPrefs.SetFloat(BestKey, _time);
            PlayerPrefs.Save();
            Toasts.Show(best <= 0f ? $"Top of the cat wall in {Format(_time)}!" : $"New best on the cat wall: {Format(_time)}!");
            cat.Announce($"{cat.DisplayName} climbed the cat wall in {Format(_time)}");
        }

        /// <summary>Seconds as 12.3 s, or 1:02.3 from a minute.</summary>
        public static string Format(float seconds)
        {
            var tenths = Mathf.FloorToInt(seconds * 10f);
            var minutes = tenths / 600;
            var rest = (tenths % 600) / 10f;
            return minutes > 0 ? $"{minutes}:{rest:00.0}" : $"{rest:0.0} s";
        }
    }
}
