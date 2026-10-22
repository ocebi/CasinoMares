using CasinoMares.Core;
using CasinoMares.Networking;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// On-screen controls for phones and tablets (<see cref="Platform.IsTouch"/>). A finger on the left part of the
    /// screen grabs a joystick where it lands (a gentle push walks slowly, past the ring runs); a finger anywhere else drags the camera,
    /// also while seated. Buttons for jump, paw swipe and emotes, plus menu, chat and leaderboard pills. Everything is
    /// fed into <see cref="GameInput"/>, so gameplay code reads touch and keyboard the same way.
    ///
    /// This document sits below every other UI document: its full-screen surface only gets the touches that no button
    /// or panel above it wants. It also scales the UI up for small screens.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class TouchControls : MonoBehaviour
    {
        /// <summary>UI pixels per CSS pixel on touch screens: a phone held sideways (~390 CSS px) gets ~560 UI px.</summary>
        const float UiPerCssPixel = 1.45f;
        const float MinUiHeight = 500f;
        const float MaxUiHeight = 1080f;
#if UNITY_EDITOR
        /// <summary>The editor's Game view stands in for a phone screen with two pixels per CSS pixel.</summary>
        const float EditorPixelRatio = 2f;
#endif

        /// <summary>The joystick takes fingers that land on this share of the screen, from the left.</summary>
        const float StickZone = 0.42f;
        /// <summary>UI pixels from the stick's centre to a full push (UX §1.7).</summary>
        const float StickRadius = 96f;
        /// <summary>Share of the radius below which the stick reads as nothing; the same as <see cref="CatMotion.SlowWalkInput"/>.</summary>
        const float StickDeadzone = 0.15f;
        /// <summary>Share of the radius (the ring) from which the stick runs; the same as <see cref="CatMotion.FullWalkInput"/>.</summary>
        const float StickRunShare = 0.9f;
        const string StickRunClass = "touch-stick--run";
        const string KnobRunClass = "touch-stick-knob--run";

        /// <summary>Converts a drag in UI pixels into mouse counts, so the look sensitivity setting applies to both.</summary>
        const float LookPerUiPixel = 1.6f;

        [SerializeField] UIDocument _document;

        /// <summary>The Chat pill, for <see cref="ChatPanel"/>; null without touch controls.</summary>
        public static Button ChatButton { get; private set; }

        VisualElement _root;
        VisualElement _surface;
        VisualElement _stick;
        VisualElement _knob;
        VisualElement _walkButtons;
        Button _leadersButton;

        int _stickPointer = -1;
        Vector2 _stickCenter;
        int _lookPointer = -1;
        Vector2 _lookLast;

        PanelSettings _panelSettings;
        Vector2Int _scaledFor;
#if UNITY_EDITOR
        (PanelScaleMode mode, float scale) _originalScale;
#endif

        // Start, not OnEnable: the early execution order would run before the UIDocument has built its tree.
        void Start()
        {
            var ui = _document.rootVisualElement;
            _root = ui.Q("touch-root");
            if (!Platform.IsTouch)
            {
                _root.AddToClassList("hidden");
                foreach (var document in FindObjectsByType<UIDocument>())
                {
                    if (document.panelSettings == _document.panelSettings && document.rootVisualElement != null)
                        UseDesktopLabels(document.rootVisualElement);
                }

                enabled = false;
                return;
            }

            _surface = _root;
            _stick = ui.Q("stick");
            _knob = ui.Q("stick-knob");
            _walkButtons = ui.Q("walk-buttons");
            _leadersButton = ui.Q<Button>("leaders-button");

            _surface.RegisterCallback<PointerDownEvent>(OnSurfaceDown);
            _surface.RegisterCallback<PointerMoveEvent>(OnSurfaceMove);
            _surface.RegisterCallback<PointerUpEvent>(OnSurfaceUp);
            _surface.RegisterCallback<PointerCancelEvent>(OnSurfaceCancel);
            _surface.RegisterCallback<PointerCaptureOutEvent>(evt => Release(evt.pointerId));

            HoldButton(ui.Q("jump-button"), GameInput.TouchButton.Jump);
            HoldButton(ui.Q("paw-button"), GameInput.TouchButton.Paw);
            HoldButton(ui.Q("emote-button"), GameInput.TouchButton.Emote);
            ui.Q<Button>("menu-button").clicked += () => GameInput.PressTouch(GameInput.TouchButton.Pause);
            ChatButton = ui.Q<Button>("chat-button");
            ChatButton.clicked += () => GameInput.PressTouch(GameInput.TouchButton.Chat);
            _leadersButton.clicked += () =>
            {
                GameInput.TouchLeaderboard = !GameInput.TouchLeaderboard;
                _leadersButton.EnableInClassList("touch-pill--active", GameInput.TouchLeaderboard);
            };
            UiSounds.Attach(ui);

            // Every screen sharing this panel gets the .touch layout tweaks and loses its keyboard hints.
            _panelSettings = _document.panelSettings;
            foreach (var document in FindObjectsByType<UIDocument>())
            {
                if (document.panelSettings != _panelSettings || document.rootVisualElement == null)
                    continue;
                document.rootVisualElement.AddToClassList("touch");
                KeyHints.Strip(document.rootVisualElement);
            }

#if UNITY_EDITOR
            _originalScale = (_panelSettings.scaleMode, _panelSettings.scale);
#endif
            _scaledFor = default;
            UpdateScale();
        }

        /// <summary>
        /// Desktop counterpart of the .touch tweaks: the root gets .desktop, and buttons lose their touch glyphs (hidden
        /// on desktop anyway). UI Toolkit sizes a button to its words only while it has no child elements, so with the
        /// glyph inside, "Ready (Space)" got the bare min-width and was cut off. Runs after the HUDs' OnEnable, so the
        /// glyphs they hold on to (the View and Check/Call icons) stay valid, just out of the tree.
        /// </summary>
        static void UseDesktopLabels(VisualElement root)
        {
            root.AddToClassList("desktop");
            foreach (var icon in root.Query(className: "btn-icon").ToList())
            {
                if (icon.parent is Button)
                    icon.RemoveFromHierarchy();
            }
        }

        void OnDestroy()
        {
            if (!Platform.IsTouch || _root == null)
                return;

            ChatButton = null;
            ReleaseStick();
            ReleaseLook();
            GameInput.TouchLeaderboard = false;
#if UNITY_EDITOR
            // The panel settings are an asset; don't leave the phone scale behind after play mode.
            if (_panelSettings != null)
            {
                _panelSettings.scaleMode = _originalScale.mode;
                _panelSettings.scale = _originalScale.scale;
            }
#endif
        }

        void Update()
        {
            // Button presses from the last UI events count from this frame on (this runs before gameplay scripts).
            GameInput.BeginTouchFrame();
            UpdateScale();

            var inGame = ConnectionManager.Instance != null && ConnectionManager.Instance.State == ConnectionState.Connected;
            _root.EnableInClassList("hidden", !inGame);
            if (!inGame)
            {
                ReleaseStick();
                ReleaseLook();
                return;
            }

            // Seated: the table UI has the buttons, and every finger looks around.
            var walking = SeatingController.LocalTable == null && PlayerCat.Local != null;
            _walkButtons.EnableInClassList("hidden", !walking);
            _stick.EnableInClassList("hidden", !walking);
            if (!walking || !GameInput.GameplayEnabled)
                ReleaseStick();
            if (!GameInput.GameplayEnabled)
                ReleaseLook();
        }

        /// <summary>Scales the whole UI to the screen's physical size; redone when the screen turns or resizes.</summary>
        void UpdateScale()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size == _scaledFor || size.y <= 0)
                return;
            _scaledFor = size;

            var cssHeight = Platform.CssHeight;
