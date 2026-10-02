using UnityEngine;
using UnityEngine.InputSystem;

namespace CasinoMares.Core
{
    /// <summary>
    /// Keyboard + mouse actions, defined in code so no input asset has to be wired up, plus the on-screen touch
    /// controls (<see cref="UI.TouchControls"/>) folded into the same reads.
    /// Gameplay reads are gated by <see cref="GameplayEnabled"/>, which UI turns off while it has focus.
    /// </summary>
    public static class GameInput
    {
        public static InputAction Move { get; private set; }
        public static InputAction Look { get; private set; }
        public static InputAction Sprint { get; private set; }
        public static InputAction Jump { get; private set; }
        public static InputAction Interact { get; private set; }
        public static InputAction Pause { get; private set; }
        public static InputAction Chat { get; private set; }
        public static InputAction Paw { get; private set; }
        public static InputAction Emote { get; private set; }

        /// <summary>On-screen buttons of the touch controls.</summary>
        public enum TouchButton
        {
            Jump,
            Interact,
            Paw,
            Emote,
            Chat,
            Pause,
        }

        static int _uiBlockers;
        static int _cursorFreeRequests;
        static int _lookBlockers;

        static readonly bool[] TouchPending = new bool[6];
        static readonly int[] TouchPressedFrame = { -1, -1, -1, -1, -1, -1 };
        static Vector2 _touchMove;
        static bool _touchSprint;
        static Vector2 _touchLookPending;
        static Vector2 _touchLook;

        /// <summary>True when no UI is capturing the keyboard/mouse.</summary>
        public static bool GameplayEnabled => _uiBlockers == 0;

        /// <summary>
        /// True while something needs a visible mouse cursor without blocking the keyboard, e.g. while seated at
        /// a table (click the bet buttons, but the game hotkeys and WASD look still work).
        /// </summary>
        public static bool CursorFree => _cursorFreeRequests > 0;

        public static void PushUIBlock() => _uiBlockers++;
        public static void PopUIBlock() => _uiBlockers = Mathf.Max(0, _uiBlockers - 1);

        public static void PushCursorFree() => _cursorFreeRequests++;
        public static void PopCursorFree() => _cursorFreeRequests = Mathf.Max(0, _cursorFreeRequests - 1);

        /// <summary>
        /// Something (the emote wheel) is using the mouse: camera look and paw clicks pause, but the cursor stays
        /// locked and the cat can still walk.
        /// </summary>
        public static bool LookBlocked => _lookBlockers > 0;

        public static void PushLookBlock() => _lookBlockers++;
        public static void PopLookBlock() => _lookBlockers = Mathf.Max(0, _lookBlockers - 1);

        /// <summary>The leaderboard pill of the touch controls (there's no Tab key to hold).</summary>
        public static bool TouchLeaderboard { get; set; }

        /// <summary>A key press for UI shortcuts, ignored while a text field or dialog has the keyboard.</summary>
        public static bool KeyPressed(Key key) => GameplayEnabled && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;

        public static bool KeyHeld(Key key) => GameplayEnabled && Keyboard.current != null && Keyboard.current[key].isPressed;

        /// <summary>
        /// With domain reload disabled, statics survive between play sessions, but the Input System disables every
        /// action when play mode exits. Throw the old actions away so <see cref="Initialize"/> builds fresh ones.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _uiBlockers = 0;
            _cursorFreeRequests = 0;
            _lookBlockers = 0;
            foreach (var action in new[] { Move, Look, Sprint, Jump, Interact, Pause, Chat, Paw, Emote })
                action?.Dispose();
            Move = Look = Sprint = Jump = Interact = Pause = Chat = Paw = Emote = null;

            for (var i = 0; i < TouchPending.Length; i++)
            {
                TouchPending[i] = false;
                TouchPressedFrame[i] = -1;
            }

            _touchMove = _touchLookPending = _touchLook = Vector2.zero;
            _touchSprint = false;
            TouchLeaderboard = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            Move = new InputAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            Look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            Sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            Jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            Interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            Pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            Chat = new InputAction("Chat", InputActionType.Button, "<Keyboard>/enter");
            Chat.AddBinding("<Keyboard>/numpadEnter");
            Paw = new InputAction("Paw", InputActionType.Button, "<Mouse>/leftButton");
            Emote = new InputAction("Emote", InputActionType.Button, "<Keyboard>/q");

