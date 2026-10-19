using System;
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
    /// each table's shown round so the labels change together with the cards. The labels are placed every frame, so
    /// their text comes from a cache of strings built once per distinct value (<see cref="Cached{T}"/>) and only the
    /// style classes that changed are touched: on a phone, formatting a dozen strings a frame was garbage every frame.
    /// Each table's labels are then pushed apart where they overlap and kept on screen (<see cref="Separate"/>).
    /// </summary>
    public class TableOverlay : MonoBehaviour
    {
        const float LabelRange = 8f;
        const float SpectateRange = 3f;
        /// <summary>The drunk player's own hand labels rock gently (UX §1.2): degrees and Hz.</summary>
        const float TipsyWobbleDegrees = 3f;
        const float TipsyWobbleHz = 0.25f;
        /// <summary>Pots and payouts make new entries; past this many the cache starts over.</summary>
        const int MaxCachedStrings = 512;

        /// <summary>Which label a cached string is for; part of its key.</summary>
        enum Text : byte { Hand, Dealer, Pot, Won, Action, Player, Banker, Slots, Bets, Result, Fuzzy }

        static readonly string[] Modifiers = { "hand-label--active", "hand-label--win", "hand-label--lose", "hand-label--push", "hand-label--tipsy" };

        /// <summary>Pixels kept between labels that were pushed apart, and from the screen's edges.</summary>
        const float LabelGap = 3f;

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
        readonly Dictionary<long, string> _strings = new();
        int _used;

        static long Key(Text kind, int a, int b = 0, int c = 0) =>
            ((long)kind << 56) | ((long)(uint)a << 24) | ((long)(ushort)b << 8) | (byte)c;

        /// <summary>The string for <paramref name="key"/>, made from <paramref name="state"/> the first time only.</summary>
        string Cached<T>(long key, T state, Func<T, string> make)
        {
            if (_strings.TryGetValue(key, out var text))
                return text;
            if (_strings.Count >= MaxCachedStrings)
                _strings.Clear();
            text = make(state);
            _strings.Add(key, text);
            return text;
        }

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
                    var first = _used;
                    LabelTable(table, cam);
                    Separate(first);
                }

                foreach (var poker in PokerTable.All)
                {
                    if (poker == null || poker.Object == null || !poker.Object.IsValid)
                        continue;
                    if ((poker.transform.position - cam.transform.position).sqrMagnitude > LabelRange * LabelRange)
                        continue;
                    var first = _used;
                    LabelPoker(poker, cam);
                    Separate(first);
                }

                foreach (var baccarat in BaccaratTable.All)
                {
                    if (baccarat == null || baccarat.Object == null || !baccarat.Object.IsValid || baccarat.Shown.Revealed == 0)
                        continue;
                    if ((baccarat.transform.position - cam.transform.position).sqrMagnitude > LabelRange * LabelRange)
                        continue;
                    var first = _used;
                    LabelBaccarat(baccarat, cam);
                    Separate(first);
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
            // The dealer's label first: where labels overlap, the later ones (the hands) give way to it.
            if (shown.DealerCardCount > 0)
            {
                var dealerValue = shown.EvaluateDealerVisible();
                var dealer = Place(view.DealerLabelPosition,
                    Cached(Key(Text.Dealer, dealerValue.Total, 0, dealerValue.IsSoft ? 1 : 0), dealerValue, static v => $"Dealer {v}"), cam);
                dealer?.EnableInClassList("hand-label--active", shown.Phase == BlackjackPhase.DealerTurn);
            }

            // A drunk player can't quite read its own cards (GDD §5.8): its unsettled hands show two totals.
            var local = PlayerCat.Local;
            var tipsy = local != null && local.Object != null && local.Object.IsValid && local.IsDrunk;
            var wobble = tipsy && CameraRig.TipsyEffect.VisualsEnabled;
            for (var seat = 0; seat < BlackjackTable.SeatCount; seat++)
            {
                var hands = shown.HandCountAt(seat);
                var mySeat = tipsy && shown.RoundOwnerAt(seat) == local.Owner;
                for (var hand = 0; hand < hands; hand++)
                {
                    var cards = shown.CardCountAt(seat, hand);
                    if (cards == 0)
                        continue;

                    var outcome = shown.OutcomeAt(seat, hand);
                    var value = shown.EvaluateHand(seat, hand);
                    var fuzzy = mySeat && outcome == HandOutcome.None && !value.IsBust;
                    string text;
                    if (fuzzy)
                    {
                        var (first, second) = DrunkTotals.Candidates(value.Total, DrunkTotals.Hash(table.Object.Id.Raw, shown.RoundId, seat, hand, cards));
                        text = Cached(Key(Text.Fuzzy, first, second), (first, second), static s => $"{s.first} or {s.second}?");
                    }
                    else
                    {
                        text = outcome == HandOutcome.None
                            ? value.IsBust ? "Bust" : Cached(Key(Text.Hand, value.Total, 0, value.IsSoft ? 1 : 0), value, static v => v.ToString())
                            : Cached(Key(Text.Hand, value.Total, (int)outcome + 1), (value.Total, outcome),
                                static s => $"{s.Total} · {BlackjackTable.OutcomeLabel(s.outcome)}");
                    }

                    var label = Place(view.HandLabelPosition(seat, hand, hands), text, cam);
                    if (label == null)
                        continue;
                    label.EnableInClassList("hand-label--tipsy", fuzzy);
                    if (fuzzy && wobble)
                        label.style.rotate = new Rotate(TipsyWobbleDegrees * Mathf.Sin(2f * Mathf.PI * TipsyWobbleHz * Time.time + seat * 1.7f + hand * 0.9f));
                    label.EnableInClassList("hand-label--active", shown.IsActiveTurn(seat) && shown.ActiveHand == hand);
                    label.EnableInClassList("hand-label--win", outcome is HandOutcome.Win or HandOutcome.Blackjack);
                    label.EnableInClassList("hand-label--lose", outcome is HandOutcome.Lose or HandOutcome.Bust || (outcome == HandOutcome.None && value.IsBust));
                    label.EnableInClassList("hand-label--push", outcome == HandOutcome.Push);
                }
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
                Place(view.PotLabelPosition, Cached(Key(Text.Pot, shown.TotalPot), shown.TotalPot, static pot => $"Pot {pot:N0}"), cam);

            for (var seat = 0; seat < PokerTable.SeatCount; seat++)
            {
                if (table.OccupantAt(seat) == PokerOccupant.Empty || (shown.FlagsAt(seat) & PokerSeatFlags.InHand) == 0)
                    continue;

                string text;
                if (shown.Phase == PokerPhase.Showdown && shown.WonAt(seat) > 0)
                    text = Cached(Key(Text.Won, shown.WonAt(seat)), shown.WonAt(seat), static won => $"+{won:N0}");
                else if ((shown.FlagsAt(seat) & PokerSeatFlags.Folded) != 0)
                    text = "Fold";
                else if ((shown.FlagsAt(seat) & PokerSeatFlags.AllIn) != 0)
                    text = "All in";
                else
                    text = Cached(Key(Text.Action, shown.LastAmountAt(seat), (int)shown.LastActionAt(seat)), (shown.LastActionAt(seat), shown.LastAmountAt(seat)),
                        static s => PokerTable.ActionLabel(s.Item1, s.Item2));

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
            var player = Place(view.PlayerLabelPosition, Cached(Key(Text.Player, shown.VisibleTotal(false)), shown.VisibleTotal(false), static t => $"Player {t}"), cam);
            player?.EnableInClassList("hand-label--win", result && shown.Outcome == Rules.BaccaratOutcome.Player);
            var banker = Place(view.BankerLabelPosition, Cached(Key(Text.Banker, shown.VisibleTotal(true)), shown.VisibleTotal(true), static t => $"Banker {t}"), cam);
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
            // Keyed on the spin too, so a new spin by a cat of the same number (someone who left) gets its own name.
            var text = Cached(Key(Text.Slots, payout, machine.SpinOwner.PlayerId, machine.SpinId), (machine.SpinOwner, payout),
                static s => s.payout > 0 ? $"{PlayerCat.NameOf(s.SpinOwner)} won {s.payout:N0}" : $"{PlayerCat.NameOf(s.SpinOwner)}: no luck");
            var label = Place(machine.LabelPosition, text, cam);
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
            var seconds = Mathf.CeilToInt(Mathf.Max(0f, shown.TimeRemaining));
            var text = shown.Phase switch
            {
                RoulettePhase.Betting => Cached(Key(Text.Bets, seconds), seconds, static s => $"Place your bets · {s}s"),
                RoulettePhase.Spinning => "No more bets!",
                RoulettePhase.Result => Cached(Key(Text.Result, shown.Result), shown.Result, static r => $"{r} {Rules.RouletteRules.ColorName(r)}"),
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
            // Back to a plain, upright label; the caller adds its modifiers (and the tipsy wobble). Only a class that
            // was there is a style change, and an unchanged inline rotate is skipped by the style accessor.
            label.style.rotate = new Rotate(0f);
            foreach (var modifier in Modifiers)
                label.EnableInClassList(modifier, false);
            return label;
        }

        /// <summary>
        /// Pushes apart the labels placed since <paramref name="first"/> (one table's) where they overlap: each later
        /// label moves down the screen, toward the players, below the earlier one it hits, so a hand's label gives
        /// way to the dealer's. Then all of them are kept on screen. Sizes are from the last layout, so a label's
        /// first frame isn't moved.
        /// </summary>
        void Separate(int first)
        {
            var screen = _layer.layout.size;
            for (var i = first; i < _used; i++)
            {
                var label = _labels[i];
                var size = label.layout.size;
                if (float.IsNaN(size.x) || size.x <= 0f)
                    continue;

                // style left/top is the label's bottom centre (the USS translates it up and left).
                var x = label.style.left.value.value;
                var y = label.style.top.value.value;
                for (var j = first; j < i; j++)
                {
                    var other = _labels[j];
                    var otherSize = other.layout.size;
                    if (float.IsNaN(otherSize.x) || otherSize.x <= 0f)
                        continue;
                    var otherX = other.style.left.value.value;
                    var otherY = other.style.top.value.value;
                    var apartX = Mathf.Abs(x - otherX) >= (size.x + otherSize.x) / 2f + LabelGap;
                    var apartY = y <= otherY - otherSize.y - LabelGap || y - size.y >= otherY + LabelGap;
                    if (!apartX && !apartY)
                        y = otherY + size.y + LabelGap;
                }

                if (!float.IsNaN(screen.x) && screen.x > 0f)
                {
                    x = Mathf.Clamp(x, size.x / 2f + LabelGap, Mathf.Max(size.x / 2f + LabelGap, screen.x - size.x / 2f - LabelGap));
                    y = Mathf.Clamp(y, size.y + LabelGap, Mathf.Max(size.y + LabelGap, screen.y - LabelGap));
                }

                label.style.left = x;
                label.style.top = y;
            }
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
