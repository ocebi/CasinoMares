using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Social;
using UnityEngine;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Seated at a terrace table or on a bar stool there's no game UI, just Stand up (E). Phones need the button:
    /// with no keyboard, it's the only way back up.
    /// </summary>
    public class TerraceHud : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

        VisualElement _root;
        TerraceTable _table;
        int _seat;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("terrace-root");
            ui.Q<Button>("leave-button").clicked += () => _table?.RequestLeave(_seat);
            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable() => SeatingController.LocalSeatChanged -= OnLocalSeatChanged;

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            _table = table as TerraceTable;
            _seat = seat;
            _root.EnableInClassList("hidden", _table == null);
        }
    }
}
