using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Hold Q to open the emote wheel, move the mouse toward an emote, release Q (or click) to play it. The cursor
    /// stays locked: mouse movement drives a pointer on the wheel, and the cat can keep walking meanwhile.
    /// With touch controls the Emote button opens it, and on desktop the HUD's Emotes button does
    /// (<see cref="OpenForClicks"/>): tap or click an emote to play it, or anywhere else to close.
    /// </summary>
    public class EmoteWheel : MonoBehaviour
    {
        const float WheelSize = 480f;
        const float OptionRadius = 172f;
        const float PointerRange = 150f;
        const float Deadzone = 36f;
        const float PointerSpeed = 0.9f;

        [SerializeField] UIDocument _document;

        readonly List<VisualElement> _options = new();
        VisualElement _root;
        VisualElement _pointer;
        Label _title;
        Label _hint;
        bool _open;
        /// <summary>Options are picked with the pointer (touch, or the desktop Emotes button) instead of Q and the mouse.</summary>
        bool _clickMode;
        bool _lookBlocked;
        int _unblockFrame = -1;
        Vector2 _pointerOffset;
        int _selected = -1;

        public static bool IsOpen { get; private set; }

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            _root = ui.Q("emote-root");
            _pointer = ui.Q("pointer");
            _title = ui.Q<Label>("center-title");
            _hint = ui.Q<Label>("center-hint");

            var container = ui.Q("options");
            container.Clear();
            _options.Clear();
            for (var i = 0; i < CatEmotes.Wheel.Length; i++)
            {
                // Clockwise from the top.
                var angle = i * Mathf.PI * 2f / CatEmotes.Wheel.Length;
                var option = new VisualElement { pickingMode = PickingMode.Ignore };
                option.AddToClassList("emote-option");
                option.style.left = WheelSize / 2f + Mathf.Sin(angle) * OptionRadius;
                option.style.top = WheelSize / 2f - Mathf.Cos(angle) * OptionRadius;
                var label = new Label(CatEmotes.Label(CatEmotes.Wheel[i])) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("emote-option-label");
                option.Add(label);
                container.Add(option);
                _options.Add(option);
            }

            _root.RegisterCallback<PointerDownEvent>(OnPointerDown);
            Close(false);
        }

        /// <summary>The desktop HUD's Emotes button: the wheel opens for a click on an emote.</summary>
        public void OpenForClicks()
        {
            if (!_open && CanEmote())
                Open(true);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            var index = _options.IndexOf(evt.target as VisualElement);
            if (index >= 0)
                Select(index);
            Close(index >= 0);
            evt.StopPropagation();
        }

        void OnDisable()
        {
            if (_open)
                Close(false);
            SetLookBlocked(false);
        }

        void Update()
        {
            // Released a frame after closing, so the click that picked an emote doesn't also swipe a paw.
            if (_unblockFrame >= 0 && Time.frameCount >= _unblockFrame)
            {
                _unblockFrame = -1;
                SetLookBlocked(false);
            }

            if (!_open)
            {
                if (GameInput.EmotePressed() && CanEmote())
                    Open(Platform.IsTouch);
                return;
            }

            if (!CanEmote() || GameInput.PausePressed())
            {
                Close(false);
                return;
            }

            if (_clickMode)
            {
                if (GameInput.TouchPressed(GameInput.TouchButton.Emote) || GameInput.Emote.WasPressedThisFrame())
                    Close(false);
                return;
            }

            // Mouse movement nudges a pointer from the wheel's center (UI y points down).
            var delta = GameInput.ReadMouseDelta();
            _pointerOffset = Vector2.ClampMagnitude(_pointerOffset + new Vector2(delta.x, -delta.y) * PointerSpeed, PointerRange);
            _pointer.style.left = WheelSize / 2f + _pointerOffset.x;
            _pointer.style.top = WheelSize / 2f + _pointerOffset.y;
            Select(_pointerOffset.magnitude < Deadzone ? -1 : IndexForDirection(_pointerOffset));

            if (GameInput.Emote.WasReleasedThisFrame() || GameInput.Paw.WasPressedThisFrame())
                Close(true);
        }

        static bool CanEmote()
        {
            var cat = PlayerCat.Local;
            return cat != null && !cat.IsSeated && GameInput.GameplayEnabled && !GameInput.CursorFree;
        }

        static int IndexForDirection(Vector2 direction)
        {
            var count = CatEmotes.Wheel.Length;
            var angle = Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
            if (angle < 0f)
                angle += 360f;
            return Mathf.RoundToInt(angle / (360f / count)) % count;
        }

        void Select(int index, bool force = false)
        {
            if (index == _selected && !force)
                return;
            if (_selected >= 0)
                _options[_selected].RemoveFromClassList("emote-option--selected");
            _selected = index;
            if (_selected >= 0)
            {
                _options[_selected].AddToClassList("emote-option--selected");
                Sfx.Play(SoundId.Tick, 0.35f);
            }

            _title.text = _selected >= 0 ? CatEmotes.Label(CatEmotes.Wheel[_selected]) : "Emotes";
            _hint.text = _clickMode ? Platform.IsTouch ? "Tap an emote" : "Click an emote" : _selected >= 0 ? "Release Q to play" : "Move the mouse, release Q";
        }

        void Open(bool clickMode)
        {
            _open = IsOpen = true;
            _clickMode = clickMode;
            var picking = clickMode ? PickingMode.Position : PickingMode.Ignore;
            _root.pickingMode = picking;
            foreach (var option in _options)
                option.pickingMode = picking;
            _pointerOffset = Vector2.zero;
            // Forced: the hint depends on how the wheel was opened.
            Select(-1, true);
            _pointer.style.left = WheelSize / 2f;
            _pointer.style.top = WheelSize / 2f;
            _pointer.EnableInClassList("hidden", clickMode);
            _root.RemoveFromClassList("hidden");
            _unblockFrame = -1;
            SetLookBlocked(true);
        }

        void Close(bool play)
        {
            if (play && _selected >= 0)
                PlayerCat.Local?.PlayEmote(CatEmotes.Wheel[_selected]);

            _root.AddToClassList("hidden");
            if (_open)
                _unblockFrame = Time.frameCount + 1;
            _open = IsOpen = false;
            Select(-1);
        }

        void SetLookBlocked(bool blocked)
        {
            if (blocked == _lookBlocked)
                return;
            _lookBlocked = blocked;
            if (blocked)
                GameInput.PushLookBlock();
            else
                GameInput.PopLookBlock();
        }
    }
}
