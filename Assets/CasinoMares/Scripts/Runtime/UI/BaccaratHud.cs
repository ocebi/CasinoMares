using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Baccarat;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Seated baccarat UI: Player / Banker / Tie bet boxes, chip picker, Clear / Ready, the "road" of results, the
    /// rules and the optional coach (teaching mode).
    /// </summary>
    public class BaccaratHud : MonoBehaviour
    {
        static readonly int[] ChipValues = { 10, 25, 100 };

        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _phase;
        VisualElement _timer;
        VisualElement _timerFill;
        VisualElement _road;
        VisualElement _settingsPanel;
        Label _total;
        Button _doneButton;
        Button _betTimerButton;
        Button _npcButton;
        Button _coachButton;
        BaccaratTutor _tutor;
        readonly Button[] _spotButtons = new Button[BaccaratRules.Spots];
        readonly Label[] _spotBadges = new Label[BaccaratRules.Spots];
        readonly List<Button> _chipButtons = new();

        BaccaratTable _table;
        int _seat = -1;
        int _chip = 10;
        int _shownRoad = int.MinValue;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("bc-root");
            _phase = ui.Q<Label>("phase-label");
            _timer = ui.Q("timer");
            _timerFill = ui.Q("timer-fill");
            _road = ui.Q("road");
            _settingsPanel = ui.Q("settings-panel");
            _total = ui.Q<Label>("total-label");
            _doneButton = ui.Q<Button>("done-button");
            _betTimerButton = ui.Q<Button>("bet-timer-button");
            _npcButton = ui.Q<Button>("npc-button");
            _coachButton = ui.Q<Button>("coach-button");
            _tutor = new BaccaratTutor(_root);

            var spots = ui.Q("spot-row");
            spots.Clear();
            var labels = new[] { "PLAYER\npays 1:1", "BANKER\npays 0.95:1", "TIE\npays 8:1" };
            var styles = new[] { "bc-spot--player", "bc-spot--banker", "bc-spot--tie" };
            for (var spot = 0; spot < BaccaratRules.Spots; spot++)
            {
                var index = spot;
                var button = new Button(() => PlaceChip(index)) { text = labels[spot] };
                button.AddToClassList("bc-spot");
                button.AddToClassList(styles[spot]);
                var badge = new Label { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("rl-chip-badge");
                button.Add(badge);
                spots.Add(button);
                _spotButtons[spot] = button;
                _spotBadges[spot] = badge;
            }

            var chipRow = ui.Q("chip-row");
            chipRow.Clear();
            _chipButtons.Clear();
            foreach (var value in ChipValues)
            {
                var chip = new Button(() => _chip = value) { text = value.ToString(), userData = value };
                chip.AddToClassList("bj-chip");
                chip.style.backgroundColor = ChipStack.ColorFor(value);
                chipRow.Add(chip);
                _chipButtons.Add(chip);
            }

            ui.Q<Button>("clear-button").clicked += () => _table?.ClearBets(_seat);
            _doneButton.clicked += () => _table?.SetDone(_seat);
            ui.Q<Button>("settings-button").clicked += () => _settingsPanel.ToggleInClassList("hidden");
            ui.Q<Button>("rules-button").clicked += () => _tutor.Panel.ShowRules(!_tutor.Panel.RulesOpen);
            _coachButton.clicked += () => _tutor.Panel.Enabled = !_tutor.Panel.Enabled;
            ui.Q<Button>("leave-button").clicked += () => _table?.RequestLeave(_seat);
            _betTimerButton.clicked += () => _table?.ChangeSettings((_table.BetTimerOption + 1) % BaccaratTable.BetTimerOptions.Length, _table.NpcFill);
            _npcButton.clicked += () => _table?.ChangeSettings(_table.BetTimerOption, NpcSeating.NextOption(_table.NpcFill));

            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            BaccaratTable.LocalRoundResult += OnLocalRoundResult;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable()
        {
            SeatingController.LocalSeatChanged -= OnLocalSeatChanged;
            BaccaratTable.LocalRoundResult -= OnLocalRoundResult;
        }

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            _table = table as BaccaratTable;
            _seat = _table != null ? seat : -1;
            _root.EnableInClassList("hidden", _table == null);
            _settingsPanel.AddToClassList("hidden");
            _shownRoad = int.MinValue;
            _tutor.Panel.SetSeated(_table != null);
            if (_table != null && !_tutor.Panel.Enabled && PlayerPrefs.GetInt("Coach.Baccarat.Hinted", 0) == 0)
            {
                PlayerPrefs.SetInt("Coach.Baccarat.Hinted", 1);
                Toasts.Show("New to baccarat? Click Rules, or turn on the coach in Settings for live tips.");
            }
        }

        void OnLocalRoundResult(BaccaratTable table, int net)
        {
            Sfx.Play(net >= 0 ? SoundId.Win : SoundId.Lose, 0.8f);
            Toasts.Show(net > 0 ? $"Baccarat pays you {net:N0}!" : net < 0 ? $"You lost {-net:N0}." : "Push: bets returned.");
        }

        void PlaceChip(int spot)
        {
            if (_table == null || _table.Phase != BaccaratPhase.Betting)
                return;
            if (_chip > ProfileStore.Current.Balance)
            {
                Toasts.Show("Not enough chips for that bet.");
                return;
            }

            if (_table.TotalBetAt(_seat) + _chip > BaccaratRules.MaxTotalBet)
            {
                Toasts.Show($"Table limit is {BaccaratRules.MaxTotalBet} per coup.");
                return;
            }

            _table.AddBet(_seat, spot, _chip);
        }

        void Update()
        {
            if (_table == null || _table.Object == null || !_table.Object.IsValid)
                return;

            var phase = _table.Phase;
            var betting = phase == BaccaratPhase.Betting;
            for (var spot = 0; spot < BaccaratRules.Spots; spot++)
            {
                var amount = _table.BetAt(_seat, spot);
                _spotBadges[spot].style.display = amount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _spotBadges[spot].text = amount.ToString();
                _spotButtons[spot].SetEnabled(betting);
                var winner = phase == BaccaratPhase.Result &&
                             (int)_table.Outcome - 1 == spot;
                _spotButtons[spot].EnableInClassList("rl-cell--winner", winner);
            }

            _tutor.Update(_table, _seat);
            for (var spot = 0; spot < BaccaratRules.Spots; spot++)
                _spotButtons[spot].EnableInClassList("coach-suggested", betting && _tutor.SuggestedSpot == spot);

            foreach (var chip in _chipButtons)
                chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor =
                    (int)chip.userData == _chip ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 1f, 1f, 0.6f);

            _total.text = $"Bet {_table.TotalBetAt(_seat):N0}";
            _doneButton.SetEnabled(betting && !_table.IsDoneAt(_seat));
            _phase.text = phase switch
            {
                BaccaratPhase.WaitingForPlayers => "Waiting for players",
                BaccaratPhase.Betting => _table.IsDoneAt(_seat) ? "Ready! Waiting for the others..." : "Place your bets",
                BaccaratPhase.Dealing => $"Player {_table.VisibleTotal(false)} · Banker {_table.VisibleTotal(true)}",
                BaccaratPhase.Result => _table.Outcome == BaccaratOutcome.Tie ? "Tie!" : $"{_table.Outcome} wins {_table.VisibleTotal(_table.Outcome == BaccaratOutcome.Banker)}",
                _ => "",
            };

            var remaining = _table.TimeRemaining;
            var showTimer = betting && remaining >= 0f;
            _timer.EnableInClassList("hidden", !showTimer);
            if (showTimer)
                _timerFill.style.width = Length.Percent(100f * remaining / Mathf.Max(1f, _table.BetTimerSeconds));

            _betTimerButton.text = $"Betting timer: {_table.BetTimerSeconds}s";
            _npcButton.text = $"NPC players: {NpcSeating.Describe(_table.NpcFill)}";
            _coachButton.text = $"Coach (just for you): {(_tutor.Panel.Enabled ? "on" : "off")}";

            if (GameInput.KeyPressed(UnityEngine.InputSystem.Key.Space) && betting)
                _table.SetDone(_seat);

            UpdateRoad();
        }

        void UpdateRoad()
        {
            var head = (int)_table.HistoryAt(0) * 100000 + _table.SettledRoundId;
            if (head == _shownRoad)
                return;
            _shownRoad = head;

            _road.Clear();
            for (var i = 0; i < BaccaratTable.HistoryLength; i++)
            {
                var outcome = _table.HistoryAt(i);
                if (outcome == BaccaratOutcome.None)
                    break;
                var item = new Label(outcome.ToString().Substring(0, 1)) { pickingMode = PickingMode.Ignore };
                item.AddToClassList("rl-history-item");
                item.style.backgroundColor = outcome switch
                {
                    BaccaratOutcome.Player => new Color(0.2f, 0.4f, 0.85f),
                    BaccaratOutcome.Banker => new Color(0.75f, 0.15f, 0.18f),
                    _ => new Color(0.1f, 0.5f, 0.25f),
                };
                _road.Add(item);
            }
        }
    }
}
