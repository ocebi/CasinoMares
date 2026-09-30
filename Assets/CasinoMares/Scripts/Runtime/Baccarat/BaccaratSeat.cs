using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Baccarat
{
    /// <summary>A baccarat stool: [E] asks the table for this seat.</summary>
    public class BaccaratSeat : Interactable
    {
        [SerializeField] BaccaratTable _table;
        [SerializeField] int _seat;

        public override string GetPrompt(PlayerCat player)
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return null;
            return _table.OccupantAt(_seat) == BaccaratOccupant.Player
                ? $"Seat taken by {PlayerCat.NameOf(_table.PlayerAt(_seat))}"
                : $"Sit down at {_table.DisplayName}";
        }

        public override bool CanInteract(PlayerCat player) =>
            _table.Object != null && _table.Object.IsValid && SeatingController.LocalTable == null &&
            _table.OccupantAt(_seat) != BaccaratOccupant.Player;

        public override void Interact(PlayerCat player) => _table.RequestSeat(_seat);
    }
}
