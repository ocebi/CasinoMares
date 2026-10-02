using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Social
{
    /// <summary>A chair at a terrace table.</summary>
    public class TerraceSeat : Interactable
    {
        [SerializeField] TerraceTable _table;
        [SerializeField] int _seat;

        public override string GetPrompt(PlayerCat player)
        {
            if (_table.Object == null || !_table.Object.IsValid)
                return null;
            var occupant = _table.OccupantOf(_seat);
            return occupant != Fusion.PlayerRef.None ? $"{PlayerCat.NameOf(occupant)} is sitting here" : "Sit and watch the sea";
        }

        public override bool CanInteract(PlayerCat player) =>
            _table.Object != null && _table.Object.IsValid && SeatingController.LocalTable == null && !_table.IsTaken(_seat);

        public override void Interact(PlayerCat player) => _table.RequestSeat(_seat);
    }
}
