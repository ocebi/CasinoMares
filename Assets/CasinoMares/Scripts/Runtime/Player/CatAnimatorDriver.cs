using Fusion;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Drives a cat's Animator from what the cat is visibly doing, so no animation state has to be networked:
    /// speed comes from the (interpolated) position, airborne from <see cref="CatMovement"/>, sitting from whoever
    /// owns the cat (<see cref="PlayerCat"/> or an NPC script). Works for players and local NPC visuals alike.
    /// Another player's cat takes its speed from the networked velocity instead, so it keeps walking through a
    /// network stall rather than flickering to idle and back.
    /// </summary>
    public class CatAnimatorDriver : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int AirborneId = Animator.StringToHash("Airborne");
        static readonly int SittingId = Animator.StringToHash("Sitting");
        static readonly int DrinkingId = Animator.StringToHash("Drinking");

        /// <summary>Faster than any cat walks: a sit-down, stand-up or catch-up snap, not movement.</summary>
        const float TeleportSpeed = 12f;
        /// <summary>
        /// How long a remote cat without a networked velocity (a patron) keeps its last speed while it isn't moving,
        /// which is what it looks like when snapshots arrive late.
        /// </summary>
        const float StallHold = 0.35f;

        [SerializeField] Animator _animator;
        [SerializeField] CatMovement _movement;
        [SerializeField] float _speedSmoothing = 10f;

        public bool Sitting { get; set; }
        /// <summary>Lapping at a glass of wine (WineBar sets it); only plays while the cat sits.</summary>
        public bool Drinking { get; set; }
        public Animator Animator => _animator;

        NetworkObject _networkObject;
        NetworkCharacterController _character;
        Vector3 _lastPosition;
        float _speed;
        float _measured;
        float _stalledFor;

        void Awake()
        {
            _networkObject = GetComponentInParent<NetworkObject>();
            _character = GetComponent<NetworkCharacterController>();
        }

        void OnEnable()
        {
            _lastPosition = transform.position;
        }

        /// <summary>A networked cat someone else simulates, so this client only sees it through snapshots.</summary>
        bool IsRemote => _networkObject != null && _networkObject.IsValid && !_networkObject.HasStateAuthority;

        void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f || _animator == null)
                return;

            var delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;

            var measured = delta.magnitude / deltaTime;
            var remote = IsRemote;
            if (remote && _character != null)
            {
                var velocity = _character.Velocity;
                velocity.y = 0f;
                measured = velocity.magnitude;
                // Its velocity right after a teleport is the jump in position.
                if (measured > TeleportSpeed)
                    measured = 0f;
            }
            else if (remote && (measured == 0f || measured > TeleportSpeed) && _stalledFor < StallHold)
            {
                // Frozen while it waits for late snapshots, or snapping to catch up: keep the walk going.
                _stalledFor += deltaTime;
                measured = _measured;
            }
            else
            {
                // Teleports (sitting down, standing up) would read as a burst of speed; ignore big jumps.
                if (measured > TeleportSpeed)
                    measured = 0f;
                if (measured > 0f)
                    _stalledFor = 0f;
            }

            _measured = measured;
            _speed = Mathf.Lerp(_speed, measured, 1f - Mathf.Exp(-_speedSmoothing * deltaTime));

            _animator.SetFloat(SpeedId, _speed < 0.05f || Sitting ? 0f : _speed);
            _animator.SetBool(AirborneId, _movement != null && _movement.IsAirborne);
            _animator.SetBool(SittingId, Sitting);
            _animator.SetBool(DrinkingId, Drinking && Sitting);
        }
    }
}
