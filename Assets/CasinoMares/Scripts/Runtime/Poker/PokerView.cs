using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Poker
{
    /// <summary>
    /// Renders a poker table from its shown hand (<see cref="PokerTable.Shown"/>): community cards, two cards in front
    /// of each player still in the hand (face down for everyone else, face up for their owner, and for all at
    /// showdown), stacks, bets, the pot and the button.
    /// </summary>
    public class PokerView : MonoBehaviour
    {
        const int CommunityKey = 1000;

        [SerializeField] PokerTable _table;
        [SerializeField] Material _cardMaterial;
        [SerializeField] Material _chipMaterial;
        [SerializeField] Mesh _chipMesh;
        [SerializeField] Transform _deck;
        [SerializeField] Transform _muck;
        [SerializeField] Transform _board;
        [SerializeField] Transform _pot;
        [SerializeField] Transform _button;
        [SerializeField] Transform[] _cardSpots = new Transform[PokerTable.SeatCount];
        [SerializeField] Transform[] _stackSpots = new Transform[PokerTable.SeatCount];
        [SerializeField] Transform[] _betSpots = new Transform[PokerTable.SeatCount];
        [SerializeField] NpcCatVisual _npcCatPrefab;
        [SerializeField] NpcCatVisual _dealer;

        readonly Dictionary<int, CardVisual> _cards = new();
        readonly HashSet<int> _seen = new();
        readonly List<int> _stale = new();
        readonly ChipStack[] _stacks = new ChipStack[PokerTable.SeatCount];
        readonly ChipStack[] _bets = new ChipStack[PokerTable.SeatCount];
        SeatCats _seatCats;
        PokerTable.Snapshot _shown;
        ChipStack _potStack;
        bool _initialized;
        int _lastHand = -1;
        int _reactedHand = -1;

        public Vector3 PotLabelPosition => _pot.position + Vector3.up * 0.12f;
        public Vector3 SeatLabelPosition(int seat) => _cardSpots[seat].position + Vector3.up * 0.1f;

        void Awake()
        {
            _cardMaterial = new Material(_cardMaterial) { name = "Card (runtime)" };
            _cardMaterial.SetTexture("_BaseMap", CardFaces.Atlas);
            _seatCats = new SeatCats(_npcCatPrefab, transform, PokerTable.SeatCount, _table.GetSeatAnchor, _table);
        }

        void LateUpdate()
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return;

            _shown = _table.Shown;
            var instant = !_initialized;
            _initialized = true;
            var phase = _shown.Phase;
            if (_shown.HandId != _lastHand && phase != PokerPhase.WaitingForPlayers)
            {
                _lastHand = _shown.HandId;
                if (!instant)
                    Sfx.PlayAt(SoundId.Shuffle, _deck.position, 0.7f);
            }

            // React once the pot is awarded, not when the cards are first turned over.
            var settled = phase == PokerPhase.Showdown && _shown.ShowdownSettled;
            if (settled && _reactedHand != _shown.HandId && !instant)
                React();
            if (settled || instant)
                _reactedHand = _shown.HandId;

            _seen.Clear();
            var dealt = phase != PokerPhase.WaitingForPlayers;
            if (dealt)
            {
                for (var i = 0; i < _shown.CommunityCount; i++)
                {
                    var position = _board.position + _board.right * ((i - 2) * (CardFaces.CardSize.x + 0.03f)) + Vector3.up * 0.002f;
                    Place(CommunityKey + i, _shown.CommunityAt(i), position, _board.rotation, true, instant);
                }

                for (var seat = 0; seat < PokerTable.SeatCount; seat++)
                    PlaceHoleCards(seat, instant);
            }

            ReleaseStale();
            UpdateChips(instant);
            UpdateButton();
            UpdateCats();
        }

        void PlaceHoleCards(int seat, bool instant)
        {
            if (!_shown.IsInHand(seat))
                return;

            var spot = _cardSpots[seat];
            byte first = Cards.None;
            byte second = Cards.None;
            var faceUp = false;

            if (_shown.ShownCardAt(seat, 0) != Cards.None)
            {
                first = _shown.ShownCardAt(seat, 0);
                second = _shown.ShownCardAt(seat, 1);
                faceUp = true;
            }
            else if (_table.PlayerAt(seat) == _table.Runner.LocalPlayer && _shown.TryGetMyHoleCards(out var a, out var b))
            {
                first = a;
                second = b;
                faceUp = true;
            }

            for (var i = 0; i < 2; i++)
            {
                var card = i == 0 ? first : second;
                // Face-down cards still need some face mesh; the ace of spades is never visible from above.
                var shown = faceUp ? card : (byte)0;
                var position = spot.position + spot.right * ((i - 0.5f) * 0.12f) + Vector3.up * (0.002f + i * 0.002f);
                var rotation = spot.rotation * Quaternion.Euler(0f, (i - 0.5f) * 10f, 0f);
                Place(seat * 2 + i, shown, position, rotation, faceUp, instant);
            }
        }

        void Place(int key, byte card, Vector3 position, Quaternion rotation, bool faceUp, bool instant)
        {
            _seen.Add(key);
            if (_cards.TryGetValue(key, out var visual) && visual != null)
            {
                if (visual.Card != card)
                    visual.SetCard(card);
                visual.Retarget(position, rotation, faceUp);
                return;
            }

            visual = CardVisual.Create(transform, _cardMaterial);
            visual.transform.SetPositionAndRotation(_deck.position, _deck.rotation * Quaternion.Euler(0f, 0f, 180f));
            visual.SetCard(card);
            visual.FlyTo(position, rotation, faceUp, instant);
            _cards[key] = visual;
        }

        void ReleaseStale()
        {
            _stale.Clear();
            foreach (var pair in _cards)
            {
                if (!_seen.Contains(pair.Key))
                    _stale.Add(pair.Key);
            }

            foreach (var key in _stale)
            {
                if (_cards[key] != null)
                    _cards[key].FlyAwayAndRelease(_muck.position, _muck.rotation);
                _cards.Remove(key);
            }
        }

        void UpdateChips(bool instant)
        {
            for (var seat = 0; seat < PokerTable.SeatCount; seat++)
            {
                var occupied = _table.OccupantAt(seat) != PokerOccupant.Empty;
                SetStack(ref _stacks[seat], occupied ? _shown.StackAt(seat) : 0, _stackSpots[seat].position, instant, false);
                SetStack(ref _bets[seat], _shown.RoundBetAt(seat), _betSpots[seat].position, instant, true);
            }

            SetStack(ref _potStack, _shown.Phase == PokerPhase.Showdown && _shown.ShowdownSettled ? 0 : _shown.PotInMiddle, _pot.position, instant, false);
        }

        void SetStack(ref ChipStack stack, int amount, Vector3 position, bool instant, bool sound)
        {
            if (amount <= 0)
            {
                if (stack != null)
                    stack.gameObject.SetActive(false);
                return;
            }

            if (stack == null)
            {
                stack = ChipStack.Create(transform, _chipMesh, _chipMaterial);
                stack.transform.position = position;
            }

            if (!stack.gameObject.activeSelf)
                stack.gameObject.SetActive(true);
            if (sound && !instant && stack.Amount >= 0 && amount > stack.Amount)
                Sfx.PlayAt(SoundId.ChipBet, position, 0.7f);
            stack.SetAmount(amount);
            stack.MoveTo(position, instant);
        }

        void UpdateButton()
        {
            var seat = _shown.ButtonSeat;
            _button.gameObject.SetActive(seat >= 0 && _shown.Phase != PokerPhase.WaitingForPlayers);
            if (seat >= 0)
                _button.position = Vector3.Lerp(_button.position, _betSpots[seat].position + _betSpots[seat].right * 0.14f + Vector3.up * 0.005f, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }

        void UpdateCats()
        {
            for (var seat = 0; seat < PokerTable.SeatCount; seat++)
                _seatCats.UpdateSeat(seat, _table.OccupantAt(seat) == PokerOccupant.Npc, _table.NpcFurAt(seat), _table.NpcArrivalAt(seat), _table.NpcScaresAt(seat));
            _seatCats.Tick();

            if (_dealer != null)
            {
                _dealer.Sitting = true;
                var active = _shown.ActiveSeat;
                _dealer.FaceTowards(active >= 0 ? _table.GetSeatAnchor(active).position : transform.position, 120f);
            }
        }

        void React()
        {
            for (var seat = 0; seat < PokerTable.SeatCount; seat++)
            {
                var won = _shown.WonAt(seat);
                var put = _shown.ContributedAt(seat);
                if (won == 0 && put == 0)
                    continue;

                CatReactions reactions = null;
                if (_table.OccupantAt(seat) == PokerOccupant.Player)
                    reactions = PlayerCat.Find(_table.PlayerAt(seat))?.Reactions;
                else
                    reactions = _seatCats.ReactionsAt(seat);
                if (reactions == null)
                    continue;

                if (won > put)
                    reactions.PlayHappy(won >= put * 4 ? 3 : 1, CatReactions.StaggerDelay());
                else if (won < put)
                    reactions.PlayAngry(CatReactions.StaggerDelay());
            }
        }
    }
}