            foreach (var action in new[] { Move, Look, Sprint, Jump, Interact, Pause, Chat, Paw, Emote })
                action.Enable();
        }

        public static Vector2 ReadMove() => GameplayEnabled ? Vector2.ClampMagnitude(Move.ReadValue<Vector2>() + _touchMove, 1f) : Vector2.zero;

        /// <summary>
        /// Camera look in mouse counts. With touch controls only the drag counts: a browser may also turn touches into
        /// mouse movement. In a desktop browser the cursor isn't locked, so the mouse only looks while the right button
        /// is held.
        /// </summary>
        public static Vector2 ReadLook()
        {
            if (!GameplayEnabled || LookBlocked)
                return Vector2.zero;
            if (Platform.IsTouch)
                return _touchLook;
            if (Platform.IsWeb && (Mouse.current == null || !Mouse.current.rightButton.isPressed))
                return Vector2.zero;
            return Look.ReadValue<Vector2>();
        }

        /// <summary>Raw mouse movement for UI that reads the mouse while the cursor is locked.</summary>
        public static Vector2 ReadMouseDelta() => Look.ReadValue<Vector2>();
        public static bool IsSprinting() => GameplayEnabled && (Sprint.IsPressed() || _touchSprint);
        public static bool JumpPressed() => GameplayEnabled && (Jump.WasPressedThisFrame() || TouchPressed(TouchButton.Jump));
        public static bool InteractPressed() => GameplayEnabled && (Interact.WasPressedThisFrame() || TouchPressed(TouchButton.Interact));

        /// <summary>Left click while walking around (not while the cursor is out for table UI), or the Paw button.</summary>
        public static bool PawPressed()
        {
            if (!GameplayEnabled || CursorFree || LookBlocked)
                return false;
            return Platform.IsTouch ? TouchPressed(TouchButton.Paw) : Paw.WasPressedThisFrame();
        }

        public static bool EmotePressed() => GameplayEnabled && !CursorFree && (Emote.WasPressedThisFrame() || TouchPressed(TouchButton.Emote));

        /// <summary>Enter or the Chat pill. Not gated: the open chat input uses it too.</summary>
        public static bool ChatPressed() => Chat.WasPressedThisFrame() || TouchPressed(TouchButton.Chat);

        /// <summary>Esc or the Menu pill. Not gated: dialogs and the pause menu close with it.</summary>
        public static bool PausePressed() => Pause.WasPressedThisFrame() || TouchPressed(TouchButton.Pause);

        // ---------------------------------------------------------------- Touch controls

        /// <summary>The joystick: direction and length up to 1 (y forward), and whether it's pushed to the rim.</summary>
        public static void SetTouchMove(Vector2 move, bool sprint)
        {
            _touchMove = move;
            _touchSprint = sprint;
        }

        /// <summary>A drag on the look surface, already converted to mouse counts.</summary>
        public static void AddTouchLook(Vector2 delta) => _touchLookPending += delta;

        /// <summary>An on-screen button went down; it reads as pressed for one frame from the next <see cref="BeginTouchFrame"/>.</summary>
        public static void PressTouch(TouchButton button) => TouchPending[(int)button] = true;

        /// <summary>
        /// Publishes what the touch controls gathered since the last frame. Called by TouchControls before gameplay
        /// scripts update, so a press reads like a key's wasPressedThisFrame wherever UI events happen to run.
        /// </summary>
        public static void BeginTouchFrame()
        {
            for (var i = 0; i < TouchPending.Length; i++)
            {
                if (!TouchPending[i])
                    continue;
                TouchPending[i] = false;
                TouchPressedFrame[i] = Time.frameCount;
            }

            _touchLook = _touchLookPending;
            _touchLookPending = Vector2.zero;
        }

        /// <summary>An on-screen button pressed this frame, regardless of UI focus.</summary>
        public static bool TouchPressed(TouchButton button) => TouchPressedFrame[(int)button] == Time.frameCount;

        /// <summary>
        /// Look input while seated, where the cursor is out for the table UI: a drag on the touch look surface, or the
        /// mouse moved with the right button held. In mouse counts.
        /// </summary>
        public static Vector2 ReadSeatDrag()
        {
            if (!GameplayEnabled)
                return Vector2.zero;
            if (Platform.IsTouch)
                return _touchLook;
            return Mouse.current != null && Mouse.current.rightButton.isPressed ? Look.ReadValue<Vector2>() : Vector2.zero;
        }
    }
}
