using CasinoMares.Core;
using CasinoMares.Player;

namespace CasinoMares.Blackjack
{
    /// <summary>The stool at a blackjack seat: [E] asks the table for this seat (GDD §5.4).</summary>
    public class BlackjackSeat : Interactable
    {
        [UnityEngine.SerializeField] BlackjackTable _table;
        [UnityEngine.SerializeField] int _seat;

        public override string GetPrompt(PlayerCat player)
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return null;

            return _table.OccupantAt(_seat) switch
            {
                SeatOccupant.Player => $"Seat taken by {PlayerCat.NameOf(_table.PlayerAt(_seat))}",
                SeatOccupant.Npc when _table.HandCountAt(_seat) > 0 => "Seat busy (NPC mid-hand)",
                _ => $"Sit down at {_table.DisplayName}",
            };
        }

        public override bool CanInteract(PlayerCat player)
        {
            if (_table.Object == null || !_table.Object.IsValid || SeatingController.LocalTable != null)
                return false;

            var occupant = _table.OccupantAt(_seat);
            return occupant == SeatOccupant.Empty || (occupant == SeatOccupant.Npc && _table.HandCountAt(_seat) == 0);
        }

        public override void Interact(PlayerCat player) => _table.RequestSeat(_seat);
    }
}
