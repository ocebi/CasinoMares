using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Blackjack
{
    /// <summary>
    /// Renders a <see cref="BlackjackTable"/> on every client from its shown round (<see cref="BlackjackTable.Shown"/>,
    /// GDD §5.5): floating cards and chips, NPC seat cats and the dealer cat. New cards fly in from the shoe; a client
    /// that just arrived sees the current layout immediately without replaying animations.
    /// </summary>
    public class BlackjackTableView : MonoBehaviour
    {
        const int DealerKeyBase = 100000;

        [SerializeField] BlackjackTable _table;
        /// <summary>
        /// How far each later card in a hand sits right of and above (toward the dealer) the one before, in metres: wide
        /// enough to leave the rank corner of every card, "10" included, readable from the seat and overhead views.
        /// </summary>
        public static readonly Vector2 CardFan = new(0.08f, 0.02f);

        [SerializeField] Material _cardMaterial;
        [SerializeField] Material _chipMaterial;
        [SerializeField] Mesh _chipMesh;
        [SerializeField] Transform _shoe;
        [SerializeField] Transform _discard;
        [SerializeField] Transform _dealerCards;
        [SerializeField] Transform[] _handAnchors = new Transform[BlackjackTable.SeatCount];
        [SerializeField] NpcCatVisual _npcCatPrefab;
        [SerializeField] NpcCatVisual _dealer;

        readonly Dictionary<int, CardVisual> _cards = new();
        readonly HashSet<int> _seen = new();
        readonly List<int> _stale = new();
        readonly ChipStack[] _betStacks = new ChipStack[BlackjackTable.SeatCount * BlackjackTable.MaxHands];
        SeatCats _seatCats;
        BlackjackTable.Snapshot _shown;
        bool _initialized;
        int _lastShoeSeed;

        public BlackjackTable Table => _table;

        void Awake()
        {
            // The atlas is generated at runtime, so give it to a copy; assigning it to the material asset would
            // dirty the asset with a texture that doesn't exist on disk.
            _cardMaterial = new Material(_cardMaterial) { name = "Card (runtime)" };
            _cardMaterial.SetTexture("_BaseMap", CardFaces.Atlas);
            _seatCats = new SeatCats(_npcCatPrefab, transform, BlackjackTable.SeatCount, _table.GetSeatAnchor, _table);
        }

        void OnEnable()
        {
            _table.RoundSettled += OnRoundSettled;
        }

        void OnDisable()
        {
            _table.RoundSettled -= OnRoundSettled;
        }

        void LateUpdate()
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return;

            // First frame after joining: place everything where it is, no fly-ins.
            var instant = !_initialized;
            _initialized = true;

            if (!instant && _table.ShoeSeedValue != _lastShoeSeed)
                Sfx.PlayAt(SoundId.Shuffle, _shoe.position);
            _lastShoeSeed = _table.ShoeSeedValue;

            _shown = _table.Shown;
            _seen.Clear();
            UpdateHands(instant);
            UpdateDealer(instant);
            ReleaseStaleCards();
            UpdateSeatNpcs();
            UpdateDealerCat();
        }

        // ---------------------------------------------------------------- Layout

        public Vector3 HandCenter(int seat, int hand, int handCount)
        {
            var anchor = _handAnchors[seat];
            // Room for a fanned hand of three cards before the next split hand starts.
            var offset = (hand - (handCount - 1) * 0.5f) * (CardFaces.CardSize.x + 2f * CardFan.x + 0.06f);
            return anchor.position + anchor.right * offset;
        }

        public Vector3 HandLabelPosition(int seat, int hand, int handCount) =>
            HandCenter(seat, hand, handCount) + _handAnchors[seat].forward * 0.28f + Vector3.up * 0.05f;

        public Vector3 DealerLabelPosition => _dealerCards.position + _dealerCards.forward * -0.25f + Vector3.up * 0.05f;

        Vector3 CardPosition(int seat, int hand, int handCount, int index)
        {
            var anchor = _handAnchors[seat];
            return Fan(HandCenter(seat, hand, handCount), anchor, index);
        }

        Vector3 DealerCardPosition(int index) => Fan(_dealerCards.position, _dealerCards, index);

        /// <summary>Each card sits a little right of and above the one before, clear of its rank corner.</summary>
        static Vector3 Fan(Vector3 start, Transform anchor, int index) =>
            start + anchor.right * (index * CardFan.x) + anchor.forward * (index * CardFan.y) + Vector3.up * (0.002f + index * 0.0015f);

        // ---------------------------------------------------------------- Cards & chips

        void UpdateHands(bool instant)
        {
            for (var seat = 0; seat < BlackjackTable.SeatCount; seat++)
            {
                var handCount = _shown.HandCountAt(seat);
                var anchor = _handAnchors[seat];

                for (var hand = 0; hand < BlackjackTable.MaxHands; hand++)
                {
                    var stack = _betStacks[seat * BlackjackTable.MaxHands + hand];

                    if (hand >= handCount)
                    {
                        // During betting, show the chips a player has put up on their first spot.
                        var pending = hand == 0 && _shown.Phase == BlackjackPhase.Betting && _table.OccupantAt(seat) != SeatOccupant.Empty
                            ? _table.PendingBetAt(seat)
                            : 0;
                        SetStack(ref stack, seat, hand, pending, ChipSpot(seat, 0, 1), instant);
                        _betStacks[seat * BlackjackTable.MaxHands + hand] = stack;
                        continue;
                    }

                    var cardCount = _shown.CardCountAt(seat, hand);
                    for (var i = 0; i < cardCount; i++)
                    {
                        var key = ((seat * BlackjackTable.MaxHands) + hand) * BlackjackTable.MaxCards + i;
                        PlaceCard(key, _shown.CardAt(seat, hand, i), CardPosition(seat, hand, handCount, i), anchor.rotation, true, instant);
                    }

                    // Settled hands show what was paid out; lost chips vanish to the dealer.
                    var amount = _shown.HandBetAt(seat, hand);
                    var outcome = _shown.OutcomeAt(seat, hand);
                    if (outcome != HandOutcome.None)
                        amount = BlackjackRules.Payout(outcome, amount);
                    SetStack(ref stack, seat, hand, amount, ChipSpot(seat, hand, handCount), instant);
                    _betStacks[seat * BlackjackTable.MaxHands + hand] = stack;
                }
            }
        }

        Vector3 ChipSpot(int seat, int hand, int handCount) =>
            HandCenter(seat, hand, handCount) - _handAnchors[seat].forward * 0.17f;

        void SetStack(ref ChipStack stack, int seat, int hand, int amount, Vector3 position, bool instant)
        {
            if (amount <= 0)
            {
                if (stack != null && stack.gameObject.activeSelf)
                {
                    stack.MoveTo(_dealerCards.position, false);
                    stack.SetAmount(0);
                    stack.gameObject.SetActive(false);
                }

                return;
            }

            if (stack == null)
            {
                stack = ChipStack.Create(transform, _chipMesh, _chipMaterial);
                stack.transform.position = _dealerCards.position;
            }

            if (!stack.gameObject.activeSelf)
            {
                stack.gameObject.SetActive(true);
                // New bets float in from in front of the seat.
                stack.transform.position = position - _handAnchors[seat].forward * 0.35f;
            }

            if (!instant && amount != stack.Amount)
                Sfx.PlayAt(amount > stack.Amount && _shown.Phase == BlackjackPhase.Settlement ? SoundId.ChipPayout : SoundId.ChipBet, position, 0.7f);
            stack.SetAmount(amount);
            stack.MoveTo(position, instant);
        }

        void UpdateDealer(bool instant)
        {
            var count = _shown.DealerCardCount;
            for (var i = 0; i < count; i++)
            {
                var faceUp = i != 1 || _shown.HoleRevealed;
                PlaceCard(DealerKeyBase + i, _shown.DealerCardAt(i), DealerCardPosition(i), _dealerCards.rotation, faceUp, instant);
            }
        }

        void PlaceCard(int key, byte card, Vector3 position, Quaternion rotation, bool faceUp, bool instant)
        {
            _seen.Add(key);
            if (!_cards.TryGetValue(key, out var visual) || visual == null)
            {
                visual = CardVisual.Create(transform, _cardMaterial);
                visual.transform.SetPositionAndRotation(_shoe.position, _shoe.rotation * Quaternion.Euler(0f, 0f, 180f));
                visual.SetCard(card);
                visual.FlyTo(position, rotation, faceUp, instant);
                _cards[key] = visual;
                return;
            }

            if (visual.Card != card)
            {
                // Happens after a split: the slot now holds a different card. Re-deal it into place.
                visual.SetCard(card);
                visual.FlyTo(position, rotation, faceUp, instant);
                return;
            }

            visual.Retarget(position, rotation, faceUp);
        }

        void ReleaseStaleCards()
        {
            _stale.Clear();
            foreach (var pair in _cards)
            {
                if (!_seen.Contains(pair.Key))
                    _stale.Add(pair.Key);
            }

            foreach (var key in _stale)
            {
                var visual = _cards[key];
                _cards.Remove(key);
                if (visual != null)
                    visual.FlyAwayAndRelease(_discard.position, _discard.rotation);
            }
        }

        // ---------------------------------------------------------------- Cats

        void UpdateSeatNpcs()
        {
            for (var seat = 0; seat < BlackjackTable.SeatCount; seat++)
                _seatCats.UpdateSeat(seat, _table.OccupantAt(seat) == SeatOccupant.Npc, _table.NpcFurAt(seat), _table.NpcArrivalAt(seat), _table.NpcScaresAt(seat));
            _seatCats.Tick();
        }

        void UpdateDealerCat()
        {
            if (_dealer == null)
                return;

            _dealer.Sitting = true;
            var active = _shown.ActiveSeat;
            var lookAt = active >= 0 ? _table.GetSeatAnchor(active).position : transform.position;
            _dealer.FaceTowards(lookAt, 120f);
        }

        void OnRoundSettled()
        {
            var shown = _table.Shown;
            var dealerWon = false;
            for (var seat = 0; seat < BlackjackTable.SeatCount; seat++)
            {
                var hands = shown.HandCountAt(seat);
                if (hands == 0)
                    continue;

                var best = HandOutcome.Lose;
                var net = 0;
                for (var h = 0; h < hands; h++)
                {
                    var outcome = shown.OutcomeAt(seat, h);
                    net += BlackjackRules.Payout(outcome, shown.HandBetAt(seat, h)) - shown.HandBetAt(seat, h);
                    if (outcome is HandOutcome.Blackjack or HandOutcome.Win)
                        best = outcome == HandOutcome.Blackjack || best == HandOutcome.Blackjack ? HandOutcome.Blackjack : HandOutcome.Win;
                }

                var reactions = ReactionsFor(shown, seat);
                if (reactions == null)
                    continue;

                if (net > 0)
                    reactions.PlayHappy(best == HandOutcome.Blackjack ? 3 : 1, CatReactions.StaggerDelay());
                else if (net < 0)
                {
                    reactions.PlayAngry(CatReactions.StaggerDelay());
                    dealerWon = true;
                }
            }

            if (dealerWon && _dealer != null && _dealer.Reactions != null)
                _dealer.Reactions.PlayHappy(1, CatReactions.StaggerDelay() + 0.3f);
        }

        CatReactions ReactionsFor(BlackjackTable.Snapshot shown, int seat)
        {
            var owner = shown.RoundOwnerAt(seat);
            if (owner != Fusion.PlayerRef.None)
            {
                var cat = PlayerCat.Find(owner);
                return cat != null ? cat.Reactions : null;
            }

            return _seatCats.ReactionsAt(seat);
        }
    }
}
