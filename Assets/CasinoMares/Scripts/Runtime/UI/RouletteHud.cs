using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Roulette;
using CasinoMares.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// The seated roulette UI: a clickable betting layout (numbers, columns, dozens, even-money), chip picker,
    /// Clear / Ready, the phase banner with timer, recent results and table settings.
    /// </summary>
    public class RouletteHud : MonoBehaviour
    {
        static readonly int[] ChipValues = { 5, 10, 25, 100 };

        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _phase;
        VisualElement _timer;
        VisualElement _timerFill;
        VisualElement _history;
        VisualElement _settingsPanel;
        Label _total;
        Button _doneButton;
        Button _betTimerButton;
        Button _npcButton;
        readonly Dictionary<int, (Button cell, Label badge)> _cells = new();
        readonly List<Button> _chipButtons = new();

        RouletteTable _table;
        int _seat = -1;
        int _chip = 10;
        int _shownHistoryHead = int.MinValue;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("rl-root");
            _phase = ui.Q<Label>("phase-label");
            _timer = ui.Q("timer");
            _timerFill = ui.Q("timer-fill");
            _history = ui.Q("history");
            _settingsPanel = ui.Q("settings-panel");
            _total = ui.Q<Label>("total-label");
            _doneButton = ui.Q<Button>("done-button");
            _betTimerButton = ui.Q<Button>("bet-timer-button");
            _npcButton = ui.Q<Button>("npc-button");

            BuildBoard(ui.Q("board"));
            BuildChips(ui.Q("chip-row"));

            ui.Q<Button>("clear-button").clicked += () => _table?.ClearBets(_seat);
            _doneButton.clicked += () => _table?.SetDone(_seat, true);
            ui.Q<Button>("settings-button").clicked += () => _settingsPanel.ToggleInClassList("hidden");
            ui.Q<Button>("leave-button").clicked += () => _table?.RequestLeave(_seat);
            _betTimerButton.clicked += () => _table?.ChangeSettings((_table.BetTimerOption + 1) % RouletteTable.BetTimerOptions.Length, _table.NpcFill);
            _npcButton.clicked += () => _table?.ChangeSettings(_table.BetTimerOption, NpcSeating.NextOption(_table.NpcFill));

            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            RouletteTable.LocalRoundResult += OnLocalRoundResult;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable()
        {
            SeatingController.LocalSeatChanged -= OnLocalSeatChanged;
            RouletteTable.LocalRoundResult -= OnLocalRoundResult;
        }

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            _table = table as RouletteTable;
            _seat = _table != null ? seat : -1;
            _root.EnableInClassList("hidden", _table == null);
            _settingsPanel.AddToClassList("hidden");
            _shownHistoryHead = int.MinValue;
        }

        void OnLocalRoundResult(RouletteTable table, int net)
        {
            Sfx.Play(net >= 0 ? SoundId.Win : SoundId.Lose, 0.8f);
            Toasts.Show(net > 0 ? $"Roulette pays you {net:N0}!" : net < 0 ? $"The house keeps {-net:N0}." : "You broke even.");
        }

        // ---------------------------------------------------------------- Board

        void BuildBoard(VisualElement board)
        {
            board.Clear();
            _cells.Clear();

            var top = new VisualElement();
            top.AddToClassList("rl-row");
            board.Add(top);

            // Zero spans the three number rows.
            var zero = Cell(0, "0", "rl-cell--green");
            zero.style.height = 3 * 42 - 2;
            top.Add(zero);

            var grid = new VisualElement();
            top.Add(grid);
            for (var row = 2; row >= 0; row--)
            {
                var line = new VisualElement();
                line.AddToClassList("rl-row");
                for (var column = 0; column < 12; column++)
                {
                    var number = column * 3 + row + 1;
                    line.Add(Cell(number, number.ToString(), RouletteRules.IsRed(number) ? "rl-cell--red" : "rl-cell--black"));
                }

                line.Add(Cell(RouletteRules.Column1 + row, "2:1", null));
                grid.Add(line);
            }

            var dozens = new VisualElement();
            dozens.AddToClassList("rl-row");
            dozens.style.marginLeft = 48;
            for (var d = 0; d < 3; d++)
            {
                var cell = Cell(RouletteRules.Dozen1 + d, $"{d * 12 + 1} to {(d + 1) * 12}", null);
                cell.style.width = 4 * 48 - 2;
                dozens.Add(cell);
            }

            board.Add(dozens);

            var outside = new VisualElement();
            outside.AddToClassList("rl-row");
            outside.style.marginLeft = 48;
            foreach (var (spot, label, style) in new (int, string, string)[]
                     {
                         (RouletteRules.Low, "1-18", null), (RouletteRules.Even, "EVEN", null), (RouletteRules.Red, "RED", "rl-cell--red"),
                         (RouletteRules.Black, "BLACK", "rl-cell--black"), (RouletteRules.Odd, "ODD", null), (RouletteRules.High, "19-36", null),
                     })
            {
                var cell = Cell(spot, label, style);
                cell.style.width = 2 * 48 - 2;
                outside.Add(cell);
            }

            board.Add(outside);
        }

        Button Cell(int spot, string label, string style)
        {
            var cell = new Button(() => PlaceChip(spot)) { text = label };
            cell.AddToClassList("rl-cell");
            if (style != null)
                cell.AddToClassList(style);
            var badge = new Label { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("rl-chip-badge");
            badge.style.display = DisplayStyle.None;
            cell.Add(badge);
            _cells[spot] = (cell, badge);
            return cell;
        }

        void BuildChips(VisualElement row)
        {
            row.Clear();
            _chipButtons.Clear();
            foreach (var value in ChipValues)
            {
                var chip = new Button(() => _chip = value) { text = value.ToString() };
                chip.AddToClassList("bj-chip");
                var color = ChipStack.ColorFor(value);
                chip.style.backgroundColor = color;
                if (color.grayscale > 0.7f)
                    chip.style.color = new Color(0.15f, 0.15f, 0.2f);
                chip.userData = value;
                row.Add(chip);
                _chipButtons.Add(chip);
            }
        }

        void PlaceChip(int spot)
        {
            if (_table == null || _table.Phase != RoulettePhase.Betting)
                return;
            var total = _table.TotalBetAt(_seat);
            if (_chip > ProfileStore.Current.Balance)
            {
                Toasts.Show("Not enough chips for that bet.");
                return;
            }

            if (total + _chip > RouletteRules.MaxTotalBet)
            {
                Toasts.Show($"Table limit is {RouletteRules.MaxTotalBet} per spin.");
                return;
            }

            _table.AddBet(_seat, spot, _chip);
        }

        // ---------------------------------------------------------------- Per frame

        void Update()
        {
            if (_table == null || _table.Object == null || !_table.Object.IsValid)
                return;

            var phase = _table.Phase;
            var betting = phase == RoulettePhase.Betting;
            var result = phase == RoulettePhase.Result ? _table.Result : -1;

            foreach (var pair in _cells)
            {
                var amount = _table.BetAt(_seat, pair.Key);
                pair.Value.badge.style.display = amount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                pair.Value.badge.text = amount.ToString();
                pair.Value.cell.SetEnabled(betting);
                pair.Value.cell.EnableInClassList("rl-cell--winner", result >= 0 && RouletteRules.Wins(pair.Key, result));
            }

            foreach (var chip in _chipButtons)
                chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor =
                    (int)chip.userData == _chip ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 1f, 1f, 0.6f);

            _total.text = $"Bet {_table.TotalBetAt(_seat):N0}";
            _doneButton.SetEnabled(betting && !_table.IsDoneAt(_seat));

            _phase.text = phase switch
            {
                RoulettePhase.WaitingForPlayers => "Waiting for players",
                RoulettePhase.Betting => _table.IsDoneAt(_seat) ? "Ready! Waiting for the others..." : "Place your bets",
                RoulettePhase.Spinning => "No more bets!",
                RoulettePhase.Result => $"{_table.Result} {RouletteRules.ColorName(_table.Result)}",
                _ => "",
            };

            var remaining = _table.TimeRemaining;
            var showTimer = betting && remaining >= 0f;
            _timer.EnableInClassList("hidden", !showTimer);
            if (showTimer)
                _timerFill.style.width = Length.Percent(100f * remaining / Mathf.Max(1f, _table.BetTimerSeconds));

            _betTimerButton.text = $"Betting timer: {_table.BetTimerSeconds}s";
            _npcButton.text = $"NPC players: {NpcSeating.Describe(_table.NpcFill)}";

            if (GameInput.KeyPressed(UnityEngine.InputSystem.Key.Space) && betting)
                _table.SetDone(_seat, true);

            UpdateHistory();
        }

        void UpdateHistory()
        {
            var head = _table.HistoryAt(0) * 1000 + _table.SettledRoundId;
            if (head == _shownHistoryHead)
                return;
            _shownHistoryHead = head;

            _history.Clear();
            for (var i = 0; i < RouletteTable.HistoryLength; i++)
            {
                var number = _table.HistoryAt(i);
                if (number < 0)
                    break;
                var item = new Label(number.ToString()) { pickingMode = PickingMode.Ignore };
                item.AddToClassList("rl-history-item");
                item.style.backgroundColor = number == 0 ? new Color(0.08f, 0.47f, 0.24f) : RouletteRules.IsRed(number) ? new Color(0.67f, 0.12f, 0.15f) : new Color(0.11f, 0.1f, 0.12f);
                item.style.opacity = 1f - i * 0.06f;
                _history.Add(item);
            }
        }
    }
}
