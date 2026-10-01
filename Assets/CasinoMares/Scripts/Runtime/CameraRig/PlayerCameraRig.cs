using CasinoMares.Core;
using CasinoMares.Player;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.CameraRig
{
    /// <summary>
    /// Owns the local player's cameras (GDD §3.5): an overview camera for the menu and a third-person orbit camera
    /// that follows the local cat. Mouse look is fed straight into the orbital axes.
    /// The orbit follows a smoothed anchor instead of the cat itself, so a jump barely moves the view. While the
    /// cat walks, the orbit slowly swings behind it (turn off in the pause menu). At a table, A/D swing the seat
    /// camera a little way around the table (to see the other players' cards) and W/S tilt it; the view stays where
    /// it's left until the next time you sit down. A table can briefly hand the view to a focus camera (the roulette
    /// wheel during a spin) and take it back without losing that seated look.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerCameraRig : MonoBehaviour
    {
        const string SensitivityKey = "MouseSensitivity";
        const string AutoFollowKey = "CameraAutoFollow";
        const int ActivePriority = 20;
        const int OverridePriority = 30;
        const int FocusPriority = 40;
        const int InactivePriority = 0;

        [SerializeField] CinemachineCamera _menuCamera;
        [SerializeField] CinemachineCamera _followCamera;
        [SerializeField] float _defaultSensitivity = 0.12f;

        [Header("Jumps")]
        [Tooltip("How much of a jump's height the camera follows (0 = none, 1 = all of it).")]
        [SerializeField] float _jumpFollow = 0.15f;
        [SerializeField] float _heightSmoothTime = 0.18f;

        [Header("Auto follow")]
        [Tooltip("Degrees per second the orbit turns toward a cat walking sideways.")]
        [SerializeField] float _followTurnRate = 75f;
        [Tooltip("Seconds after the last mouse look before the orbit starts following again.")]
        [SerializeField] float _followDelay = 0.6f;

        [Header("Seated look")]
        [Tooltip("How far (degrees) the seat camera can swing around the table either way.")]
        [SerializeField] float _seatYaw = 35f;
        [SerializeField] float _seatPitch = 12f;
        [Tooltip("Degrees per second while A/D or W/S is held.")]
        [SerializeField] float _seatTurnSpeed = 40f;
        [Tooltip("The seat camera swings around a point this far ahead of it, roughly the middle of the felt.")]
        [SerializeField] float _seatPivotDistance = 1.8f;
        [SerializeField] float _seatLookSmoothTime = 0.12f;

        public static PlayerCameraRig Instance { get; private set; }

        CinemachineOrbitalFollow _orbital;
        Transform _target;
        Transform _anchor;
        CatMovement _movement;
        CinemachineCamera _override;
        CinemachineCamera _focus;
        Vector3 _overrideBasePosition;
        Quaternion _overrideBaseRotation;

        float _groundHeight;
        float _anchorHeight;
        float _anchorHeightVelocity;
        Vector3 _lastAnchorPosition;
        float _lastManualLook = -10f;
        Vector2 _seatLook;
        Vector2 _seatLookGoal;
        Vector2 _seatLookVelocity;

        /// <summary>True while a seat camera (or other fixed view) has taken over from the follow camera.</summary>
        public bool HasOverride => _override != null;

        public float Sensitivity
        {
            get => PlayerPrefs.GetFloat(SensitivityKey, _defaultSensitivity);
            set => PlayerPrefs.SetFloat(SensitivityKey, value);
        }

        /// <summary>The orbit swings behind the cat while it walks, so WASD alone can steer (on by default).</summary>
        public bool AutoFollow
        {
            get => PlayerPrefs.GetInt(AutoFollowKey, 1) == 1;
            set => PlayerPrefs.SetInt(AutoFollowKey, value ? 1 : 0);
        }

        void Awake()
        {
            Instance = this;
            _orbital = _followCamera.GetComponent<CinemachineOrbitalFollow>();
            _menuCamera.Priority = ActivePriority;
            _followCamera.Priority = InactivePriority;
            _anchor = new GameObject("CameraAnchor").transform;
            _anchor.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Follow(Transform target)
        {
            _target = target;
            _movement = target.GetComponentInParent<CatMovement>();
            _groundHeight = _anchorHeight = target.position.y;
            _anchorHeightVelocity = 0f;
            _anchor.position = _lastAnchorPosition = target.position;
            _followCamera.Follow = _anchor;
            _followCamera.LookAt = _anchor;

            // Start behind the cat.
            _orbital.HorizontalAxis.Value = target.eulerAngles.y;
            _followCamera.Priority = ActivePriority;
            _menuCamera.Priority = InactivePriority;
        }

        /// <summary>Blend to a fixed camera, e.g. the seat view at a table (GDD §3.5).</summary>
        public void SetOverride(CinemachineCamera camera)
        {
            ClearOverride();
            _override = camera;
            if (_override == null)
                return;
            _override.transform.GetPositionAndRotation(out _overrideBasePosition, out _overrideBaseRotation);
            _seatLook = _seatLookGoal = _seatLookVelocity = Vector2.zero;
            _override.Priority = OverridePriority;
        }

        public void ClearOverride()
        {
            ClearFocus();
            if (_override != null)
            {
                _override.transform.SetPositionAndRotation(_overrideBasePosition, _overrideBaseRotation);
                _override.Priority = InactivePriority;
            }

            _override = null;
        }

        /// <summary>Show this camera for a moment (seated only); the seat view comes back as it was on <see cref="ClearFocus"/>.</summary>
        public void SetFocus(CinemachineCamera camera)
        {
            if (_focus == camera)
                return;
            ClearFocus();
            _focus = camera;
            if (_focus != null)
                _focus.Priority = FocusPriority;
        }

        public void ClearFocus()
        {
            if (_focus != null)
                _focus.Priority = InactivePriority;
            _focus = null;
        }

        public void Release()
        {
            ClearOverride();
            _target = null;
            _movement = null;
            _followCamera.Priority = InactivePriority;
            _menuCamera.Priority = ActivePriority;
        }

        void Update()
        {
            if (_override != null)
            {
                if (_focus == null)
                    UpdateSeatLook();
                return;
            }

            if (_target == null || !GameInput.GameplayEnabled || GameInput.CursorFree)
                return;

            var look = GameInput.ReadLook() * Sensitivity;
            if (look.sqrMagnitude > 0.0001f)
                _lastManualLook = Time.time;

            ref var horizontal = ref _orbital.HorizontalAxis;
            ref var vertical = ref _orbital.VerticalAxis;
            horizontal.Value = horizontal.ClampValue(horizontal.Value + look.x + AutoFollowTurn(horizontal.Value));
            vertical.Value = vertical.ClampValue(vertical.Value - look.y);
        }

        /// <summary>
        /// Turns the orbit toward where the cat is walking: fastest when it walks sideways, not at all when it walks
        /// straight ahead or back toward the camera (which would spin the view around).
        /// </summary>
        float AutoFollowTurn(float cameraYaw)
        {
            if (!AutoFollow || Time.time - _lastManualLook < _followDelay || GameInput.ReadMove().sqrMagnitude < 0.04f)
                return 0f;

            var deltaTime = Time.deltaTime;
            var velocity = (_anchor.position - _lastAnchorPosition) / Mathf.Max(deltaTime, 0.0001f);
            velocity.y = 0f;
            if (velocity.sqrMagnitude < 0.3f * 0.3f)
                return 0f;

            var heading = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
            var delta = Mathf.DeltaAngle(cameraYaw, heading);
            if (Mathf.Abs(delta) > 115f)
                return 0f;
            return Mathf.Sign(delta) * Mathf.Sin(Mathf.Abs(delta) * Mathf.Deg2Rad) * _followTurnRate * deltaTime;
        }

        /// <summary>
        /// WASD (or the arrows), or a drag with touch controls, turns the seat camera within limits; it stays where
        /// it's left.
        /// </summary>
        void UpdateSeatLook()
        {
            var move = GameInput.ReadMove();
            var step = _seatTurnSpeed * Time.deltaTime;
            var drag = GameInput.ReadTouchLook() * Sensitivity;
            _seatLookGoal.x = Mathf.Clamp(_seatLookGoal.x + move.x * step + drag.x, -_seatYaw, _seatYaw);
            _seatLookGoal.y = Mathf.Clamp(_seatLookGoal.y - move.y * step - drag.y, -_seatPitch, _seatPitch);
            _seatLook = Vector2.SmoothDamp(_seatLook, _seatLookGoal, ref _seatLookVelocity, _seatLookSmoothTime);

            // Orbit around a point on the table rather than turning in place, so the table stays in view and the
            // other hands are seen from their side.
            var pivot = _overrideBasePosition + _overrideBaseRotation * Vector3.forward * _seatPivotDistance;
            var orbit = Quaternion.AngleAxis(_seatLook.x, Vector3.up);
            var position = pivot + orbit * (_overrideBasePosition - pivot);
            var rotation = orbit * _overrideBaseRotation * Quaternion.Euler(_seatLook.y, 0f, 0f);
            _override.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Moves the orbit anchor: glued to the cat sideways, but only a little of each jump goes up into the view.</summary>
        void LateUpdate()
        {
            if (_target == null)
                return;

            _lastAnchorPosition = _anchor.position;
            var targetPosition = _target.position;
            var airborne = _movement != null && _movement.IsAirborne;

            float goal;
            if (!airborne)
            {
                _groundHeight = targetPosition.y;
                goal = targetPosition.y;
            }
            else if (targetPosition.y > _groundHeight)
            {
                goal = _groundHeight + (targetPosition.y - _groundHeight) * _jumpFollow;
            }
            else
            {
                // Dropping off a table or the bar: follow on down.
                goal = targetPosition.y;
            }

            // Teleports (sitting down, standing up) snap instead of gliding.
            if (Mathf.Abs(goal - _anchorHeight) > 2f)
            {
                _anchorHeight = goal;
                _anchorHeightVelocity = 0f;
            }
            else
            {
                _anchorHeight = Mathf.SmoothDamp(_anchorHeight, goal, ref _anchorHeightVelocity, _heightSmoothTime);
            }

            _anchor.position = new Vector3(targetPosition.x, _anchorHeight, targetPosition.z);
        }
    }
}
