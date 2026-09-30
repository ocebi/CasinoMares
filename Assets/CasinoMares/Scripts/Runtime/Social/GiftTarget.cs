using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.UI;

namespace CasinoMares.Social
{
    /// <summary>On every player cat: walk up to another cat and press [E] to gift chips (GDD §7.3).</summary>
    public class GiftTarget : Interactable
    {
        PlayerCat _cat;

        void Awake()
        {
            _cat = GetComponentInParent<PlayerCat>();
        }

        public override string GetPrompt(PlayerCat player)
        {
            if (_cat == null || _cat.Object == null || !_cat.Object.IsValid || _cat.IsLocal)
                return null;
            return $"Gift chips to {_cat.DisplayName}";
        }

        public override bool CanInteract(PlayerCat player) => ProfileStore.Current.Balance > 0;

        public override void Interact(PlayerCat player) => GiftDialog.Open(_cat);
    }
}