#if UNITY_EDITOR
            cssHeight = Screen.height / EditorPixelRatio;
#endif
            var uiHeight = Mathf.Clamp(cssHeight * UiPerCssPixel, MinUiHeight, MaxUiHeight);
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.scale = Screen.height / uiHeight;
        }

        // ---------------------------------------------------------------- Buttons

        /// <summary>Acts on touch-down (a jump shouldn't wait for the finger to lift) and keeps the finger off the surface.</summary>
        static void HoldButton(VisualElement button, GameInput.TouchButton action)
        {
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                GameInput.PressTouch(action);
                button.AddToClassList("touch-button--pressed");
                button.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                button.RemoveFromClassList("touch-button--pressed");
                button.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });
            button.RegisterCallback<PointerCancelEvent>(_ => button.RemoveFromClassList("touch-button--pressed"));
            button.RegisterCallback<PointerCaptureOutEvent>(_ => button.RemoveFromClassList("touch-button--pressed"));
        }

        // ---------------------------------------------------------------- Stick and look surface

        void OnSurfaceDown(PointerDownEvent evt)
        {
            if (evt.target != _surface || !GameInput.GameplayEnabled)
                return;

            var position = (Vector2)evt.position;
            var walking = !_stick.ClassListContains("hidden");
            if (walking && _stickPointer < 0 && position.x < _surface.layout.width * StickZone)
            {
                _stickPointer = evt.pointerId;
                _stickCenter = position;
                _stick.style.left = position.x;
                _stick.style.top = position.y;
                _stick.style.bottom = StyleKeyword.Auto;
                _stick.AddToClassList("touch-stick--active");
                MoveStick(position);
            }
            else if (_lookPointer < 0)
            {
                _lookPointer = evt.pointerId;
                _lookLast = position;
            }
            else
            {
                return;
            }

            _surface.CapturePointer(evt.pointerId);
        }

        void OnSurfaceMove(PointerMoveEvent evt)
        {
            var position = (Vector2)evt.position;
            if (evt.pointerId == _stickPointer)
            {
                MoveStick(position);
            }
            else if (evt.pointerId == _lookPointer)
            {
                // UI y points down, mouse y points up.
                var delta = position - _lookLast;
                _lookLast = position;
                GameInput.AddTouchLook(new Vector2(delta.x, -delta.y) * LookPerUiPixel);
            }
        }

        void OnSurfaceUp(PointerUpEvent evt) => Release(evt.pointerId);

        void OnSurfaceCancel(PointerCancelEvent evt) => Release(evt.pointerId);

        void Release(int pointerId)
        {
            if (pointerId == _stickPointer)
                ReleaseStick();
            else if (pointerId == _lookPointer)
                ReleaseLook();
        }

        void MoveStick(Vector2 position)
        {
            var offset = Vector2.ClampMagnitude(position - _stickCenter, StickRadius);
            _knob.style.translate = new Translate(offset.x, offset.y);

            var share = offset.magnitude / StickRadius;
            var move = share < StickDeadzone ? Vector2.zero : new Vector2(offset.x, -offset.y) / StickRadius;
            var run = share >= StickRunShare;
            _knob.EnableInClassList(KnobRunClass, run);
            _stick.EnableInClassList(StickRunClass, run);
            GameInput.SetTouchMove(move, run);
        }

        void ReleaseStick()
        {
            if (_stickPointer < 0)
                return;

            if (_surface.HasPointerCapture(_stickPointer))
                _surface.ReleasePointer(_stickPointer);
            _stickPointer = -1;
            _stick.style.left = StyleKeyword.Null;
            _stick.style.top = StyleKeyword.Null;
            _stick.style.bottom = StyleKeyword.Null;
            _stick.RemoveFromClassList("touch-stick--active");
            _knob.style.translate = new Translate(0, 0);
            _knob.RemoveFromClassList(KnobRunClass);
            _stick.RemoveFromClassList(StickRunClass);
            GameInput.SetTouchMove(Vector2.zero, false);
        }

        void ReleaseLook()
        {
            if (_lookPointer < 0)
                return;

            if (_surface.HasPointerCapture(_lookPointer))
                _surface.ReleasePointer(_lookPointer);
            _lookPointer = -1;
        }
    }
}
