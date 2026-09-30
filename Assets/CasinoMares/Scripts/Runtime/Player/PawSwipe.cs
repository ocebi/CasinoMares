using CasinoMares.Core;
using CasinoMares.Props;
using Fusion;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Left click swats with a front paw (everyone sees the swipe). If a <see cref="Knockable"/> is in reach in
    /// front of the cat, it gets knocked away just as the paw lands.
    /// </summary>
    public class PawSwipe : NetworkBehaviour
    {
        [SerializeField] float _reach = 1.0f;
        [SerializeField] float _maxAngle = 65f;
        [SerializeField] float _cooldown = 0.6f;
        [SerializeField] float _hitDelay = 0.2f;
        [SerializeField] float _pushSpeed = 2.2f;

        PlayerCat _cat;
        float _nextSwipe;
        Knockable _target;
        float _hitAt;

        void Awake() => _cat = GetComponent<PlayerCat>();

        void Update()
        {
            if (Object == null || !Object.IsValid || !HasStateAuthority)
                return;

            if (_target != null && Time.time >= _hitAt)
            {
                var direction = _target.Center - transform.position;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
                // Mostly away from the cat, a bit to the paw's side, and slightly up so it tips over the edge.
                _target.Push((direction + transform.right * 0.25f).normalized * _pushSpeed + Vector3.up * 0.8f);
                _target = null;
            }

            if (!GameInput.PawPressed() || _cat == null || _cat.IsSeated || Time.time < _nextSwipe)
                return;

            _nextSwipe = Time.time + _cooldown;
            _cat.PlaySwipe();
            _target = Knockable.FindInReach(transform, _reach, _maxAngle);
            _hitAt = Time.time + _hitDelay;
        }
    }
}
