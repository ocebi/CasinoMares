using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Poker
{
    /// <summary>A poker seat: sitting down buys in with up to 500 chips from your balance.</summary>
    public class PokerSeat : Interactable
    {
        public const int DefaultBuyIn = 500;

        [SerializeField] PokerTable _table;
        [SerializeField] int _seat;

        public override string GetPrompt(PlayerCat player)
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return null;
            if (_table.OccupantAt(_seat) == PokerOccupant.Player)
                return $"Seat taken by {PlayerCat.NameOf(_table.PlayerAt(_seat))}";
            return ProfileStore.Current.Balance < PokerTable.MinBuyIn
                ? $"Poker needs at least {PokerTable.MinBuyIn} chips"
                : $"Sit down at {_table.DisplayName} (buy in {BuyIn})";
        }

        static int BuyIn => Mathf.Min(DefaultBuyIn, ProfileStore.Current.Balance);

        public override bool CanInteract(PlayerCat player) =>
            _table.Object != null && _table.Object.IsValid && SeatingController.LocalTable == null &&
            _table.OccupantAt(_seat) != PokerOccupant.Player && ProfileStore.Current.Balance >= PokerTable.MinBuyIn;

        public override void Interact(PlayerCat player) => _table.Sit(_seat, BuyIn);
    }
}
