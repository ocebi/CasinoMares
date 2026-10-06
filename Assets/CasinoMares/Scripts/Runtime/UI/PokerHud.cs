using CasinoMares.Audio;
using CasinoMares.Core;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Poker;
using CasinoMares.Rules;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Seated poker UI: your private hole cards and best hand, pot and price to call, Fold / Check-Call / Raise
    /// with sizing shortcuts, rebuy when busted, table settings, the rules and the optional coach (teaching mode).
    /// Keys: F fold, C check/call, R raise.
    /// </summary>
    public class PokerHud : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _phase;
        VisualElement _timer;
        VisualElement _timerFill;
        CardElement _card1;
        CardElement _card2;
        Label _bestHand;
        Label _info;
        VisualElement _actionPanel;
        Button _foldButton;
        Button _callButton;
        Button _raiseButton;
        Button _allInButton;
        Label _raiseLabel;
        Button _rebuyButton;
        VisualElement _settingsPanel;
        Button _blindButton;
        Button _timerButton;
        Button _npcButton;
        Button _coachButton;
        PokerTutor _tutor;

        PokerTable _table;
        PokerTable.Snapshot _shown;
        int _seat = -1;
        int _raiseTo;
        int _lastTurnHand = -1;
        PokerPhase _lastTurnPhase;
        bool _wasMyTurn;
        bool _coachSizedRaise;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("pk-root");
            _phase = ui.Q<Label>("phase-label");
            _timer = ui.Q("timer");
            _timerFill = ui.Q("timer-fill");
            _bestHand = ui.Q<Label>("best-hand");
            _info = ui.Q<Label>("info-label");
            _actionPanel = ui.Q("action-panel");
            _foldButton = ui.Q<Button>("fold-button");
            _callButton = ui.Q<Button>("call-button");
            _raiseButton = ui.Q<Button>("raise-button");
            _raiseLabel = ui.Q<Label>("raise-label");
            _rebuyButton = ui.Q<Button>("rebuy-button");
            _settingsPanel = ui.Q("settings-panel");
            _blindButton = ui.Q<Button>("blind-button");
            _timerButton = ui.Q<Button>("timer-button");
            _npcButton = ui.Q<Button>("npc-button");
            _coachButton = ui.Q<Button>("coach-button");
            _allInButton = ui.Q<Button>("allin-button");
            _tutor = new PokerTutor(_root);

            var cards = ui.Q("hole-cards");
            cards.Clear();
            _card1 = new CardElement();
            _card2 = new CardElement();
            cards.Add(_card1);
            cards.Add(_card2);

            _foldButton.clicked += () => Act(PokerAction.Fold);
            _callButton.clicked += () => Act(PokerAction.Call);
            _raiseButton.clicked += () => Act(PokerAction.Raise);
            ui.Q<Button>("minus-button").clicked += () => NudgeRaise(-1);
            ui.Q<Button>("plus-button").clicked += () => NudgeRaise(1);
            ui.Q<Button>("min-button").clicked += () => _raiseTo = _shown != null ? _shown.MinRaiseTo : 0;
            ui.Q<Button>("half-pot-button").clicked += () => SetRaiseToPotFraction(0.5f);
            ui.Q<Button>("pot-button").clicked += () => SetRaiseToPotFraction(1f);
            _allInButton.clicked += () => Act(PokerAction.AllIn);
            _rebuyButton.clicked += Rebuy;
            ui.Q<Button>("settings-button").clicked += () => _settingsPanel.ToggleInClassList("hidden");
            ui.Q<Button>("rules-button").clicked += () => _tutor.Panel.ShowRules(!_tutor.Panel.RulesOpen);
            _coachButton.clicked += () => _tutor.Panel.Enabled = !_tutor.Panel.Enabled;
            ui.Q<Button>("leave-button").clicked += () => _table?.RequestLeave(_seat);
            _blindButton.clicked += () => _table?.ChangeSettings((_table.BlindOption + 1) % PokerTable.SmallBlindOptions.Length, _table.TurnTimerOption, _table.NpcFill);
            _timerButton.clicked += () => _table?.ChangeSettings(_table.BlindOption, (_table.TurnTimerOption + 1) % PokerTable.TurnTimerOptions.Length, _table.NpcFill);
            _npcButton.clicked += () => _table?.ChangeSettings(_table.BlindOption, _table.TurnTimerOption, NpcSeating.NextOption(_table.NpcFill));

            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable()
        {
            SeatingController.LocalSeatChanged -= OnLocalSeatChanged;
        }

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            _table = table as PokerTable;
            _shown = null;
            _seat = _table != null ? seat : -1;
            _root.EnableInClassList("hidden", _table == null);
            _settingsPanel.AddToClassList("hidden");
            _tutor.Panel.SetSeated(_table != null);
            if (_table != null && !_tutor.Panel.Enabled && PlayerPrefs.GetInt("Coach.Poker.Hinted", 0) == 0)
            {
                PlayerPrefs.SetInt("Coach.Poker.Hinted", 1);
                PlayerPrefs.Save();
                Toasts.Show($"New to poker? {(Platform.IsTouch ? "Tap" : "Click")} Rules, or turn on the coach in Settings for live tips.");
            }
        }

        void Update()
        {
            if (_table == null || _table.Object == null || !_table.Object.IsValid)
                return;

            // The hand as everyone sees it at this moment (see PokerTable.Shown), so the buttons appear with the cards.
            _shown = _table.Shown;
            var phase = _shown.Phase;
            var myTurn = _shown.IsActiveTurn(_seat);
            var toCall = _shown.ToCall(_seat);
            var stack = _shown.StackAt(_seat);

            // Hole cards and best hand.
            var haveCards = _shown.IsInHand(_seat) && _shown.TryGetMyHoleCards(out var a, out var b);
            _card1.style.display = _card2.style.display = haveCards ? DisplayStyle.Flex : DisplayStyle.None;
            if (haveCards)
            {
                _shown.TryGetMyHoleCards(out a, out b);
                _card1.SetCard(a);
                _card2.SetCard(b);
                _bestHand.text = BestHand(a, b);
            }
            else
            {
                _bestHand.text = (_shown.FlagsAt(_seat) & PokerSeatFlags.Folded) != 0 ? "Folded" : "";
            }

            _info.text = $"Stack {stack:N0}  ·  Pot {_shown.TotalPot:N0}" + (toCall > 0 && myTurn ? $"  ·  {toCall:N0} to call" : "") +
                         $"  ·  Blinds {_table.SmallBlind}/{_table.BigBlind}";

            _actionPanel.EnableInClassList("hidden", !myTurn);
            if (myTurn)
            {
                if (!_wasMyTurn || _lastTurnHand != _shown.HandId || _lastTurnPhase != phase)
                {
                    _raiseTo = _shown.MinRaiseTo;
                    _coachSizedRaise = false;
                    _lastTurnHand = _shown.HandId;
                    _lastTurnPhase = phase;
                    Sfx.Play(SoundId.Notify, 0.7f);
                }

                var maxTo = _shown.RoundBetAt(_seat) + stack;
                _tutor.Update(_shown, _seat);
                if (!_coachSizedRaise && _tutor.Suggestion is PokerAdvice.Raise)
                {
                    // Start the raise slider at the coach's size once; the player can still change it.
                    _coachSizedRaise = true;
                    _raiseTo = _tutor.SuggestedRaiseTo;
                }

                _raiseTo = Mathf.Clamp(_raiseTo, Mathf.Min(_shown.MinRaiseTo, maxTo), maxTo);
                _callButton.text = (toCall == 0 ? "Check" : toCall >= stack ? $"Call all in {stack:N0}" : $"Call {toCall:N0}") + KeyHints.For("C");
                _raiseButton.text = (_shown.CurrentBet == 0 ? $"Bet {_raiseTo:N0}" : $"Raise to {_raiseTo:N0}") + KeyHints.For("R");
                _raiseButton.SetEnabled(maxTo > _shown.CurrentBet);
                _raiseLabel.text = _raiseTo.ToString("N0");
                HandleKeys(toCall);
            }

            _wasMyTurn = myTurn;
            if (!myTurn)
                _tutor.Update(_shown, _seat);
            HighlightSuggestion(myTurn ? _tutor.Suggestion : null);
            _rebuyButton.EnableInClassList("hidden", stack >= _table.BigBlind || (_shown.IsInHand(_seat) && _shown.IsBettingPhase));

            _phase.text = phase switch
            {
                PokerPhase.WaitingForPlayers => "Waiting for the next hand...",
                PokerPhase.Showdown => _shown.ShowdownSettled ? _shown.LastResult.ToString() : "Showdown!",
                _ when myTurn => $"Your turn ({StreetName(phase)})",
                _ when _shown.ActiveSeat >= 0 => $"{SeatName(_shown.ActiveSeat)} is thinking... ({StreetName(phase)})",
                _ => StreetName(phase),
            };

            var remaining = _shown.TimeRemaining;
            var showTimer = _shown.IsBettingPhase && _shown.ActiveSeat >= 0 && remaining >= 0f;
            _timer.EnableInClassList("hidden", !showTimer);
            if (showTimer)
                _timerFill.style.width = Length.Percent(Mathf.Min(100f, 100f * remaining / Mathf.Max(1f, _table.TurnTimerSeconds)));

            _blindButton.text = $"Blinds: {_table.SmallBlind}/{_table.BigBlind}";
            _timerButton.text = $"Turn timer: {_table.TurnTimerSeconds}s";
            _npcButton.text = $"NPC players: {NpcSeating.Describe(_table.NpcFill)}";
            _coachButton.text = $"Coach (just for you): {(_tutor.Panel.Enabled ? "on" : "off")}";
        }

        void HighlightSuggestion(PokerAdvice? advice)
        {
            _foldButton.EnableInClassList("coach-suggested", advice == PokerAdvice.Fold);
            _callButton.EnableInClassList("coach-suggested", advice is PokerAdvice.Check or PokerAdvice.Call);
            _raiseButton.EnableInClassList("coach-suggested", advice == PokerAdvice.Raise);
            _allInButton.EnableInClassList("coach-suggested", advice == PokerAdvice.AllIn);
        }

        string BestHand(byte a, byte b)
        {
            var count = _shown.CommunityCount;
            if (count < 3)
                return Cards.Rank(a) == Cards.Rank(b) ? "Pocket pair" : Cards.Suit(a) == Cards.Suit(b) ? "Suited" : "";
            var cards = new byte[2 + count];
            cards[0] = a;
            cards[1] = b;
            for (var i = 0; i < count; i++)
                cards[2 + i] = _shown.CommunityAt(i);
            return PokerRules.Evaluate(cards).ToString();
        }

        static string StreetName(PokerPhase phase) => phase switch
        {
            PokerPhase.PreFlop => "pre-flop",
            PokerPhase.Flop => "flop",
            PokerPhase.Turn => "turn",
            PokerPhase.River => "river",
            _ => "",
        };

        string SeatName(int seat) =>
            _table.OccupantAt(seat) == PokerOccupant.Player ? PlayerCat.NameOf(_table.PlayerAt(seat)) : $"Seat {seat + 1} (NPC)";

        void HandleKeys(int toCall)
        {
            if (GameInput.KeyPressed(Key.F))
                Act(PokerAction.Fold);
            else if (GameInput.KeyPressed(Key.C))
                Act(PokerAction.Call);
            else if (GameInput.KeyPressed(Key.R))
                Act(PokerAction.Raise);
        }

        void NudgeRaise(int direction)
        {
            if (_table != null)
                _raiseTo += direction * _table.BigBlind;
        }

        void SetRaiseToPotFraction(float fraction)
        {
            if (_table == null || _shown == null)
                return;
            var toCall = _shown.ToCall(_seat);
            // Pot-sized raise: call first, then raise by (the pot after calling) × fraction.
            _raiseTo = _shown.CurrentBet + Mathf.RoundToInt((_shown.TotalPot + toCall) * fraction);
        }

        void Act(PokerAction action)
        {
            if (_table == null || _shown == null || !_shown.IsActiveTurn(_seat))
                return;
            _table.Act(_seat, action, _raiseTo);
        }

        void Rebuy()
        {
            if (_table == null)
                return;
            var amount = Mathf.Min(PokerSeat.DefaultBuyIn, ProfileStore.Current.Balance);
            if (amount < PokerTable.MinBuyIn)
            {
                Toasts.Show($"You need at least {PokerTable.MinBuyIn} chips to rebuy. Try the cashier.");
                return;
            }

            _table.Rebuy(_seat, amount);
        }
    }
}
