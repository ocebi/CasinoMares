using CasinoMares.Core;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>The stool in front of a slot machine.</summary>
    public class SlotSeat : Interactable
    {
        [SerializeField] SlotMachine _machine;

        public override string GetPrompt(PlayerCat player)
        {
            if (_machine.Object == null || !_machine.Object.IsValid)
                return null;
            return _machine.Occupant != Fusion.PlayerRef.None
                ? $"{PlayerCat.NameOf(_machine.Occupant)} is playing"
                : _machine.Kind == SlotKind.YarnTumble ? "Play Yarn Tumble" : "Play the slot machine";
        }

        public override bool CanInteract(PlayerCat player) =>
            _machine.Object != null && _machine.Object.IsValid && SeatingController.LocalTable == null &&
            _machine.Occupant == Fusion.PlayerRef.None;

        public override void Interact(PlayerCat player) => _machine.RequestSeat();
    }
}
