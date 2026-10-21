using CasinoMares.Audio;
using CasinoMares.Core;
using CasinoMares.Networking;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Text chat (GDD §8): Enter or the HUD's Chat button opens the input, Enter sends, Esc cancels. Old lines fade
    /// out. With touch controls the Chat pill opens and closes it, there's a Send button, and the chat sits at the top,
    /// clear of the on-screen keyboard. In a browser the typing goes through a page text box (<see cref="WebTextInput"/>),
    /// which the Chat pill's tap focuses so a phone's keyboard comes up with it. The log keeps the newest line in view
    /// unless the player has scrolled up to read back; their own message, or closing the input, brings it down again.
    /// </summary>
    public class ChatPanel : MonoBehaviour
    {
        const float LineVisibleSeconds = 12f;
        /// <summary>Within this many pixels of the bottom counts as at the bottom.</summary>
        const float BottomSlack = 2f;

        [SerializeField] UIDocument _document;

        public static bool IsTyping { get; private set; }

        /// <summary>True in the frame the input closed, so the same Esc doesn't also open the pause menu.</summary>
        public static bool ClosedThisFrame => _closedFrame == Time.frameCount;

        static int _closedFrame = -1;
        static bool _togglePending;

        VisualElement _root;
        ScrollView _log;
        VisualElement _inputRow;
        TextField _input;
        float _lastMessageAt = -100f;
        int _openedFrame = -1;
        /// <summary>The Enter that opened the input is still down: its key events reach the field too, and mustn't send.</summary>
        bool _openingEnterHeld;
        /// <summary>The log follows new lines; false once the player scrolls up, until they're back at the bottom.</summary>
        bool _stickToBottom = true;
        System.Action _scrollToBottom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsTyping = false;
            _closedFrame = -1;
            _togglePending = false;
        }

        /// <summary>The desktop HUD's Chat button: opens the input, or closes it while typing.</summary>
        public static void Toggle() => _togglePending = true;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            _root = ui.Q("chat-root");
            _log = ui.Q<ScrollView>("chat-log");
            _scrollToBottom ??= ScrollToBottom;
            // A new line only has a height once layout has run, and the scroller's range follows after that: scroll on
            // the pass after the content's size changes, not straight after adding it (that stopped a line short).
            _log.contentContainer.RegisterCallback<GeometryChangedEvent>(OnLogResized);
            _log.verticalScroller.valueChanged += OnLogScrolled;
            _inputRow = ui.Q("chat-input-row");
            _input = ui.Q<TextField>("chat-input");
            _input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            WebTextInput.Attach("chat", _input, Send, () => { if (IsTyping) CloseInput(); },
                () => !IsTyping && GameInput.GameplayEnabled ? TouchControls.ChatButton : null);

            var send = ui.Q<Button>("chat-send");
            send.clicked += Send;
            send.EnableInClassList("hidden", !Platform.IsTouch);
            ui.Q(className: "chat").EnableInClassList("chat--touch", Platform.IsTouch);

            foreach (var message in ChatLog.Messages)
                AddLine(message);
            ChatLog.Added += OnMessageAdded;
        }

        void OnDisable()
        {
            ChatLog.Added -= OnMessageAdded;
            _log.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnLogResized);
            _log.verticalScroller.valueChanged -= OnLogScrolled;
            if (IsTyping)
                CloseInput();
        }

        void Update()
        {
            var inGame = ConnectionManager.Instance != null && ConnectionManager.Instance.State == ConnectionState.Connected;
            _root.EnableInClassList("hidden", !inGame);
            if (!inGame)
                return;

            if (_openingEnterHeld && !EnterHeld())
                _openingEnterHeld = false;

            // The Enter that sends a message must not reopen the input in the same frame.
            var toggle = _togglePending;
            _togglePending = false;
            if (!IsTyping && !ClosedThisFrame && GameInput.GameplayEnabled && (GameInput.ChatPressed() || toggle))
                OpenInput();
            else if (IsTyping && (GameInput.TouchPressed(GameInput.TouchButton.Chat) || toggle))
                CloseInput();

            // Keep the log readable while typing, fade it away when idle.
            var idle = Time.unscaledTime - _lastMessageAt;
            _log.style.opacity = IsTyping ? 1f : Mathf.Clamp01((LineVisibleSeconds - idle) / 2f);
        }

        void OpenInput()
        {
            IsTyping = true;
            _openedFrame = Time.frameCount;
            _openingEnterHeld = EnterHeld();
            GameInput.PushUIBlock();
            _inputRow.RemoveFromClassList("hidden");
            _input.value = "";
            _input.schedule.Execute(() => WebTextInput.Focus(_input));
        }

        void CloseInput()
        {
            IsTyping = false;
            _closedFrame = Time.frameCount;
            GameInput.PopUIBlock();
            _inputRow.AddToClassList("hidden");
            WebTextInput.Blur(_input);
            // The log fades and can't be read back once the input is closed: show the newest lines again.
            FollowNewest();
        }

        void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
            {
                if (!_openingEnterHeld && Time.frameCount > _openedFrame + 1)
                    Send();
                evt.StopImmediatePropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CloseInput();
                evt.StopImmediatePropagation();
            }
        }

        static bool EnterHeld()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed);
        }

        void Send()
        {
            WebTextInput.Pull(_input);
            var text = _input.value;
            CloseInput();
            if (PlayerCat.Local != null && !string.IsNullOrWhiteSpace(text))
            {
                FollowNewest();
                PlayerCat.Local.Social.SendChat(text);
            }
        }

        void FollowNewest()
        {
            _stickToBottom = true;
            _log.schedule.Execute(_scrollToBottom);
        }

        void OnLogResized(GeometryChangedEvent evt)
        {
            if (_stickToBottom)
                _log.schedule.Execute(_scrollToBottom);
        }

        /// <summary>The player scrolled (or the log was scrolled for them): follow new lines only from the bottom.</summary>
        void OnLogScrolled(float value)
        {
            var scroller = _log.verticalScroller;
            _stickToBottom = value >= scroller.highValue - BottomSlack;
        }

        void ScrollToBottom()
        {
            var bottom = Mathf.Max(0f, _log.contentContainer.layout.height - _log.contentViewport.layout.height);
            _log.scrollOffset = new Vector2(0f, bottom);
            _stickToBottom = true;
        }

        void OnMessageAdded(ChatLog.Message message)
        {
            AddLine(message);
            Sfx.Play(SoundId.Tick, 0.35f);
        }

        void AddLine(ChatLog.Message message)
        {
            var line = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
            line.AddToClassList("chat-line");
            if (message.IsSystem)
            {
                line.AddToClassList("chat-line--system");
                line.text = Escape(message.Text);
            }
            else
            {
                var color = ColorUtility.ToHtmlStringRGB(message.SenderColor);
                line.text = $"<b><color=#{color}>{Escape(message.Sender)}</color></b>: {Escape(message.Text)}";
            }

            _log.Add(line);
            while (_log.contentContainer.childCount > ChatLog.MaxMessages)
                _log.contentContainer.RemoveAt(0);
            _lastMessageAt = Time.unscaledTime;
        }

        static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");
    }
}
