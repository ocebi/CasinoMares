using CasinoMares.Core;
using CasinoMares.Player;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.CameraRig
{
    /// <summary>The views a seated player can cycle through at a card table (GDD §3.5).</summary>
    public enum TableView
    {
        /// <summary>The seat camera over the cat's shoulder, with the A/D look.</summary>
        Seat = 0,
        /// <summary>The table's fixed camera above the felt; no look input. (2: what players' saved choices store.)</summary>
        Overhead = 2,
    }

    /// <summary>
    /// Owns the local player's cameras (GDD §3.5): an overview camera for the menu and a third-person orbit camera
    /// that follows the local cat. Mouse look is fed straight into the orbital axes, and the orbit's distance is a
    /// setting (phones start closer, where the cat would otherwise look small).
    /// The orbit follows a smoothed anchor instead of the cat itself, so a jump barely moves the view. While the
    /// cat walks, the orbit swings behind it within about a second (turn off in the pause menu). Up near the ceiling
    /// (the cat wall) the orbit tips down to stay under it, rather than being pulled in to the cat by the ceiling's
    /// camera blocker. At a table, A/D (or a right-drag) swing the seat camera a little way around the table (to see
    /// the other players' cards) and W/S tilt it; the view stays where it's left until the next time you sit down.
    /// A card table also offers an overhead view (<see cref="TableView"/>), remembered per game. A seat
    /// camera with its own orbit (the terrace café tables) goes all the way round the table instead, starting behind
    /// your seat. A table can briefly hand the view to a focus camera (the roulette wheel during a spin) and take it
    /// back without losing that seated look.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerCameraRig : MonoBehaviour
    {
        const string SensitivityKey = "MouseSensitivity";
        const string AutoFollowKey = "CameraAutoFollow";
        const string DistanceKey = "CameraDistance";
        /// <summary>PlayerPrefs key prefix for the remembered table view, followed by the game ("TableView.Blackjack").</summary>
        const string TableViewKeyPrefix = "TableView.";
        public const float MinDistance = 1f;
        public const float MaxDistance = 3.5f;
        const int ActivePriority = 20;
        const int OverridePriority = 30;
        /// <summary>The overhead view sits above the seat camera but below a focus camera (the roulette wheel).</summary>
        const int OverheadPriority = 35;
        const int FocusPriority = 40;
        const int InactivePriority = 0;

        /// <summary>Auto-follow stops nudging the orbit once it is this close to the cat's heading.</summary>
        const float FollowSettleAngle = 1f;
        /// <summary>Anchor speed (m/s) below which the cat isn't really walking and the orbit stays put.</summary>
        const float FollowMinSpeed = 0.2f;

        [SerializeField] CinemachineCamera _menuCamera;
        [SerializeField] CinemachineCamera _followCamera;
        [SerializeField] float _defaultSensitivity = 0.12f;

        [Header("Distance")]
        [Tooltip("Metres between the follow camera and the cat, until the player picks their own in the pause menu.")]
        [SerializeField] float _defaultDistance = 2.2f;
        [Tooltip("The same on phones and tablets: closer, so the cat isn't tiny on a small screen.")]
        [SerializeField] float _touchDefaultDistance = 1.6f;

        [Header("Jumps")]
        [Tooltip("How much of a jump's height the camera follows (0 = none, 1 = all of it).")]
        [SerializeField] float _jumpFollow = 0.15f;
        [SerializeField] float _heightSmoothTime = 0.18f;

        [Header("Ceiling")]
        [Tooltip("Metres the follow camera keeps below a ceiling's camera blocker.")]
        [SerializeField] float _ceilingClearance = 0.2f;
        [Tooltip("Degrees per second the orbit tips down (or back up) for a ceiling.")]
        [SerializeField] float _ceilingTipSpeed = 120f;

        [Header("Auto follow")]
        [Tooltip("Turn rate per degree of error (1/s): the orbit closes this share of the remaining angle each second, an exponential approach.")]
        [SerializeField] float _followGain = 3f;
        [Tooltip("Degrees per second at least, so the last few degrees don't crawl.")]
        [SerializeField] float _followMinRate = 25f;
        [Tooltip("Degrees per second at most, however far off the orbit is.")]
        [SerializeField] float _followMaxRate = 100f;
        [Tooltip("Beyond this angle the cat is walking at the camera: the orbit stays put rather than spinning round.")]
        [SerializeField] float _followIgnoreAngle = 150f;
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
        [Tooltip("Degrees per second A/D turn an orbiting seat camera round its table.")]
        [SerializeField] float _seatOrbitSpeed = 90f;

        public static PlayerCameraRig Instance { get; private set; }

        CinemachineOrbitalFollow _orbital;
        Transform _target;
        Transform _anchor;
        CatMovement _movement;
        CinemachineCamera _override;
        CinemachineOrbitalFollow _overrideOrbit;
        CinemachineCamera _focus;
        CinemachineCamera _overhead;
        Vector3 _overrideBasePosition;
        Quaternion _overrideBaseRotation;
        string _tableViewKey;
        TableView _tableView;

        float _groundHeight;
        float _anchorHeight;
        float _anchorHeightVelocity;
        Vector3 _lastAnchorPosition;
        float _lastManualLook = -10f;
        /// <summary>The pitch the player chose; the orbit's own is lower under a ceiling.</summary>
        float _pitch;
        float _ceilingPitch = 90f;
        int _ceilingMask;
        Vector2 _seatLook;
        Vector2 _seatLookGoal;
        Vector2 _seatLookVelocity;

        /// <summary>True while a seat camera (or other fixed view) has taken over from the follow camera.</summary>
        public bool HasOverride => _override != null;

        /// <summary>In game: the cameras belong to the local cat (walking about or seated), not the menu.</summary>
        public bool IsFollowing => _target != null;

        /// <summary>The view in use at the table the player is seated at (<see cref="TableView.Seat"/> elsewhere).</summary>
        public TableView CurrentTableView => _tableView;

        /// <summary>Whether <see cref="CycleTableView"/> does anything here: a fixed seat camera at a card table.</summary>
        public bool HasTableViews => _override != null && _overrideOrbit == null && _tableViewKey != null && _overhead != null;

        /// <summary>The third-person orbit camera, and the seat (or other fixed) camera while one has taken over.</summary>
        public CinemachineCamera FollowCamera => _followCamera;
        public CinemachineCamera OverrideCamera => _override;

        public float Sensitivity
        {
            get => PlayerPrefs.GetFloat(SensitivityKey, _defaultSensitivity);
            set => PlayerPrefs.SetFloat(SensitivityKey, value);
        }

        /// <summary>How far the follow camera orbits from the cat, in metres.</summary>
        public float Distance
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(DistanceKey, Platform.IsTouch ? _touchDefaultDistance : _defaultDistance), MinDistance, MaxDistance);
            set
            {
                PlayerPrefs.SetFloat(DistanceKey, Mathf.Clamp(value, MinDistance, MaxDistance));
                _orbital.Radius = Distance;
            }
        }

        /// <summary>The orbit swings behind the cat while it walks, so WASD alone can steer (on by default).</summary>
        public bool AutoFollow
        {
            get => PlayerPrefs.GetInt(AutoFollowKey, 1) == 1;
            set => PlayerPrefs.SetInt(AutoFollowKey, value ? 1 : 0);
        }

        /// <summary>The PlayerPrefs name a table's views are remembered under: its game, e.g. "Blackjack" for a <c>BlackjackTable</c>.</summary>
        public static string ViewKeyFor(IGameTable table)
        {
            var name = table.GetType().Name;
            return name.EndsWith("Table") ? name.Substring(0, name.Length - "Table".Length) : name;
        }

        void Awake()
        {
            Instance = this;
            _orbital = _followCamera.GetComponent<CinemachineOrbitalFollow>();
            _orbital.Radius = Distance;
            _menuCamera.Priority = ActivePriority;
            _followCamera.Priority = InactivePriority;
            _anchor = new GameObject("CameraAnchor").transform;
            _anchor.SetParent(transform, false);
            _ceilingMask = LayerMask.GetMask("CameraBlocker");
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
            _pitch = _orbital.VerticalAxis.Value;
            _ceilingPitch = 90f;
            _followCamera.Priority = ActivePriority;
            _menuCamera.Priority = InactivePriority;
        }

        /// <summary>
        /// Blend to a fixed camera, e.g. the seat view at a table (GDD §3.5). An orbiting seat camera starts behind
        /// <paramref name="seat"/>, looking the way the seat faces. A card table also passes its
        /// <paramref name="overhead"/> camera (may be null) and a <paramref name="viewKey"/> (its game) under which the
        /// player's last <see cref="TableView"/> is remembered and restored.
        /// </summary>
        public void SetOverride(CinemachineCamera camera, Transform seat = null, CinemachineCamera overhead = null, string viewKey = null)
        {
            ClearOverride();
            _override = camera;
            if (_override == null)
                return;
            _overrideOrbit = _override.GetComponent<CinemachineOrbitalFollow>();
            if (_overrideOrbit != null)
            {
                _overrideOrbit.HorizontalAxis.Value = (seat != null ? seat : _override.transform).eulerAngles.y;
                _overrideOrbit.VerticalAxis.Value = _overrideOrbit.VerticalAxis.Center;
            }

            _override.transform.GetPositionAndRotation(out _overrideBasePosition, out _overrideBaseRotation);
            _seatLook = _seatLookGoal = _seatLookVelocity = Vector2.zero;
            _override.Priority = OverridePriority;

            // Table views only make sense for a fixed seat camera; an orbiting one places itself.
            _overhead = _overrideOrbit == null ? overhead : null;
            _tableViewKey = _overrideOrbit == null ? viewKey : null;
            var remembered = _tableViewKey != null ? (TableView)PlayerPrefs.GetInt(TableViewKeyPrefix + _tableViewKey, (int)TableView.Seat) : TableView.Seat;
            ApplyTableView(remembered, instant: true);
            if (_overrideOrbit == null)
                ApplySeatPose();
        }

        public void ClearOverride()
        {
            ClearFocus();
            if (_override != null)
            {
                // An orbiting camera places itself; a fixed one goes back to where the table put it.
                if (_overrideOrbit == null)
                    _override.transform.SetPositionAndRotation(_overrideBasePosition, _overrideBaseRotation);
                _override.Priority = InactivePriority;
            }

            if (_overhead != null)
                _overhead.Priority = InactivePriority;

            _override = null;
            _overrideOrbit = null;
            _overhead = null;
            _tableViewKey = null;
            _tableView = TableView.Seat;
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

        // ---------------------------------------------------------------- Table views

        /// <summary>
        /// Seat ↔ Overhead. Remembered for this game. Returns the view now in use; unchanged where there are no table views.
        /// </summary>
        public TableView CycleTableView()
        {
            if (!HasTableViews)
                return _tableView;
            SetTableView(_tableView == TableView.Seat ? TableView.Overhead : TableView.Seat);
            return _tableView;
        }

        /// <summary>Switch to <paramref name="view"/> (with the brain's blend) and remember it for this game.</summary>
        public void SetTableView(TableView view)
        {
            if (!HasTableViews)
                return;
            ApplyTableView(view, instant: false);
            PlayerPrefs.SetInt(TableViewKeyPrefix + _tableViewKey, (int)_tableView);
        }

        void ApplyTableView(TableView view, bool instant)
        {
            // Anything else (no overhead camera here, or the old wide view a player may still have saved) is the seat.
            if (view != TableView.Overhead || _overhead == null)
                view = TableView.Seat;
            _tableView = view;
            if (_overhead != null)
                _overhead.Priority = view == TableView.Overhead ? OverheadPriority : InactivePriority;
        }

        // ---------------------------------------------------------------- Per frame

        void Update()
        {
            if (_override != null)
            {
                if (_focus != null)
                    return;
                if (_overrideOrbit != null)
                    UpdateSeatOrbit();
                else
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
            _pitch = vertical.ClampValue(_pitch - look.y);
        }

        /// <summary>
        /// Turns the orbit toward the way the cat faces while it walks: an exponential approach (faster the further
        /// off, never slower than a floor) that settles behind the cat within about a second. Nothing happens while
        /// the cat walks at the camera, which would spin the view round, nor for a moment after a manual look.
        /// </summary>
        float AutoFollowTurn(float cameraYaw)
        {
            if (!AutoFollow || Time.time - _lastManualLook < _followDelay)
                return 0f;

            var move = GameInput.ReadMove();
            if (move.sqrMagnitude < CatMotion.SlowWalkInput * CatMotion.SlowWalkInput)
                return 0f;
            // Pushing (roughly) toward the camera: stay put, even while the cat is still turning to face that way.
            if (Mathf.Abs(Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg) > _followIgnoreAngle)
                return 0f;

            var deltaTime = Time.deltaTime;
            var velocity = (_anchor.position - _lastAnchorPosition) / Mathf.Max(deltaTime, 0.0001f);
            velocity.y = 0f;
            if (velocity.sqrMagnitude < FollowMinSpeed * FollowMinSpeed)
                return 0f;

            // The cat's own facing, not the smoothed velocity: it turns within a few ticks, so the goal is steady.
            var heading = (_movement != null ? _movement.transform : _target).eulerAngles.y;
            var delta = Mathf.DeltaAngle(cameraYaw, heading);
            var error = Mathf.Abs(delta);
            if (error < FollowSettleAngle || error > _followIgnoreAngle)
                return 0f;

            var rate = Mathf.Clamp(error * _followGain, _followMinRate, _followMaxRate);
            return Mathf.Sign(delta) * Mathf.Min(rate * deltaTime, error);
        }

        /// <summary>
        /// WASD (or the arrows), or a drag with touch controls, turns the seat camera within limits; it stays where
        /// it's left. The overhead view takes no look input.
        /// </summary>
        void UpdateSeatLook()
        {
            if (_tableView != TableView.Overhead)
            {
                var move = GameInput.ReadMove();
                var step = _seatTurnSpeed * Time.deltaTime;
                var drag = GameInput.ReadSeatDrag() * Sensitivity;
                _seatLookGoal.x = Mathf.Clamp(_seatLookGoal.x + move.x * step + drag.x, -_seatYaw, _seatYaw);
                _seatLookGoal.y = Mathf.Clamp(_seatLookGoal.y - move.y * step - drag.y, -_seatPitch, _seatPitch);
            }

            _seatLook = Vector2.SmoothDamp(_seatLook, _seatLookGoal, ref _seatLookVelocity, _seatLookSmoothTime);
            ApplySeatPose();
        }

        /// <summary>
        /// Places the seat camera: its base pose swung around a point on the
        /// table rather than turned in place, so the table stays in view and the other hands are seen from their side.
        /// </summary>
        void ApplySeatPose()
        {
            var pivot = _overrideBasePosition + _overrideBaseRotation * Vector3.forward * _seatPivotDistance;
            var orbit = Quaternion.AngleAxis(_seatLook.x, Vector3.up);
            var position = pivot + orbit * (_overrideBasePosition - pivot);
            var rotation = orbit * _overrideBaseRotation * Quaternion.Euler(_seatLook.y, 0f, 0f);
            _override.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>A/D (or a drag) take the seat camera round the table; W/S (or a drag) raise and lower it.</summary>
        void UpdateSeatOrbit()
        {
            var move = GameInput.ReadMove();
            var step = _seatOrbitSpeed * Time.deltaTime;
            var drag = GameInput.ReadSeatDrag() * Sensitivity;
            ref var horizontal = ref _overrideOrbit.HorizontalAxis;
            ref var vertical = ref _overrideOrbit.VerticalAxis;
            horizontal.Value = horizontal.ClampValue(horizontal.Value + move.x * step + drag.x);
            vertical.Value = vertical.ClampValue(vertical.Value - move.y * step * 0.5f - drag.y);
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
            UpdateCeilingPitch();
        }

        /// <summary>
        /// Lowers the orbit while the camera would otherwise rise into a ceiling's camera blocker: it orbits at
        /// <see cref="CinemachineOrbitalFollow.Radius"/> from the anchor, so its height is the radius times the sine of
        /// its pitch.
        /// </summary>
        void UpdateCeilingPitch()
        {
            ref var vertical = ref _orbital.VerticalAxis;
            var radius = Mathf.Max(_orbital.Radius, 0.1f);
            var limit = 90f;
            if (Physics.Raycast(_anchor.position, Vector3.up, out var hit, radius + 1f, _ceilingMask, QueryTriggerInteraction.Ignore))
                limit = Mathf.Asin(Mathf.Clamp((hit.distance - _ceilingClearance) / radius, -1f, 1f)) * Mathf.Rad2Deg;
            // Eased, but from no higher than it needs to start: a pitch that's already under the limit changes nothing.
            _ceilingPitch = Mathf.MoveTowards(Mathf.Min(_ceilingPitch, Mathf.Max(limit, _pitch)), limit, _ceilingTipSpeed * Time.deltaTime);
            vertical.Value = vertical.ClampValue(Mathf.Min(_pitch, _ceilingPitch));
        }
    }
}
