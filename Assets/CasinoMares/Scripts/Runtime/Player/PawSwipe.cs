using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Props;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CasinoMares.Player
{
    /// <summary>
    /// Left click swats with a front paw (everyone sees the swipe). If a <see cref="Knockable"/> is in reach in
    /// front of the cat, it gets knocked away just as the paw lands; a patron cat in reach hisses and walks off.
    /// With a mouse cursor on screen (a desktop browser), the cat first turns to face the clicked spot and swats
    /// there, preferring the prop or cat that was clicked. A paw that lands earns a few chips, at most once per
    /// object every few minutes (GDD §7.2).
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
        /// <summary>Close enough to the clicked direction to swipe, and the longest the turn may take.</summary>
        const float AimTolerance = 10f;
        const float AimMaxTurnTime = 0.3f;
        /// <summary>Chips for pawing a prop or a patron, and how long until the same one pays again.</summary>
        const int PawReward = 10;
        static readonly System.TimeSpan PawRewardCooldown = System.TimeSpan.FromMinutes(5);

        PlayerCat _cat;
        CatMovement _movement;
        float _nextSwipe;
        Knockable _target;
        PatronBrain _patron;
        float _hitAt;
        bool _aiming;
        Vector3 _aimDirection;
        float _aimUntil;
        Knockable _clickedProp;
        PatronBrain _clickedPatron;

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

        void Awake()
        {
            _cat = GetComponent<PlayerCat>();
            _movement = GetComponent<CatMovement>();
        }

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
                if (_target.Object != null && _target.Object.IsValid)
                    EarnPawReward(_target);
                _target = null;
            }

            if (_patron != null && Time.time >= _hitAt)
            {
                if (_patron.Object != null && _patron.Object.IsValid)
                {
                    // A cat still spooked from an earlier paw ignores this one (see PatronBrain.RPC_Pawed), so it doesn't pay.
                    if (!_patron.IsSpooked)
                        EarnPawReward(_patron);
                    _patron.RPC_Pawed();
                }

                _patron = null;
            }

            if (_aiming)
            {
                if (_cat == null || _cat.IsSeated || _cat.Swimming)
                    _aiming = false;
                else if (Vector3.Angle(transform.forward, _aimDirection) <= AimTolerance || Time.time >= _aimUntil)
                    Swipe();
                return;
            }

            if (!GameInput.PawPressed() || _cat == null || _cat.IsSeated || _cat.Swimming || Time.time < _nextSwipe)
                return;

            _nextSwipe = Time.time + _cooldown;
            _clickedProp = null;
            _clickedPatron = null;
            if (_movement != null && TryAim(out _aimDirection))
            {
                // Hold the facing through the swipe, so a walking cat doesn't turn away before the paw lands.
                _movement.FaceFor(_aimDirection, AimMaxTurnTime + _hitDelay);
                _aimUntil = Time.time + AimMaxTurnTime;
                _aiming = true;
                return;
            }

            Swipe();
        }

        void Swipe()
        {
            _aiming = false;
            _cat.PlaySwipe();
            _target = _clickedProp != null && InReach(transform, _clickedProp.Center, _reach, _maxAngle, out _)
                ? _clickedProp
                : Knockable.FindInReach(transform, _reach, _maxAngle);
            _patron = _clickedPatron != null &&
                      InReach(transform, _clickedPatron.transform.position + Vector3.up * 0.2f, _reach + PatronBodyRadius, _maxAngle, out _)
                ? _clickedPatron
                : PatronBrain.FindInReach(transform, _reach + PatronBodyRadius, _maxAngle);
            _hitAt = Time.time + _hitDelay;
        }

        /// <summary>
        /// The paw landed on <paramref name="target"/>. Keyed by the object's place in the scene, so a reload doesn't
        /// reset its cooldown.
        /// </summary>
        static void EarnPawReward(Component target) =>
            ChipRewards.TryEarn("paw:" + ChipRewards.ScenePath(target.transform), PawReward, PawRewardCooldown);

        /// <summary>
        /// Where the mouse points, as a flat direction from the cat, when there's a cursor on screen to aim with (not
        /// on a phone, nor with the cursor locked). Also notes a prop or patron that was clicked on.
        /// </summary>
        bool TryAim(out Vector3 direction)
        {
            direction = default;
            var mouse = Mouse.current;
            var camera = Camera.main;
            if (Platform.IsTouch || mouse == null || camera == null || Cursor.lockState == CursorLockMode.Locked)
                return false;

            var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            Vector3 point;
            if (RaycastPastSelf(ray, out var hit))
            {
                point = hit.point;
                _clickedProp = hit.collider.GetComponentInParent<Knockable>();
                _clickedPatron = hit.collider.GetComponentInParent<PatronBrain>();
            }
            else if (new Plane(Vector3.up, transform.position).Raycast(ray, out var distance))
            {
                point = ray.GetPoint(distance);
            }
            else
            {
                // Up at the sky: the way the camera looks.
                point = transform.position + ray.direction;
            }

            direction = point - transform.position;
            direction.y = 0f;
            // Clicking the cat itself (or right at its feet) swats straight ahead.
            if (direction.sqrMagnitude < 0.04f)
                return false;
            direction.Normalize();
            return true;
        }

        /// <summary>The nearest solid thing the ray hits that isn't this cat (nor an invisible camera or cat barrier).</summary>
        bool RaycastPastSelf(Ray ray, out RaycastHit nearest)
        {
            nearest = default;
            var found = false;
            var layers = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("CameraBlocker", "CatBarrier");
            foreach (var hit in Physics.RaycastAll(ray, 200f, layers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || (found && hit.distance >= nearest.distance))
                    continue;
                nearest = hit;
                found = true;
            }

            return found;
        }
    }
}
