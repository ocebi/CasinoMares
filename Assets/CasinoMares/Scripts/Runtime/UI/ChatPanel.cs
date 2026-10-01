using CasinoMares.Audio;
using CasinoMares.Core;
using CasinoMares.Networking;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Text chat (GDD §8): Enter opens the input, Enter sends, Esc cancels. Old lines fade out. With touch controls the
    /// Chat pill opens and closes it, there's a Send button, and the chat sits at the top, clear of the on-screen
    /// keyboard.
    /// </summary>
    public class ChatPanel : MonoBehaviour
    {
        const float LineVisibleSeconds = 12f;

        [SerializeField] UIDocument _document;

        public static bool IsTyping { get; private set; }

        /// <summary>True in the frame the input closed, so the same Esc doesn't also open the pause menu.</summary>
        public static bool ClosedThisFrame => _closedFrame == Time.frameCount;

        static int _closedFrame = -1;

        VisualElement _root;
        ScrollView _log;
        VisualElement _inputRow;
        TextField _input;
        float _lastMessageAt = -100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsTyping = false;
            _closedFrame = -1;
        }

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            _root = ui.Q("chat-root");
            _log = ui.Q<ScrollView>("chat-log");
            _inputRow = ui.Q("chat-input-row");
            _input = ui.Q<TextField>("chat-input");
            _input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);

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
            if (IsTyping)
                CloseInput();
        }

        void Update()
        {
            var inGame = ConnectionManager.Instance != null && ConnectionManager.Instance.State == ConnectionState.Connected;
            _root.EnableInClassList("hidden", !inGame);
            if (!inGame)
                return;

            // The Enter that sends a message must not reopen the input in the same frame.
            if (!IsTyping && !ClosedThisFrame && GameInput.GameplayEnabled && GameInput.ChatPressed())
                OpenInput();
            else if (IsTyping && GameInput.TouchPressed(GameInput.TouchButton.Chat))
                CloseInput();

            // Keep the log readable while typing, fade it away when idle.
            var idle = Time.unscaledTime - _lastMessageAt;
            _log.style.opacity = IsTyping ? 1f : Mathf.Clamp01((LineVisibleSeconds - idle) / 2f);
        }

        void OpenInput()
        {
            IsTyping = true;
            GameInput.PushUIBlock();
            _inputRow.RemoveFromClassList("hidden");
            _input.value = "";
            _input.schedule.Execute(() => _input.Focus());
        }

        void CloseInput()
        {
            IsTyping = false;
            _closedFrame = Time.frameCount;
            GameInput.PopUIBlock();
            _inputRow.AddToClassList("hidden");
            _input.Blur();
        }

        void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
            {
                Send();
                evt.StopImmediatePropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                CloseInput();
                evt.StopImmediatePropagation();
            }
        }

        void Send()
        {
            var text = _input.value;
            CloseInput();
            if (PlayerCat.Local != null && !string.IsNullOrWhiteSpace(text))
                PlayerCat.Local.SendChat(text);
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
            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0f, float.MaxValue));
            _lastMessageAt = Time.unscaledTime;
        }

        static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");
    }
}
