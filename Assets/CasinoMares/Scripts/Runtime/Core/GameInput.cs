using UnityEngine;
using UnityEngine.InputSystem;

namespace CasinoMares.Core
{
    /// <summary>
    /// Keyboard + mouse actions, defined in code so no input asset has to be wired up.
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

        static int _uiBlockers;
        static int _cursorFreeRequests;
        static int _lookBlockers;

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

        public static Vector2 ReadMove() => GameplayEnabled ? Move.ReadValue<Vector2>() : Vector2.zero;
        public static Vector2 ReadLook() => GameplayEnabled && !LookBlocked ? Look.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>Raw mouse movement for UI that reads the mouse while the cursor is locked.</summary>
        public static Vector2 ReadMouseDelta() => Look.ReadValue<Vector2>();
        public static bool IsSprinting() => GameplayEnabled && Sprint.IsPressed();
        public static bool JumpPressed() => GameplayEnabled && Jump.WasPressedThisFrame();
        public static bool InteractPressed() => GameplayEnabled && Interact.WasPressedThisFrame();

        /// <summary>Left click while walking around (not while the cursor is out for table UI).</summary>
        public static bool PawPressed() => GameplayEnabled && !CursorFree && !LookBlocked && Paw.WasPressedThisFrame();

        public static bool EmotePressed() => GameplayEnabled && !CursorFree && Emote.WasPressedThisFrame();
    }
}
