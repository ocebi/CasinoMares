using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Drives a cat's Animator from what the cat is visibly doing, so no animation state has to be networked:
    /// speed comes from the (interpolated) position, airborne from <see cref="CatMovement"/>, sitting from whoever
    /// owns the cat (<see cref="PlayerCat"/> or an NPC script). Works for players and local NPC visuals alike.
    /// </summary>
    public class CatAnimatorDriver : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int AirborneId = Animator.StringToHash("Airborne");
        static readonly int SittingId = Animator.StringToHash("Sitting");

        [SerializeField] Animator _animator;
        [SerializeField] CatMovement _movement;
        [SerializeField] float _speedSmoothing = 10f;

        public bool Sitting { get; set; }
        public Animator Animator => _animator;

        Vector3 _lastPosition;
        float _speed;

        void OnEnable()
        {
            _lastPosition = transform.position;
        }

        void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f || _animator == null)
                return;

            var delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;

            // Teleports (sitting down, standing up) would read as a burst of speed; ignore big jumps.
            var measured = delta.magnitude / deltaTime;
            if (measured > 12f)
                measured = 0f;
            _speed = Mathf.Lerp(_speed, measured, 1f - Mathf.Exp(-_speedSmoothing * deltaTime));

            _animator.SetFloat(SpeedId, _speed < 0.05f || Sitting ? 0f : _speed);
            _animator.SetBool(AirborneId, _movement != null && _movement.IsAirborne);
            _animator.SetBool(SittingId, Sitting);
        }
    }
}
