using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Props;
using Fusion;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Left click swats with a front paw (everyone sees the swipe). If a <see cref="Knockable"/> is in reach in
    /// front of the cat, it gets knocked away just as the paw lands; a patron cat in reach hisses and walks off.
    /// </summary>
    public class PawSwipe : NetworkBehaviour
    {
        [SerializeField] float _reach = 1.0f;
        [SerializeField] float _maxAngle = 65f;
        [SerializeField] float _cooldown = 0.6f;
        [SerializeField] float _hitDelay = 0.2f;
        [SerializeField] float _pushSpeed = 2.2f;

        /// <summary>A patron is a whole cat, not a point: reach its middle from a little further away.</summary>
        const float PatronBodyRadius = 0.3f;

        PlayerCat _cat;
        float _nextSwipe;
        Knockable _target;
        PatronBrain _patron;
        float _hitAt;

        /// <summary>
        /// Whether <paramref name="point"/> is within paw reach in front of the cat: from a paw on the floor up to a
        /// table rim or bar top a little above its head, close enough and not too far to the side.
        /// </summary>
        public static bool InReach(Transform cat, Vector3 point, float reach, float maxAngle, out float distance)
        {
            var offset = point - cat.position;
            distance = 0f;
            if (offset.y < -0.25f || offset.y > 1.0f)
                return false;
            offset.y = 0f;
            distance = offset.magnitude;
            return distance <= reach && distance >= 0.01f && Vector3.Angle(cat.forward, offset) <= maxAngle;
        }

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

            if (_patron != null && Time.time >= _hitAt)
            {
                if (_patron.Object != null && _patron.Object.IsValid)
                    _patron.RPC_Pawed();
                _patron = null;
            }

            if (!GameInput.PawPressed() || _cat == null || _cat.IsSeated || Time.time < _nextSwipe)
                return;

            _nextSwipe = Time.time + _cooldown;
            _cat.PlaySwipe();
            _target = Knockable.FindInReach(transform, _reach, _maxAngle);
            _patron = PatronBrain.FindInReach(transform, _reach + PatronBodyRadius, _maxAngle);
            _hitAt = Time.time + _hitDelay;
        }
    }
}
