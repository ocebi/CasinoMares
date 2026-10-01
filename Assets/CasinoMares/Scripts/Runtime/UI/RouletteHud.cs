using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.CameraRig;
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
    /// Clear / Ready, the phase banner with timer, recent results and table settings. As at a real table, every seat
    /// plays its own chip colour: the other seats' bets show as coloured dots on the layout, with a legend above it
    /// and the amounts for whatever spot the pointer is over. During the spin the board steps aside and the camera
    /// moves in over the wheel (a personal setting).
    /// </summary>
    public class RouletteHud : MonoBehaviour
    {
        static readonly int[] ChipValues = { 5, 10, 25, 100 };
        const string WheelCloseUpKey = "Roulette.WheelCloseUp";
        /// <summary>Seconds the wheel close-up stays after the result is called, before the board comes back.</summary>
        const float WheelLinger = 1.5f;

        /// <summary>Each seat's chip colour, left to right as seen from the stools. Your own bets show in gold.</summary>
        static readonly Color[] SeatColors =
        {
            new(0.36f, 0.7f, 1f), // sky blue
            new(1f, 0.45f, 0.78f), // pink
            new(1f, 0.58f, 0.22f), // orange
            new(0.58f, 0.9f, 0.3f), // lime
            new(0.72f, 0.56f, 1f), // violet
            new(0.25f, 0.86f, 0.78f), // teal
        };

        static readonly Color YouColor = new(1f, 0.82f, 0.36f);

        sealed class CellView
        {
            public Button Cell;
            public Label Badge;
            public VisualElement[] Dots;
        }

        sealed class PlayerEntry
        {
            public VisualElement Root;
            public VisualElement Swatch;
            public Label Text;
        }

        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _phase;
        VisualElement _timer;
        VisualElement _timerFill;
        VisualElement _history;
        VisualElement _settingsPanel;
        VisualElement _bottom;
        Label _total;
        Label _spotInfo;
        Button _doneButton;
        Button _betTimerButton;
        Button _npcButton;
        Button _wheelCloseUpButton;
        readonly Dictionary<int, CellView> _cells = new();
        readonly PlayerEntry[] _players = new PlayerEntry[RouletteTable.SeatCount];
        readonly List<Button> _chipButtons = new();
        readonly System.Text.StringBuilder _text = new();

        RouletteTable _table;
        int _seat = -1;
        int _chip = 10;
        int _hoverSpot = -1;
        int _shownHistoryHead = int.MinValue;
        bool _watchingWheel;

        static bool WheelCloseUp
        {
            get => PlayerPrefs.GetInt(WheelCloseUpKey, 1) == 1;
            set => PlayerPrefs.SetInt(WheelCloseUpKey, value ? 1 : 0);
        }

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
            _bottom = _root.Q(className: "bj-bottom");
            _total = ui.Q<Label>("total-label");
            _doneButton = ui.Q<Button>("done-button");
            _betTimerButton = ui.Q<Button>("bet-timer-button");
            _npcButton = ui.Q<Button>("npc-button");
            _wheelCloseUpButton = ui.Q<Button>("wheel-closeup-button");

            BuildBoard(ui.Q("board"));
            BuildChips(ui.Q("chip-row"));

            ui.Q<Button>("clear-button").clicked += () => _table?.ClearBets(_seat);
            _doneButton.clicked += () => _table?.SetDone(_seat, true);
            ui.Q<Button>("settings-button").clicked += () => _settingsPanel.ToggleInClassList("hidden");
            ui.Q<Button>("leave-button").clicked += () => _table?.RequestLeave(_seat);
            _betTimerButton.clicked += () => _table?.ChangeSettings((_table.BetTimerOption + 1) % RouletteTable.BetTimerOptions.Length, _table.NpcFill);
            _npcButton.clicked += () => _table?.ChangeSettings(_table.BetTimerOption, NpcSeating.NextOption(_table.NpcFill));
            _wheelCloseUpButton.clicked += () => WheelCloseUp = !WheelCloseUp;

            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            RouletteTable.LocalRoundResult += OnLocalRoundResult;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable()
        {
            SeatingController.LocalSeatChanged -= OnLocalSeatChanged;
            RouletteTable.LocalRoundResult -= OnLocalRoundResult;
            WatchWheel(false);
        }

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            WatchWheel(false);
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

            // Who's at the table, in their chip colours, and what's on the spot under the pointer.
            var players = new VisualElement();
            players.AddToClassList("rl-players");
            board.Add(players);
            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var entry = new PlayerEntry { Root = new VisualElement(), Swatch = new VisualElement(), Text = new Label() };
                entry.Root.AddToClassList("rl-player");
                entry.Swatch.AddToClassList("rl-player-swatch");
                entry.Text.AddToClassList("rl-player-name");
                entry.Root.Add(entry.Swatch);
                entry.Root.Add(entry.Text);
                players.Add(entry.Root);
                _players[seat] = entry;
            }

            _spotInfo = new Label { pickingMode = PickingMode.Ignore };
            _spotInfo.AddToClassList("rl-spot-info");
            board.Add(_spotInfo);

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

            var dots = new VisualElement { pickingMode = PickingMode.Ignore };
            dots.AddToClassList("rl-dots");
            cell.Add(dots);
            var view = new CellView { Cell = cell, Badge = badge, Dots = new VisualElement[RouletteTable.SeatCount] };
            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("rl-dot");
                dot.style.backgroundColor = SeatColors[seat];
                dot.style.display = DisplayStyle.None;
                dots.Add(dot);
                view.Dots[seat] = dot;
            }

            cell.RegisterCallback<PointerEnterEvent>(_ => _hoverSpot = spot);
            cell.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_hoverSpot == spot)
                    _hoverSpot = -1;
            });
            _cells[spot] = view;
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
                var view = pair.Value;
                var amount = _table.BetAt(_seat, pair.Key);
                view.Badge.style.display = amount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                view.Badge.text = amount.ToString();
                view.Cell.SetEnabled(betting);
                view.Cell.EnableInClassList("rl-cell--winner", result >= 0 && RouletteRules.Wins(pair.Key, result));
                for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
                    view.Dots[seat].style.display = seat != _seat && _table.BetAt(seat, pair.Key) > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdatePlayers(betting);
            UpdateSpotInfo();

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
            _wheelCloseUpButton.text = $"Wheel close-up (just for you): {(WheelCloseUp ? "on" : "off")}";

            // Watch the ball from close by, then bring the board back once the result has sunk in.
            WatchWheel(WheelCloseUp && _table.WheelCamera != null &&
                       (phase == RoulettePhase.Spinning || phase == RoulettePhase.Result && _table.TimeRemaining > RouletteTable.ResultDuration - WheelLinger));

            if (GameInput.KeyPressed(UnityEngine.InputSystem.Key.Space) && betting)
                _table.SetDone(_seat, true);

            UpdateHistory();
        }

        void WatchWheel(bool watch)
        {
            if (watch == _watchingWheel)
                return;
            _watchingWheel = watch;
            _bottom?.EnableInClassList("hidden", watch);
            if (watch)
                PlayerCameraRig.Instance?.SetFocus(_table.WheelCamera);
            else
                PlayerCameraRig.Instance?.ClearFocus();
        }

        string NameAt(int seat)
        {
            if (seat == _seat)
                return "You";
            return _table.OccupantAt(seat) switch
            {
                RouletteOccupant.Player => PlayerCat.NameOf(_table.PlayerAt(seat)),
                RouletteOccupant.Npc => "NPC",
                // Left mid-spin: their chips still ride.
                _ => _table.RoundOwnerAt(seat) != Fusion.PlayerRef.None ? PlayerCat.NameOf(_table.RoundOwnerAt(seat)) : "Empty seat",
            };
        }

        void UpdatePlayers(bool betting)
        {
            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var entry = _players[seat];
                var total = _table.TotalBetAt(seat);
                var occupant = _table.OccupantAt(seat);
                var present = occupant == RouletteOccupant.Player || occupant == RouletteOccupant.Npc && _table.NpcArrivalAt(seat) <= 0f;
                var shown = present || total > 0;
                entry.Root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown)
                    continue;

                entry.Swatch.style.backgroundColor = seat == _seat ? YouColor : SeatColors[seat];
                var status = total > 0 ? total.ToString("N0") : betting ? "deciding" : "sitting out";
                if (betting && total > 0 && occupant == RouletteOccupant.Player && _table.IsDoneAt(seat))
                    status += " · ready";
                entry.Text.text = $"{NameAt(seat)}  {status}";
            }
        }

        void UpdateSpotInfo()
        {
            if (_hoverSpot < 0)
            {
                _spotInfo.text = "The coloured dots are the other players' chips. Point at a spot to see who bet what.";
                return;
            }

            _text.Clear();
            _text.Append(_hoverSpot < RouletteRules.Numbers ? $"{_hoverSpot} {RouletteRules.ColorName(_hoverSpot)}" : RouletteRules.SpotLabel(_hoverSpot));
            _text.Append(": ");
            var any = false;
            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var amount = _table.BetAt(seat, _hoverSpot);
                if (amount <= 0)
                    continue;
                if (any)
                    _text.Append(" · ");
                _text.Append(NameAt(seat)).Append(' ').Append(amount.ToString("N0"));
                any = true;
            }

            if (!any)
                _text.Append("no chips here yet");
            _spotInfo.text = _text.ToString();
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
