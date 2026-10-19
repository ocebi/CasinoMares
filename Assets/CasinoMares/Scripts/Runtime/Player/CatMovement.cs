using CasinoMares.Core;
using Fusion;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Turns local input into camera-relative movement for the cat (GDD §3.4).
    /// Fusion's <see cref="NetworkCharacterController"/> does the moving: it runs the CharacterController inside the
    /// network tick, keeps the controller's internal position in sync with the networked state, and
    /// render-interpolates the transform. Moving a CharacterController next to a plain NetworkTransform jitters,
    /// because render interpolation leaks into the controller's position.
    /// </summary>
    [RequireComponent(typeof(NetworkCharacterController))]
    public class CatMovement : NetworkBehaviour
    {
        /// <summary>A cat below this has fallen out of the world (off the seaside terrace, say) and goes back to its spawn.</summary>
        const float FallOutHeight = -2f;
        /// <summary>Degrees a second a cat turns to face something it was told to (a clicked spot it swipes at).</summary>
        const float FaceTurnSpeed = 900f;

        /// <summary>Set while seated or otherwise not allowed to walk around.</summary>
        public bool MovementLocked { get; set; }

        /// <summary>Networked by the NetworkCharacterController, so it's valid on every client.</summary>
        public bool IsAirborne => Object != null && Object.IsValid && !_character.Grounded;

        NetworkCharacterController _character;
        bool _jumpRequested;
        bool _teleportRequested;
        Vector3 _teleportPosition;
        Quaternion _teleportRotation;
        Vector3 _spawnPosition;
        Quaternion _spawnRotation;
        Vector3 _faceDirection;
        float _faceUntil;

        /// <summary>Move the cat instantly (sitting down, standing up). Applied on the next network tick.</summary>
        public void RequestTeleport(Vector3 position, Quaternion rotation)
        {
            _teleportRequested = true;
            _teleportPosition = position;
            _teleportRotation = rotation;
        }

        /// <summary>
        /// Turn to face <paramref name="direction"/> (flat) and hold it for <paramref name="seconds"/>, even while
        /// walking. Turns on the next network ticks.
        /// </summary>
        public void FaceFor(Vector3 direction, float seconds)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            _faceDirection = direction.normalized;
            _faceUntil = Time.time + seconds;
        }

        void Awake()
        {
            _character = GetComponent<NetworkCharacterController>();
            _character.rotationSpeed = CatMotion.TurnSharpness;
        }

        public override void Spawned()
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        void Update()
        {
            // Ticks don't run every frame, so remember a jump press until the next tick consumes it.
            if (Object != null && Object.IsValid && HasStateAuthority && GameInput.JumpPressed())
                _jumpRequested = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            if (!_teleportRequested && transform.position.y < FallOutHeight)
                RequestTeleport(_spawnPosition, _spawnRotation);

            if (_teleportRequested)
            {
                _teleportRequested = false;
                _character.Teleport(_teleportPosition, _teleportRotation);
                _character.Velocity = Vector3.zero;
            }

            // Seated cats stay exactly where they were placed (no gravity pulling them off the stool).
            if (MovementLocked)
            {
                _jumpRequested = false;
                return;
            }

            var input = GameInput.ReadMove();
            _character.maxSpeed = MaxSpeedFor(input.magnitude, GameInput.IsSprinting());

            // Floaty cat jumps: full gravity on the way up, gentler on the way down.
            _character.gravity = _character.Grounded || _character.Velocity.y > 0f ? CatMotion.RiseGravity : CatMotion.FallGravity;
            if (_jumpRequested)
                _character.Jump(overrideImpulse: CatMotion.JumpImpulse);
            _jumpRequested = false;

            // Move() also applies gravity and braking, so it runs every tick even without input. It also turns the cat
            // towards where it walks, which facing something overrides.
            var rotation = transform.rotation;
            _character.Move(CameraRelative(input));
            if (Time.time < _faceUntil)
                transform.rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(_faceDirection, Vector3.up),
                    FaceTurnSpeed * Runner.DeltaTime);

            var velocity = _character.Velocity;
            if (velocity.y < -CatMotion.MaxFallSpeed)
            {
                velocity.y = -CatMotion.MaxFallSpeed;
                _character.Velocity = velocity;
            }
        }

        /// <summary>
        /// The controller normalizes the direction, so the push's length has to become the speed cap: a gentle
        /// joystick push walks slowly, a full one trots, a key (always 1) trots, Shift or the stick's rim runs.
        /// </summary>
        static float MaxSpeedFor(float magnitude, bool sprinting)
        {
            if (sprinting)
                return CatMotion.RunSpeed;
            var push = Mathf.InverseLerp(CatMotion.SlowWalkInput, CatMotion.FullWalkInput, magnitude);
            return Mathf.Lerp(CatMotion.SlowWalkSpeed, CatMotion.WalkSpeed, push);
        }

        static Vector3 CameraRelative(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            var cam = Camera.main;
            if (cam == null)
                return new Vector3(input.x, 0f, input.y);

            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            return forward * input.y + right * input.x;
        }
    }
}
