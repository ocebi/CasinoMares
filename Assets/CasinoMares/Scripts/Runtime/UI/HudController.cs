using System.Collections.Generic;
using System.Linq;
using CasinoMares.Audio;
using CasinoMares.CameraRig;
using CasinoMares.Core;
using CasinoMares.Networking;
using CasinoMares.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// In-game HUD: balance, players online, [E] prompt, toasts, the Tab leaderboard (GDD §7.4) and the Esc pause
    /// menu. Also owns cursor locking.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        const float ToastSeconds = 4f;
        const string WalkingHint = "WASD move · Shift run · Space jump · Click paw swipe · Q emotes · E interact · Enter chat · Tab leaderboard · Esc menu";
        const string SeatedHint = "WASD look around · E stand up · Enter chat · Esc menu";

        [SerializeField] UIDocument _document;

        VisualElement _hudRoot;
        VisualElement _pauseRoot;
        VisualElement _prompt;
        Label _promptLabel;
        VisualElement _toastStack;
        VisualElement _leaderboard;
        VisualElement _leaderboardRows;
        Label _balance;
        Label _players;
        Label _hint;
        Slider _sensitivity;
        Slider _volume;
        Toggle _follow;
        bool _paused;
        string _shownPrompt;
        bool _shownPromptEnabled;
        readonly List<(VisualElement element, float expiresAt)> _toasts = new();

        bool InGame => ConnectionManager.Instance != null && ConnectionManager.Instance.State == ConnectionState.Connected;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            _hudRoot = ui.Q("hud-root");
            _pauseRoot = ui.Q("pause-root");
            _prompt = ui.Q("prompt");
            _promptLabel = ui.Q<Label>("prompt-label");
            _toastStack = ui.Q("toast-stack");
            _leaderboard = ui.Q("leaderboard");
            _leaderboardRows = ui.Q("leaderboard-rows");
            _balance = ui.Q<Label>("balance-label");
            _players = ui.Q<Label>("players-label");
            _hint = ui.Q<Label>("hint-label");
            _sensitivity = ui.Q<Slider>("sensitivity-slider");
            _volume = ui.Q<Slider>("volume-slider");
            _follow = ui.Q<Toggle>("follow-toggle");
            UiSounds.Attach(ui);

            ui.Q<Button>("resume-button").clicked += () => SetPaused(false);
            ui.Q<Button>("leave-button").clicked += OnLeaveClicked;
            ui.Q<Button>("quit-button").clicked += Application.Quit;
            _volume.RegisterValueChangedCallback(evt => Sfx.MasterVolume = evt.newValue);
            _sensitivity.RegisterValueChangedCallback(evt =>
            {
                if (PlayerCameraRig.Instance != null)
                    PlayerCameraRig.Instance.Sensitivity = evt.newValue;
            });
            _follow.RegisterValueChangedCallback(evt =>
            {
                if (PlayerCameraRig.Instance != null)
                    PlayerCameraRig.Instance.AutoFollow = evt.newValue;
            });

            ProfileStore.Changed += OnProfileChanged;
            PlayerCat.Joined += OnPlayersChanged;
            PlayerCat.Left += OnPlayersChanged;
            Toasts.Shown += ShowToast;
            ConnectionManager.Instance.StateChanged += OnConnectionStateChanged;

            OnProfileChanged(ProfileStore.Current);
            OnConnectionStateChanged(ConnectionManager.Instance.State);
        }

        void OnDisable()
        {
            ProfileStore.Changed -= OnProfileChanged;
            PlayerCat.Joined -= OnPlayersChanged;
            PlayerCat.Left -= OnPlayersChanged;
            Toasts.Shown -= ShowToast;
            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.StateChanged -= OnConnectionStateChanged;

            if (_paused)
                GameInput.PopUIBlock();
            SetCursorLocked(false);
        }

        void Update()
        {
            if (InGame && GameInput.Pause.WasPressedThisFrame() && !GiftDialog.IsOpen && !ChatPanel.IsTyping && !ChatPanel.ClosedThisFrame)
                SetPaused(!_paused);

            SetCursorLocked(InGame && GameInput.GameplayEnabled && !GameInput.CursorFree);
            UpdatePrompt();
            UpdateHint();
            UpdateLeaderboard();
            ExpireToasts();
        }

        void UpdateHint()
        {
            var seated = SeatingController.LocalTable != null;
            if (_hint.ClassListContains("hud-hint--seated") == seated)
                return;
            _hint.EnableInClassList("hud-hint--seated", seated);
            _hint.text = seated ? SeatedHint : WalkingHint;
        }

        void OnConnectionStateChanged(ConnectionState state)
        {
            _hudRoot.EnableInClassList("hidden", state != ConnectionState.Connected);
            if (state != ConnectionState.Connected)
                SetPaused(false);
            OnPlayersChanged(null);
        }

        void OnProfileChanged(PlayerProfile profile)
        {
            _balance.text = profile.Balance.ToString("N0");
        }

        void OnPlayersChanged(PlayerCat _)
        {
            _players.text = $"{PlayerCat.All.Count}/{ConnectionManager.MaxPlayers}";
        }

        // ---------------------------------------------------------------- Prompt

        void UpdatePrompt()
        {
            var prompt = InGame && GameInput.GameplayEnabled ? PlayerInteractor.Prompt : null;
            var enabled = PlayerInteractor.PromptEnabled;
            if (prompt == _shownPrompt && enabled == _shownPromptEnabled)
                return;

            _shownPrompt = prompt;
            _shownPromptEnabled = enabled;
            _prompt.EnableInClassList("hidden", string.IsNullOrEmpty(prompt));
            _promptLabel.EnableInClassList("prompt-label--disabled", !enabled);
            _promptLabel.text = enabled ? $"<b><color=#E8BA62>[E]</color></b> {prompt}" : prompt;
        }

        // ---------------------------------------------------------------- Toasts

        void ShowToast(string message)
        {
            Sfx.Play(SoundId.Notify, 0.6f);
            var toast = new Label(message) { pickingMode = PickingMode.Ignore };
            toast.AddToClassList("toast");
            _toastStack.Add(toast);
            _toasts.Add((toast, Time.unscaledTime + ToastSeconds));
        }

        void ExpireToasts()
        {
            for (var i = _toasts.Count - 1; i >= 0; i--)
            {
                var (element, expiresAt) = _toasts[i];
                var remaining = expiresAt - Time.unscaledTime;
                element.style.opacity = Mathf.Clamp01(remaining / 0.6f);
                if (remaining > 0f)
                    continue;
                element.RemoveFromHierarchy();
                _toasts.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- Leaderboard (hold Tab)

        void UpdateLeaderboard()
        {
            var show = InGame && GameInput.KeyHeld(Key.Tab);
            _leaderboard.EnableInClassList("hidden", !show);
            if (!show)
                return;

            _leaderboardRows.Clear();
            var rank = 1;
            foreach (var cat in PlayerCat.All.Where(c => c != null && c.Object != null && c.Object.IsValid).OrderByDescending(c => c.Balance))
                _leaderboardRows.Add(LeaderboardRow(rank++, cat));
        }

        public static VisualElement LeaderboardRow(int rank, PlayerCat cat)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("leader-row");
            var rankLabel = new Label($"{rank}.") { pickingMode = PickingMode.Ignore };
            rankLabel.AddToClassList("leader-rank");
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("leader-swatch");
            swatch.style.backgroundColor = cat.Swatch;
            var name = new Label(cat.DisplayName.ToString()) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("leader-name");
            var balance = new Label(cat.Balance.ToString("N0")) { pickingMode = PickingMode.Ignore };
            balance.AddToClassList("leader-balance");
            row.Add(rankLabel);
            row.Add(swatch);
            row.Add(name);
            row.Add(balance);
            return row;
        }

        // ---------------------------------------------------------------- Pause

        void SetPaused(bool paused)
        {
            if (paused == _paused)
                return;

            _paused = paused;
            _pauseRoot.EnableInClassList("hidden", !paused);
            if (paused)
            {
                if (PlayerCameraRig.Instance != null)
                {
                    _sensitivity.SetValueWithoutNotify(PlayerCameraRig.Instance.Sensitivity);
                    _follow.SetValueWithoutNotify(PlayerCameraRig.Instance.AutoFollow);
                }

                _volume.SetValueWithoutNotify(Sfx.MasterVolume);
                GameInput.PushUIBlock();
            }
            else
            {
                GameInput.PopUIBlock();
            }
        }

        void OnLeaveClicked()
        {
            SetPaused(false);
            ConnectionManager.Instance.Leave();
        }

        static void SetCursorLocked(bool locked)
        {
            var mode = locked ? CursorLockMode.Locked : CursorLockMode.None;
            if (UnityEngine.Cursor.lockState != mode)
                UnityEngine.Cursor.lockState = mode;
            UnityEngine.Cursor.visible = !locked;
        }
    }
}
