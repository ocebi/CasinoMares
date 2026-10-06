using System.Collections.Generic;
using System.Text;
using CasinoMares.Baccarat;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Poker;
using CasinoMares.Roulette;
using CasinoMares.Slots;
using CasinoMares.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// World-anchored labels over every hand at nearby tables (totals, results, whose turn), plus the spectator
    /// panel when you stand near a playing friend (GDD §6). Everything shown is public table information, read from
    /// each table's shown round so the labels change together with the cards.
    /// </summary>
    public class TableOverlay : MonoBehaviour
    {
        const float LabelRange = 8f;
        const float SpectateRange = 3f;

        [SerializeField] UIDocument _document;

        VisualElement _layer;
        VisualElement _spectator;
        Label _spectatorName;
        Label _spectatorBody;
        readonly List<Label> _labels = new();
        readonly StringBuilder _text = new();
        readonly Dictionary<BlackjackTable, BlackjackTableView> _views = new();
        readonly Dictionary<RouletteTable, RouletteView> _rouletteViews = new();
        readonly Dictionary<BaccaratTable, BaccaratView> _baccaratViews = new();
        readonly Dictionary<PokerTable, PokerView> _pokerViews = new();
        int _used;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            _layer = ui.Q("label-layer");
            _spectator = ui.Q("spectator");
            _spectatorName = ui.Q<Label>("spectator-name");
            _spectatorBody = ui.Q<Label>("spectator-body");
        }

        void LateUpdate()
        {
            _used = 0;
            var cam = Camera.main;
            if (cam != null && _layer.panel != null)
            {
                foreach (var table in BlackjackTable.All)
                {
                    if (table == null || table.Object == null || !table.Object.IsValid)
                        continue;
                    if ((table.transform.position - cam.transform.position).sqrMagnitude > LabelRange * LabelRange)
                        continue;
                    LabelTable(table, cam);
                }

                foreach (var poker in PokerTable.All)
                {
                    if (poker == null || poker.Object == null || !poker.Object.IsValid)
                        continue;
                    if ((poker.transform.position - cam.transform.position).sqrMagnitude > LabelRange * LabelRange)
                        continue;
                    LabelPoker(poker, cam);
                }

                foreach (var baccarat in BaccaratTable.All)
                {
                    if (baccarat == null || baccarat.Object == null || !baccarat.Object.IsValid || baccarat.Shown.Revealed == 0)
                        continue;
                    if ((baccarat.transform.position - cam.transform.position).sqrMagnitude > LabelRange * LabelRange)
                        continue;
                    LabelBaccarat(baccarat, cam);
                }

                foreach (var machine in SlotMachine.All)
                {
                    if (machine == null || machine.Object == null || !machine.Object.IsValid || machine.SpinId == 0)
                        continue;
                    if ((machine.transform.position - cam.transform.position).sqrMagnitude > LabelRange * LabelRange)
                        continue;
                    LabelSlots(machine, cam);
                }

                foreach (var roulette in RouletteTable.All)
                {
                    if (roulette == null || roulette.Object == null || !roulette.Object.IsValid)
                        continue;
                    if ((roulette.transform.position - cam.transform.position).sqrMagnitude > LabelRange * LabelRange)
                        continue;
                    LabelRoulette(roulette, cam);
                }
            }

            for (var i = _used; i < _labels.Count; i++)
                _labels[i].style.display = DisplayStyle.None;

            UpdateSpectator();
        }

        void LabelTable(BlackjackTable table, Camera cam)
        {
            if (!_views.TryGetValue(table, out var view) || view == null)
            {
                view = table.GetComponent<BlackjackTableView>();
                _views[table] = view;
            }

            if (view == null)
                return;

            var shown = table.Shown;
            for (var seat = 0; seat < BlackjackTable.SeatCount; seat++)
            {
                var hands = shown.HandCountAt(seat);
                for (var hand = 0; hand < hands; hand++)
                {
                    if (shown.CardCountAt(seat, hand) == 0)
                        continue;

                    var outcome = shown.OutcomeAt(seat, hand);
                    var value = shown.EvaluateHand(seat, hand);
                    var text = outcome == HandOutcome.None
                        ? value.IsBust ? "Bust" : value.ToString()
                        : $"{value.Total} · {BlackjackTable.OutcomeLabel(outcome)}";

                    var label = Place(view.HandLabelPosition(seat, hand, hands), text, cam);
                    if (label == null)
                        continue;
                    label.EnableInClassList("hand-label--active", shown.IsActiveTurn(seat) && shown.ActiveHand == hand);
                    label.EnableInClassList("hand-label--win", outcome is HandOutcome.Win or HandOutcome.Blackjack);
                    label.EnableInClassList("hand-label--lose", outcome is HandOutcome.Lose or HandOutcome.Bust || (outcome == HandOutcome.None && value.IsBust));
                    label.EnableInClassList("hand-label--push", outcome == HandOutcome.Push);
                }
            }

            if (shown.DealerCardCount > 0)
            {
                var dealer = Place(view.DealerLabelPosition, $"Dealer {shown.EvaluateDealerVisible()}", cam);
                dealer?.EnableInClassList("hand-label--active", shown.Phase == BlackjackPhase.DealerTurn);
            }
        }

        void LabelPoker(PokerTable table, Camera cam)
        {
            if (!_pokerViews.TryGetValue(table, out var view) || view == null)
            {
                view = table.GetComponent<PokerView>();
                _pokerViews[table] = view;
            }

            var shown = table.Shown;
            if (view == null || shown.Phase == PokerPhase.WaitingForPlayers)
                return;

            if (shown.TotalPot > 0 && !(shown.Phase == PokerPhase.Showdown && shown.ShowdownSettled))
                Place(view.PotLabelPosition, $"Pot {shown.TotalPot:N0}", cam);

            for (var seat = 0; seat < PokerTable.SeatCount; seat++)
            {
                if (table.OccupantAt(seat) == PokerOccupant.Empty || (shown.FlagsAt(seat) & PokerSeatFlags.InHand) == 0)
                    continue;

                string text;
                if (shown.Phase == PokerPhase.Showdown && shown.WonAt(seat) > 0)
                    text = $"+{shown.WonAt(seat):N0}";
                else if ((shown.FlagsAt(seat) & PokerSeatFlags.Folded) != 0)
                    text = "Fold";
                else if ((shown.FlagsAt(seat) & PokerSeatFlags.AllIn) != 0)
                    text = "All in";
                else
                    text = PokerTable.ActionLabel(shown.LastActionAt(seat), shown.LastAmountAt(seat));

                if (string.IsNullOrEmpty(text))
                    continue;
                var label = Place(view.SeatLabelPosition(seat), text, cam);
                if (label == null)
                    continue;
                label.EnableInClassList("hand-label--active", shown.IsActiveTurn(seat));
                label.EnableInClassList("hand-label--win", shown.Phase == PokerPhase.Showdown && shown.WonAt(seat) > 0);
                label.EnableInClassList("hand-label--push", (shown.FlagsAt(seat) & PokerSeatFlags.Folded) != 0);
            }
        }

        void LabelBaccarat(BaccaratTable table, Camera cam)
        {
            if (!_baccaratViews.TryGetValue(table, out var view) || view == null)
            {
                view = table.GetComponent<BaccaratView>();
                _baccaratViews[table] = view;
            }

            if (view == null)
                return;

            var shown = table.Shown;
            var result = shown.Phase == BaccaratPhase.Result;
            var player = Place(view.PlayerLabelPosition, $"Player {shown.VisibleTotal(false)}", cam);
            player?.EnableInClassList("hand-label--win", result && shown.Outcome == Rules.BaccaratOutcome.Player);
            var banker = Place(view.BankerLabelPosition, $"Banker {shown.VisibleTotal(true)}", cam);
            banker?.EnableInClassList("hand-label--win", result && shown.Outcome == Rules.BaccaratOutcome.Banker);
            if (result && shown.Outcome == Rules.BaccaratOutcome.Tie)
            {
                player?.AddToClassList("hand-label--push");
                banker?.AddToClassList("hand-label--push");
            }
        }

        void LabelSlots(SlotMachine machine, Camera cam)
        {
            if (machine.IsSpinning)
                return;
            var payout = machine.LastPayout;
            var label = Place(machine.LabelPosition, payout > 0 ? $"{PlayerCat.NameOf(machine.SpinOwner)} won {payout:N0}" : $"{PlayerCat.NameOf(machine.SpinOwner)}: no luck", cam);
            label?.AddToClassList(payout > machine.Bet ? "hand-label--win" : "hand-label--push");
        }

        void LabelRoulette(RouletteTable table, Camera cam)
        {
            if (!_rouletteViews.TryGetValue(table, out var view) || view == null)
            {
                view = table.GetComponent<RouletteView>();
                _rouletteViews[table] = view;
            }

            if (view == null)
                return;

            var shown = table.Shown;
            var text = shown.Phase switch
            {
                RoulettePhase.Betting => $"Place your bets · {Mathf.CeilToInt(Mathf.Max(0f, shown.TimeRemaining))}s",
                RoulettePhase.Spinning => "No more bets!",
                RoulettePhase.Result => $"{shown.Result} {Rules.RouletteRules.ColorName(shown.Result)}",
                _ => null,
            };

            if (text == null)
                return;
            var label = Place(view.ResultLabelPosition, text, cam);
            if (label == null || shown.Phase != RoulettePhase.Result)
                return;
            label.AddToClassList(shown.Result == 0 ? "hand-label--win" : Rules.RouletteRules.IsRed(shown.Result) ? "hand-label--lose" : "hand-label--push");
        }

        Label Place(Vector3 world, string text, Camera cam)
        {
            if (Vector3.Dot(world - cam.transform.position, cam.transform.forward) <= 0f)
                return null;

            if (_used >= _labels.Count)
            {
                var created = new Label { pickingMode = PickingMode.Ignore };
                created.AddToClassList("hand-label");
                _layer.Add(created);
                _labels.Add(created);
            }

            var label = _labels[_used++];
            var position = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, world, cam);
            label.style.display = DisplayStyle.Flex;
            label.style.left = position.x;
            label.style.top = position.y;
            label.text = text;
            label.ClearClassList();
            label.AddToClassList("hand-label");
            return label;
        }

        void UpdateSpectator()
        {
            var local = PlayerCat.Local;
            PlayerCat watched = null;
            if (local != null && SeatingController.LocalTable == null)
            {
                var best = SpectateRange;
                foreach (var cat in PlayerCat.All)
                {
                    if (cat == null || cat == local || cat.Object == null || !cat.Object.IsValid || !cat.IsSeated)
                        continue;
                    var distance = Vector3.Distance(cat.transform.position, local.transform.position);
                    if (distance < best)
                    {
                        best = distance;
                        watched = cat;
                    }
                }
            }

            _text.Clear();
            var show = watched != null && GameTables.TryFindSeat(watched.Owner, out var table, out _) &&
                       table.TryDescribePlayer(watched.Owner, _text);
            _spectator.EnableInClassList("hidden", !show);
            if (!show)
                return;

            _spectatorName.text = watched.DisplayName.ToString();
            _spectatorName.style.color = Color.Lerp(watched.Swatch, Color.white, 0.35f);
            _spectatorBody.text = $"{_text}Balance {watched.Balance:N0}";
        }
    }
}
