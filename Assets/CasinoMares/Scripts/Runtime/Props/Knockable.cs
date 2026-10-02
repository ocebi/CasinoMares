using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Player;
using Fusion;
using UnityEngine;

namespace CasinoMares.Props
{
    /// <summary>
    /// A glass, mug, bottle or yarn ball a cat can swat off a table (left click). Physics runs on whoever holds
    /// State Authority: the pusher requests it, so its own push is simulated locally with no delay, and
    /// NetworkTransform syncs the Rigidbody to everyone else (proxies are kinematic, PhysicsForecast is off).
    /// Once a knocked prop has rested for a while, it quietly goes back to its spot.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Knockable : NetworkBehaviour, IStateAuthorityChanged
    {
        public static readonly List<Knockable> All = new();

        [SerializeField] SoundId _impactSound = SoundId.PropClink;
        [SerializeField] float _resetAfter = 12f;
        [SerializeField] Vector3 _homePosition;
        [SerializeField] Quaternion _homeRotation = Quaternion.identity;

        /// <summary>Knocked away from its spot; networked so a new owner knows to put it back.</summary>
        [Networked] NetworkBool Displaced { get; set; }

        Rigidbody _body;
        NetworkTransform _networkTransform;
        Vector3? _pendingPush;
        float _lastImpact;
        float _restingSince;
        bool _knocked;

        public Vector3 Center => _body != null ? _body.worldCenterOfMass : transform.position;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
        }

        public override void Spawned() => All.Add(this);

        public override void Despawned(NetworkRunner runner, bool hasState) => All.Remove(this);

        /// <summary>Called by the local pusher. Takes State Authority first if someone else holds it.</summary>
        public void Push(Vector3 velocityChange)
        {
            if (Object == null || !Object.IsValid)
                return;

            if (HasStateAuthority)
            {
                ApplyPush(velocityChange);
                return;
            }

            _pendingPush = velocityChange;
            Object.RequestStateAuthority();
        }

        void IStateAuthorityChanged.StateAuthorityChanged()
        {
            // Lost it to another pusher: that client handles the reset now.
            if (!HasStateAuthority)
                _knocked = false;
        }

        void ApplyPush(Vector3 velocityChange)
        {
            Displaced = true;
            _knocked = true;
            _restingSince = Time.time;
            _body.WakeUp();
            _body.AddForce(velocityChange, ForceMode.VelocityChange);
            _body.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
        }

        void Update()
        {
            if (Object == null || !Object.IsValid)
                return;

            // Nobody owns it (its owner left): the master client adopts it so it can still reset.
            if (Object.StateAuthority == PlayerRef.None && Runner.IsSharedModeMasterClient)
                Object.RequestStateAuthority();

            if (!HasStateAuthority)
                return;

            // A push waiting for authority: apply it once NetworkTransform has made the body dynamic again.
            if (_pendingPush.HasValue && !_body.isKinematic)
            {
                ApplyPush(_pendingPush.Value);
                _pendingPush = null;
            }

            // The owner puts it back once it has been resting for a while. A new owner also finds out from the
            // networked flag that it was left knocked over.
            if (!_knocked && !Displaced)
                return;
            _knocked = true;

            var fellOut = transform.position.y < -2f;
            if (!fellOut && !_body.IsSleeping() && _body.linearVelocity.sqrMagnitude > 0.0025f)
            {
                _restingSince = Time.time;
                return;
            }

            if (_restingSince <= 0f)
                _restingSince = Time.time;
            if (!fellOut && Time.time - _restingSince < _resetAfter)
                return;

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.position = _homePosition;
            _body.rotation = _homeRotation;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _networkTransform.Teleport(_homePosition, _homeRotation);
            _body.Sleep();
            Displaced = false;
            _knocked = false;
            _restingSince = 0f;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Object == null || !Object.IsValid || !HasStateAuthority)
                return;
            var speed = collision.relativeVelocity.magnitude;
            if (speed < 0.7f || Time.time - _lastImpact < 0.12f)
                return;
            _lastImpact = Time.time;
            RPC_Impact((byte)Mathf.Clamp(speed * 40f, 0f, 255f));
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Impact(byte strength)
        {
            Sfx.PlayAt(_impactSound, transform.position, Mathf.Clamp01(strength / 160f));
        }

        /// <summary>The best prop within paw reach in front of a cat, or null.</summary>
        public static Knockable FindInReach(Transform cat, float reach, float maxAngle)
        {
            Knockable best = null;
            var bestDistance = float.MaxValue;
            foreach (var knockable in All)
            {
                if (knockable == null || !PawSwipe.InReach(cat, knockable.Center, reach, maxAngle, out var distance))
                    continue;
                if (distance < bestDistance)
                {
                    best = knockable;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
