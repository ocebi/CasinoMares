using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// The seated blackjack UI (GDD §8): chip buttons and Deal while betting, Hit/Stand/Double/Split on your turn,
    /// a phase banner with the timer, table settings and Stand up. Keyboard: 1-5 chips, Backspace clear,
    /// Space deal, then H hit, Space stand, X double, P split (WASD is left free for looking around the table).
    /// </summary>
    public class BlackjackHud : MonoBehaviour
    {
        static readonly int[] ChipValues = { 5, 10, 25, 100, 500 };
        static readonly Key[] ChipKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };

        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _phase;
        VisualElement _timer;
        VisualElement _timerFill;
        VisualElement _betPanel;
        VisualElement _actionPanel;
        VisualElement _settingsPanel;
        Label _betLabel;
        Button _dealButton;
        Button _hitButton;
        Button _standButton;
        Button _doubleButton;
        Button _splitButton;
        Button _turnTimerButton;
        Button _betTimerButton;
        Button _npcButton;

        BlackjackTable _table;
        int _seat = -1;
        int _bet = 25;
        float _timerTotal;
        BlackjackPhase _lastPhase;
        int _lastWarningSecond = -1;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("bj-root");
            _phase = ui.Q<Label>("phase-label");
            _timer = ui.Q("timer");
            _timerFill = ui.Q("timer-fill");
            _betPanel = ui.Q("bet-panel");
            _actionPanel = ui.Q("action-panel");
            _settingsPanel = ui.Q("settings-panel");
            _betLabel = ui.Q<Label>("bet-label");
            _dealButton = ui.Q<Button>("deal-button");
            _hitButton = ui.Q<Button>("hit-button");
            _standButton = ui.Q<Button>("stand-button");
            _doubleButton = ui.Q<Button>("double-button");
            _splitButton = ui.Q<Button>("split-button");
            _turnTimerButton = ui.Q<Button>("turn-timer-button");
            _betTimerButton = ui.Q<Button>("bet-timer-button");
            _npcButton = ui.Q<Button>("npc-button");

            var chipRow = ui.Q("chip-row");
            chipRow.Clear();
            foreach (var value in ChipValues)
            {
                var chip = new Button(() => AddChip(value)) { text = value.ToString() };
                chip.AddToClassList("bj-chip");
                var color = ChipStack.ColorFor(value);
                chip.style.backgroundColor = color;
                if (color.grayscale > 0.7f)
                    chip.style.color = new Color(0.15f, 0.15f, 0.2f);
                chipRow.Add(chip);
            }

            ui.Q<Button>("clear-button").clicked += () => SetBet(0);
            ui.Q<Button>("sitout-button").clicked += SitOut;
            _dealButton.clicked += Deal;
            _hitButton.clicked += () => Act(BlackjackAction.Hit);
            _standButton.clicked += () => Act(BlackjackAction.Stand);
            _doubleButton.clicked += () => Act(BlackjackAction.Double);
            _splitButton.clicked += () => Act(BlackjackAction.Split);
            ui.Q<Button>("settings-button").clicked += () => _settingsPanel.ToggleInClassList("hidden");
            ui.Q<Button>("leave-button").clicked += () => _table?.RequestLeave(_seat);
            _turnTimerButton.clicked += () => CycleSetting(turn: 1);
            _betTimerButton.clicked += () => CycleSetting(bet: 1);
            _npcButton.clicked += () => CycleSetting(npc: 1);

            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            BlackjackTable.LocalRoundResult += OnLocalRoundResult;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable()
        {
            SeatingController.LocalSeatChanged -= OnLocalSeatChanged;
            BlackjackTable.LocalRoundResult -= OnLocalRoundResult;
        }

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            _table = table as BlackjackTable;
            _seat = _table != null ? seat : -1;
            _root.EnableInClassList("hidden", _table == null);
            _settingsPanel.AddToClassList("hidden");
        }

        void OnLocalRoundResult(BlackjackTable table, int net)
        {
            Sfx.Play(net >= 0 ? SoundId.Win : SoundId.Lose, 0.8f);
            if (net > 0)
                Toasts.Show($"You won {net:N0} chips!");
            else if (net < 0)
                Toasts.Show($"You lost {-net:N0} chips.");
            else
                Toasts.Show("Push: your bet comes back.");
        }

        void Update()
        {
            if (_table == null || _table.Object == null || !_table.Object.IsValid)
                return;

            var phase = _table.Phase;
            var myTurn = _table.IsActiveTurn(_seat);
            var betting = phase == BlackjackPhase.Betting;
            var locked = _table.IsLockedAt(_seat);

            _betPanel.EnableInClassList("hidden", !betting || locked);
            _actionPanel.EnableInClassList("hidden", !myTurn);

            _bet = Mathf.Min(_bet, Mathf.Min(BlackjackRules.MaxBet, ProfileStore.Current.Balance));
            _betLabel.text = _bet.ToString("N0");
            _dealButton.SetEnabled(_bet >= BlackjackRules.MinBet);

            if (myTurn)
            {
                var handBet = _table.HandBetAt(_seat, _table.ActiveHand);
                var canAffordMore = ProfileStore.Current.Balance >= handBet;
                _doubleButton.SetEnabled(_table.CanDouble(_seat) && canAffordMore);
                _splitButton.SetEnabled(_table.CanSplit(_seat) && canAffordMore);
            }

            UpdateBanner(phase, myTurn, locked);
            UpdateTimer(phase, myTurn);
            UpdateSettingsLabels();
            HandleKeys(betting && !locked, myTurn);
            _lastPhase = phase;
        }

        void UpdateBanner(BlackjackPhase phase, bool myTurn, bool locked)
        {
            _phase.text = phase switch
            {
                BlackjackPhase.WaitingForPlayers => "Waiting for players",
                BlackjackPhase.Betting => locked
                    ? _table.PendingBetAt(_seat) > 0 ? "Bet locked. Waiting for the others..." : "Sitting this round out"
                    : $"Place your bet ({BlackjackRules.MinBet}-{BlackjackRules.MaxBet})",
                BlackjackPhase.Dealing => "Dealing...",
                BlackjackPhase.PlayerTurns => myTurn
                    ? $"Your turn: {_table.EvaluateHand(_seat, _table.ActiveHand)}" + (_table.HandCountAt(_seat) > 1 ? $" (hand {_table.ActiveHand + 1})" : "")
                    : $"{TurnName()}'s turn",
                BlackjackPhase.DealerTurn => $"Dealer's turn: {_table.EvaluateDealerVisible()}",
                BlackjackPhase.Settlement => SettlementText(),
                _ => "",
            };
        }

        string TurnName()
        {
            var seat = _table.ActiveSeat;
            if (seat < 0)
                return "Someone";
            var owner = _table.RoundOwnerAt(seat);
            return owner == Fusion.PlayerRef.None ? $"Seat {seat + 1} (NPC)" : PlayerCat.NameOf(owner);
        }

        string SettlementText()
        {
            var hands = _table.HandCountAt(_seat);
            if (hands == 0 || _table.RoundOwnerAt(_seat) != _table.Runner.LocalPlayer)
                return $"Dealer has {_table.EvaluateDealerVisible()}";

            var net = 0;
            var blackjack = false;
            for (var h = 0; h < hands; h++)
            {
                var outcome = _table.OutcomeAt(_seat, h);
                blackjack |= outcome == HandOutcome.Blackjack;
                net += BlackjackRules.Payout(outcome, _table.HandBetAt(_seat, h)) - _table.HandBetAt(_seat, h);
            }

            if (blackjack && net > 0)
                return $"Blackjack! +{net:N0}";
            if (net > 0)
                return $"You win +{net:N0}";
            return net < 0 ? $"Dealer wins ({net:N0})" : "Push";
        }

        void UpdateTimer(BlackjackPhase phase, bool myTurn)
        {
            var remaining = _table.TimeRemaining;
            var showTimer = remaining >= 0f && (phase == BlackjackPhase.Betting || phase == BlackjackPhase.PlayerTurns);
            _timer.EnableInClassList("hidden", !showTimer);
            if (!showTimer)
                return;

            if (phase != _lastPhase || remaining > _timerTotal)
                _timerTotal = phase == BlackjackPhase.Betting ? _table.BetTimerSeconds : _table.TurnTimerSeconds;
            _timerFill.style.width = Length.Percent(_timerTotal > 0f ? 100f * remaining / _timerTotal : 0f);

            // Tick each second of the last five on your own turn.
            var second = Mathf.CeilToInt(remaining);
            if (myTurn && remaining < 5f && second != _lastWarningSecond)
                Sfx.Play(SoundId.Tick, 0.6f);
            _lastWarningSecond = second;
            _timerFill.style.backgroundColor = myTurn && remaining < 5f ? new Color(0.87f, 0.4f, 0.37f) : new Color(0.91f, 0.73f, 0.38f);
        }

        void UpdateSettingsLabels()
        {
            static string Seconds(int value) => value == 0 ? "Off" : $"{value}s";
            _turnTimerButton.text = $"Turn timer: {Seconds(_table.TurnTimerSeconds)}";
            _betTimerButton.text = $"Betting timer: {Seconds(_table.BetTimerSeconds)}";
            _npcButton.text = $"NPC players: {NpcSeating.Describe(_table.NpcFill)}";
        }

        void HandleKeys(bool canBet, bool myTurn)
        {
            if (canBet)
            {
                for (var i = 0; i < ChipKeys.Length; i++)
                {
                    if (GameInput.KeyPressed(ChipKeys[i]))
                        AddChip(ChipValues[i]);
                }

                if (GameInput.KeyPressed(Key.Backspace))
                    SetBet(0);
                if (GameInput.KeyPressed(Key.Space))
                    Deal();
            }

            if (!myTurn)
                return;

            if (GameInput.KeyPressed(Key.H))
                Act(BlackjackAction.Hit);
            else if (GameInput.KeyPressed(Key.Space))
                Act(BlackjackAction.Stand);
            else if (GameInput.KeyPressed(Key.X) && _doubleButton.enabledSelf)
                Act(BlackjackAction.Double);
            else if (GameInput.KeyPressed(Key.P) && _splitButton.enabledSelf)
                Act(BlackjackAction.Split);
        }

        void AddChip(int value) => SetBet(_bet + value);

        void SetBet(int amount)
        {
            _bet = Mathf.Clamp(amount, 0, Mathf.Min(BlackjackRules.MaxBet, ProfileStore.Current.Balance));
        }

        void Deal()
        {
            if (_table == null || _bet < BlackjackRules.MinBet || _bet > ProfileStore.Current.Balance)
                return;
            _table.PlaceBet(_seat, _bet, true);
        }

        void SitOut()
        {
            _table?.PlaceBet(_seat, 0, true);
        }

        void Act(BlackjackAction action)
        {
            if (_table != null && _table.IsActiveTurn(_seat))
                _table.Act(_seat, action);
        }

        void CycleSetting(int turn = 0, int bet = 0, int npc = 0)
        {
            if (_table == null)
                return;
            _table.ChangeSettings(
                (_table.TurnTimerOption + turn) % BlackjackTable.TurnTimerOptions.Length,
                (_table.BetTimerOption + bet) % BlackjackTable.BetTimerOptions.Length,
                npc > 0 ? NpcSeating.NextOption(_table.NpcFill) : _table.NpcFill);
        }
    }
}
