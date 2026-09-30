using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Roulette
{
    /// <summary>A roulette stool: [E] asks the table for this seat.</summary>
    public class RouletteSeat : Interactable
    {
        [SerializeField] RouletteTable _table;
        [SerializeField] int _seat;

        public override string GetPrompt(PlayerCat player)
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return null;
            return _table.OccupantAt(_seat) == RouletteOccupant.Player
                ? $"Seat taken by {PlayerCat.NameOf(_table.PlayerAt(_seat))}"
                : $"Sit down at {_table.DisplayName}";
        }

        public override bool CanInteract(PlayerCat player) =>
            _table.Object != null && _table.Object.IsValid && SeatingController.LocalTable == null &&
            _table.OccupantAt(_seat) != RouletteOccupant.Player;

        public override void Interact(PlayerCat player) => _table.RequestSeat(_seat);
    }
}
