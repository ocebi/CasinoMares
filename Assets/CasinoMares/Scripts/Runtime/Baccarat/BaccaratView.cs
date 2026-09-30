using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Baccarat
{
    /// <summary>Renders a baccarat table: floating cards in the Player and Banker boxes, chips, croupier and NPC cats.</summary>
    public class BaccaratView : MonoBehaviour
    {
        [SerializeField] BaccaratTable _table;
        [SerializeField] Material _cardMaterial;
        [SerializeField] Material _chipMaterial;
        [SerializeField] Mesh _chipMesh;
        [SerializeField] Transform _shoe;
        [SerializeField] Transform _discard;
        [SerializeField] Transform _playerBox;
        [SerializeField] Transform _bankerBox;
        [SerializeField] Transform[] _chipSpots = new Transform[BaccaratTable.SeatCount];
        [SerializeField] NpcCatVisual _npcCatPrefab;
        [SerializeField] NpcCatVisual _croupier;

        readonly Dictionary<int, CardVisual> _cards = new();
        readonly HashSet<int> _seen = new();
        readonly List<int> _stale = new();
        readonly ChipStack[] _stacks = new ChipStack[BaccaratTable.SeatCount];
        SeatCats _seatCats;
        bool _initialized;
        int _lastSettled;
        int _lastShoeSeed;

        public Vector3 PlayerLabelPosition => _playerBox.position + Vector3.up * 0.06f - _playerBox.forward * 0.18f;
        public Vector3 BankerLabelPosition => _bankerBox.position + Vector3.up * 0.06f - _bankerBox.forward * 0.18f;

        void Awake()
        {
            _cardMaterial = new Material(_cardMaterial) { name = "Card (runtime)" };
            _cardMaterial.SetTexture("_BaseMap", CardFaces.Atlas);
            _seatCats = new SeatCats(_npcCatPrefab, transform, BaccaratTable.SeatCount, _table.GetSeatAnchor);
        }

        void LateUpdate()
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return;

            var instant = !_initialized;
            if (!_initialized)
            {
                _initialized = true;
                _lastSettled = _table.SettledRoundId;
                _lastShoeSeed = _table.ShoeSeedValue;
            }

            if (_table.ShoeSeedValue != _lastShoeSeed)
            {
                _lastShoeSeed = _table.ShoeSeedValue;
                Sfx.PlayAt(SoundId.Shuffle, _shoe.position);
            }

            _seen.Clear();
            for (var order = 0; order < 6; order++)
            {
                if (!_table.TryGetRevealedCard(order, out var banker, out var index, out var card))
                    continue;
                var box = banker ? _bankerBox : _playerBox;
                // The third card lies sideways, as in a real casino.
                var rotation = index == 2 ? box.rotation * Quaternion.Euler(0f, 90f, 0f) : box.rotation;
                var position = box.position + box.right * (index * 0.18f - 0.18f) + Vector3.up * 0.002f;
                Place(order, card, position, rotation, instant);
            }

            _stale.Clear();
            foreach (var pair in _cards)
            {
                if (!_seen.Contains(pair.Key))
                    _stale.Add(pair.Key);
            }

            foreach (var key in _stale)
            {
                if (_cards[key] != null)
                    _cards[key].FlyAwayAndRelease(_discard.position, _discard.rotation);
                _cards.Remove(key);
            }

            if (_table.SettledRoundId != _lastSettled)
            {
                _lastSettled = _table.SettledRoundId;
                if (_table.Phase == BaccaratPhase.Result)
                    React();
            }

            UpdateStacks();
            UpdateCats();
        }

        void Place(int key, byte card, Vector3 position, Quaternion rotation, bool instant)
        {
            _seen.Add(key);
            if (_cards.TryGetValue(key, out var visual) && visual != null && visual.Card == card)
            {
                visual.Retarget(position, rotation, true);
                return;
            }

            visual = CardVisual.Create(transform, _cardMaterial);
            visual.transform.SetPositionAndRotation(_shoe.position, _shoe.rotation * Quaternion.Euler(0f, 0f, 180f));
            visual.SetCard(card);
            visual.FlyTo(position, rotation, true, instant);
            _cards[key] = visual;
        }

        void UpdateStacks()
        {
            for (var seat = 0; seat < BaccaratTable.SeatCount; seat++)
            {
                var amount = _table.Phase == BaccaratPhase.Result ? _table.PayoutAt(seat) : _table.TotalBetAt(seat);
                var stack = _stacks[seat];
                if (amount <= 0)
                {
                    if (stack != null)
                        stack.gameObject.SetActive(false);
                    continue;
                }

                if (stack == null)
                {
                    stack = ChipStack.Create(transform, _chipMesh, _chipMaterial);
                    stack.transform.position = _chipSpots[seat].position;
                    _stacks[seat] = stack;
                }

                stack.gameObject.SetActive(true);
                if (stack.Amount != amount && stack.Amount >= 0)
                    Sfx.PlayAt(SoundId.ChipBet, _chipSpots[seat].position, 0.6f);
                stack.SetAmount(amount);
                stack.MoveTo(_chipSpots[seat].position, false);
            }
        }

        void UpdateCats()
        {
            for (var seat = 0; seat < BaccaratTable.SeatCount; seat++)
                _seatCats.UpdateSeat(seat, _table.OccupantAt(seat) == BaccaratOccupant.Npc, _table.NpcFurAt(seat), _table.NpcArrivalAt(seat));
            _seatCats.Tick();

            if (_croupier != null)
                _croupier.Sitting = true;
        }

        void React()
        {
            for (var seat = 0; seat < BaccaratTable.SeatCount; seat++)
            {
                var bet = _table.TotalBetAt(seat);
                if (bet == 0)
                    continue;
                var net = _table.PayoutAt(seat) - bet;
                CatReactions reactions;
                var owner = _table.RoundOwnerAt(seat);
                if (owner != Fusion.PlayerRef.None)
                    reactions = PlayerCat.Find(owner)?.Reactions;
                else
                    reactions = _seatCats.ReactionsAt(seat);
                if (reactions == null)
                    continue;
                if (net > 0)
                    reactions.PlayHappy(_table.Outcome == BaccaratOutcome.Tie ? 3 : 1, CatReactions.StaggerDelay());
                else if (net < 0)
                    reactions.PlayAngry(CatReactions.StaggerDelay());
            }
        }
    }
}
