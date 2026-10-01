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
        const string WebWalkingHint = "WASD move · Right-drag look · Shift run · Space jump · Click paw swipe · Q emotes · E interact · Enter chat · Tab leaderboard · Esc menu";
        const string SeatedHint = "WASD look around · E stand up · Enter chat · Esc menu";
        const string TouchWalkingHint = "Left thumb walks (push to the rim to run) · drag to look · tap a prompt to use it";
        const string TouchSeatedHint = "Drag to look around the table";

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
        Button _fullscreen;
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
            _fullscreen = ui.Q<Button>("fullscreen-button");
            UiSounds.Attach(ui);

            ui.Q<Button>("resume-button").clicked += () => SetPaused(false);
            ui.Q<Button>("leave-button").clicked += OnLeaveClicked;
            var quit = ui.Q<Button>("quit-button");
            quit.clicked += Application.Quit;
            quit.EnableInClassList("hidden", !Platform.CanQuit);

            // A browser only goes full screen inside a click, and Unity makes the request when the press ends, so
            // ask on the way down.
            _fullscreen.EnableInClassList("hidden", !Platform.CanFullscreen);
            ui.Q("home-screen-hint").EnableInClassList("hidden", !Platform.NeedsHomeScreenForFullscreen);
            _fullscreen.RegisterCallback<PointerDownEvent>(_ => Screen.fullScreen = !Screen.fullScreen, TrickleDown.TrickleDown);

            _hint.text = WalkingHintText;
            if (Platform.IsTouch)
            {
                _sensitivity.label = "Look sensitivity";
                // No E key: tap the prompt instead.
                _promptLabel.pickingMode = PickingMode.Position;
                _promptLabel.AddToClassList("prompt-label--touch");
                _promptLabel.RegisterCallback<PointerDownEvent>(evt =>
                {
                    GameInput.PressTouch(GameInput.TouchButton.Interact);
                    evt.StopPropagation();
                });
            }
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
            if (InGame && GameInput.PausePressed() && !GiftDialog.IsOpen && !ChatPanel.IsTyping && !ChatPanel.ClosedThisFrame)
                SetPaused(!_paused);

            // In a browser the cursor stays free: the camera follows the cat, and right-drag looks around.
            SetCursorLocked(InGame && GameInput.GameplayEnabled && !GameInput.CursorFree && !Platform.IsWeb);
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
            _hint.text = seated ? Platform.IsTouch ? TouchSeatedHint : SeatedHint : WalkingHintText;
        }

        static string WalkingHintText => Platform.IsTouch ? TouchWalkingHint : Platform.IsWeb ? WebWalkingHint : WalkingHint;

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
            _promptLabel.text = enabled && !Platform.IsTouch ? $"<b><color=#E8BA62>[E]</color></b> {prompt}" : prompt;
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
            var show = InGame && (GameInput.KeyHeld(Key.Tab) || GameInput.TouchLeaderboard);
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
                _fullscreen.text = Screen.fullScreen ? "Leave full screen" : "Full screen";
                GameInput.PushUIBlock();
            }
            else
            {
                GameInput.PopUIBlock();
                // Volume, sensitivity and camera follow are set here. A browser only keeps PlayerPrefs that are saved.
                PlayerPrefs.Save();
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
