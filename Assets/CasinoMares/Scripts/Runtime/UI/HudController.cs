using System.Collections.Generic;
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
    /// In-game HUD: balance (and chips just earned), players online, [E] prompt, toasts, the Tab leaderboard (GDD §7.4)
    /// and the Esc pause menu. Also owns cursor locking.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        const float ToastSeconds = 4f;
        const float GainSeconds = 1.4f;
        const float BigGainSeconds = 2.6f;
        /// <summary>Glasses the HUD shows as pips; five or more all look the same.</summary>
        const int MaxDrinkPips = 5;
        const string WalkingHint = "WASD move · Shift run · Space jump · Click paw swipe · Q emotes · E interact · Enter chat · Tab leaderboard · Esc menu";
        const string WebWalkingHint = "WASD move · Right-drag look · Shift run · Space jump · Click paw swipe · Q emotes · E interact · Enter chat · Tab leaderboard · Esc menu";
        const string SeatedHint = "WASD or right-drag look around · E stand up · Enter chat · Esc menu";
        const string TouchWalkingHint = "Left thumb walks: push past the ring to run · drag to look · tap a prompt to use it";
        const string TouchSeatedHint = "Drag to look around the table";

        [SerializeField] UIDocument _document;

        VisualElement _hudRoot;
        VisualElement _pauseRoot;
        VisualElement _prompt;
        Label _promptLabel;
        VisualElement _toastStack;
        VisualElement _leaderboard;
        LeaderboardPanel _leaderboardPanel;
        Label _balance;
        Label _gain;
        Label _players;
        Label _hint;
        Slider _sensitivity;
        Slider _distance;
        Slider _volume;
        Toggle _follow;
        Toggle _tipsy;
        VisualElement _drinkRow;
        VisualElement _drinkPips;
        Label _drinkLabel;
        int _shownDrinks = -1;
        Toggle _fpsCap;
        Button _fullscreen;
        Button _emotesButton;
        bool _paused;
        bool _overPills;
        string _shownPrompt;
        bool _shownPromptEnabled;
        int _gainAmount;
        float _gainShownAt;
        bool _gainBig;
        bool _gainShowing;
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
            _leaderboardPanel = new LeaderboardPanel(_leaderboard);
            _balance = ui.Q<Label>("balance-label");
            _gain = ui.Q<Label>("balance-gain");
            _players = ui.Q<Label>("players-label");
            _hint = ui.Q<Label>("hint-label");
            _sensitivity = ui.Q<Slider>("sensitivity-slider");
            _distance = ui.Q<Slider>("distance-slider");
            _distance.lowValue = PlayerCameraRig.MinDistance;
            _distance.highValue = PlayerCameraRig.MaxDistance;
            _volume = ui.Q<Slider>("volume-slider");
            _follow = ui.Q<Toggle>("follow-toggle");
            _tipsy = ui.Q<Toggle>("tipsy-toggle");
            _drinkRow = ui.Q("drink-row");
            _drinkPips = ui.Q("drink-pips");
            _drinkLabel = ui.Q<Label>("drink-label");
            // Frame rate: Auto, or held at 30 (FrameRateCap). Only phones and tablets get hot enough to want it.
            _fpsCap = ui.Q<Toggle>("fps-toggle");
            _fpsCap.EnableInClassList("hidden", !Platform.IsTouch);
            _fpsCap.RegisterValueChangedCallback(evt => FrameRateCap.Preference = evt.newValue);
            _fullscreen = ui.Q<Button>("fullscreen-button");
            UiSounds.Attach(ui);

            // Desktop pills: Settings opens the pause menu, Chat opens and closes the chat, Emotes the wheel (pick with
            // the pointer). A click on them mustn't also swipe a paw, so the paw stays down while the pointer is over one.
            ui.Q("desktop-pills").EnableInClassList("hidden", Platform.IsTouch);
            _emotesButton = ui.Q<Button>("emotes-button");
            ui.Q<Button>("settings-button").clicked += () => SetPaused(!_paused);
            ui.Q<Button>("chat-button").clicked += ChatPanel.Toggle;
            _emotesButton.clicked += () => FindFirstObjectByType<EmoteWheel>()?.OpenForClicks();
            ui.Q("desktop-pills").Query<Button>().ForEach(button =>
            {
                button.RegisterCallback<PointerEnterEvent>(_ => SetOverPills(true));
                button.RegisterCallback<PointerLeaveEvent>(_ => SetOverPills(false));
            });

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
            _distance.RegisterValueChangedCallback(evt =>
            {
                if (PlayerCameraRig.Instance != null)
                    PlayerCameraRig.Instance.Distance = evt.newValue;
            });
            _follow.RegisterValueChangedCallback(evt =>
            {
                if (PlayerCameraRig.Instance != null)
                    PlayerCameraRig.Instance.AutoFollow = evt.newValue;
            });
            _tipsy.RegisterValueChangedCallback(evt => TipsyEffect.VisualsEnabled = evt.newValue);

            ProfileStore.Changed += OnProfileChanged;
            PlayerCat.Joined += OnPlayersChanged;
            PlayerCat.Left += OnPlayersChanged;
            Toasts.Shown += ShowToast;
            ChipRewards.Earned += OnChipsEarned;
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
            ChipRewards.Earned -= OnChipsEarned;
            if (ConnectionManager.Instance != null)
                ConnectionManager.Instance.StateChanged -= OnConnectionStateChanged;

            if (_paused)
                GameInput.PopUIBlock();
            SetOverPills(false);
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
            UpdateGain();
            UpdateDrinks();
        }

        // ---------------------------------------------------------------- Wine (GDD §4.3, UX §1.1)

        /// <summary>
        /// One pip per glass still in effect (up to five) and "Tipsy" from the third, beside the balance; rebuilt only
        /// when the count changes. The toasts for the 2nd, 3rd and 5th glass and for sobering up fire here too.
        /// </summary>
        void UpdateDrinks()
        {
            var cat = PlayerCat.Local;
            var inGame = cat != null && cat.Object != null && cat.Object.IsValid;
            var drinks = inGame ? cat.DrinkCount : 0;
            if (drinks == _shownDrinks)
                return;

            var previous = _shownDrinks;
            _shownDrinks = drinks;
            _drinkPips.Clear();
            for (var i = 0; i < Mathf.Min(drinks, MaxDrinkPips); i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList("drink-pip");
                _drinkPips.Add(pip);
            }

            _drinkRow.EnableInClassList("hidden", drinks == 0);
            _drinkLabel.EnableInClassList("hidden", drinks < PlayerCat.DrunkAt);

            // No toast on the first look (a reload) or once the cat is gone.
            if (previous < 0 || !inGame)
                return;
            if (drinks > previous)
            {
                switch (drinks)
                {
                    case 2:
                        Toasts.Show("One more glass and you'll be tipsy.");
                        break;
                    case PlayerCat.DrunkAt:
                        Toasts.Show("You're tipsy. The room sways, and blackjack wins pay 20% extra (poker 10%).");
                        break;
                    case MaxDrinkPips:
                        Toasts.Show("That's plenty. Everything's a bit of a blur.");
                        break;
                }
            }
            else if (previous >= PlayerCat.DrunkAt && drinks < PlayerCat.DrunkAt)
            {
                Toasts.Show("You feel steady again.");
            }
        }

        void SetOverPills(bool over)
        {
            if (over == _overPills)
                return;
            _overPills = over;
            if (over)
                GameInput.PushLookBlock();
            else
                GameInput.PopLookBlock();
        }

        void UpdateHint()
        {
            var seated = SeatingController.LocalTable != null;
            _emotesButton.SetEnabled(!seated);
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

        // ---------------------------------------------------------------- Chips earned (ChipRewards)

        /// <summary>
        /// "+10" pops up beside the balance with a coin sound, drifts up and fades; a big reward is larger, stays
        /// longer and chimes. Rewards close together (a prop and a patron in one swipe) add up in the same label.
        /// </summary>
        void OnChipsEarned(int amount, bool big, bool quiet)
        {
            if (!quiet)
                Sfx.Play(big ? SoundId.SlotChime : SoundId.Coin, big ? 0.9f : 0.7f);
            _gainAmount = _gainShowing ? _gainAmount + amount : amount;
            _gainBig = big || (_gainShowing && _gainBig);
            _gainShowing = true;
            _gainShownAt = Time.unscaledTime;
            _gain.text = $"+{_gainAmount:N0}";
            _gain.EnableInClassList("balance-gain--big", _gainBig);
            _gain.RemoveFromClassList("hidden");
            UpdateGain();
        }

        void UpdateGain()
        {
            if (!_gainShowing)
                return;
            var t = (Time.unscaledTime - _gainShownAt) / (_gainBig ? BigGainSeconds : GainSeconds);
            if (t >= 1f)
            {
                _gainShowing = false;
                _gain.AddToClassList("hidden");
                return;
            }

            // Pops in a little oversized, then rises and fades out over the last third.
            var pop = 1f + (_gainBig ? 0.5f : 0.25f) * Mathf.Clamp01(1f - t * 6f);
            _gain.style.scale = new Scale(new Vector3(pop, pop, 1f));
            _gain.style.translate = new Translate(0f, -14f * t);
            _gain.style.opacity = Mathf.Clamp01((1f - t) * 3f);
        }

        // ---------------------------------------------------------------- Leaderboard (hold Tab)

        void UpdateLeaderboard()
        {
            var show = InGame && (GameInput.KeyHeld(Key.Tab) || GameInput.TouchLeaderboard);
            _leaderboard.EnableInClassList("hidden", !show);
            if (!show)
                return;

            _leaderboardPanel.Update();
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
                    _distance.SetValueWithoutNotify(PlayerCameraRig.Instance.Distance);
                    _follow.SetValueWithoutNotify(PlayerCameraRig.Instance.AutoFollow);
                }

                _tipsy.SetValueWithoutNotify(TipsyEffect.VisualsEnabled);
                _volume.SetValueWithoutNotify(Sfx.MasterVolume);
                _fpsCap.SetValueWithoutNotify(FrameRateCap.Preference);
                _fullscreen.text = Screen.fullScreen ? "Leave full screen" : "Full screen";
                GameInput.PushUIBlock();
            }
            else
            {
                GameInput.PopUIBlock();
                // Volume, sensitivity and the camera settings are set here. A browser only keeps PlayerPrefs that are saved.
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
