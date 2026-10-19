using CasinoMares.CameraRig;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Social;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Seated at a terrace table or on a bar stool there's no game UI, just Stand up (E). Phones need the button:
    /// with no keyboard, it's the only way back up. At the bar there's also the wine order (UX §1.5): one button
    /// whose text is its state ("Order wine · 20", "Pouring…", "Another in 12 s") and a tipsy meter; Space
    /// orders on desktop. On a phone the wine glyph carries the meaning and the label under it is short: the price
    /// when ready, "…" while busy, the seconds left in the cooldown. The meter has a segment per glass still in effect
    /// and is full at <see cref="TipsyEffect.VeryDrunkAt"/>, where the drunk effect is at its strongest.
    /// </summary>
    public class TerraceHud : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

        VisualElement _root;
        VisualElement _barBottom;
        Label _count;
        VisualElement _meter;
        readonly VisualElement[] _segments = new VisualElement[TipsyEffect.VeryDrunkAt];
        Button _orderButton;
        Label _orderLabel;
        TerraceTable _table;
        WineBar _bar;
        int _seat;
        string _orderText;
        bool _orderReady;
        int _shownDrinks = -1;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("terrace-root");
            _barBottom = ui.Q("bar-bottom");
            _count = ui.Q<Label>("bar-count");
            _meter = ui.Q("bar-meter-track");
            _meter.Clear();
            for (var i = 0; i < _segments.Length; i++)
            {
                var segment = new VisualElement { pickingMode = PickingMode.Ignore };
                segment.AddToClassList("bar-meter-seg");
                segment.EnableInClassList("bar-meter-seg--tipsy", i + 1 >= PlayerCat.DrunkAt);
                segment.EnableInClassList("bar-meter-seg--tipsy-start", i + 1 == PlayerCat.DrunkAt);
                _meter.Add(segment);
                _segments[i] = segment;
            }

            _orderButton = ui.Q<Button>("order-button");
            _orderLabel = _orderButton.Q<Label>("order-label");
            ui.Q<Button>("leave-button").clicked += () => _table?.RequestLeave(_seat);
            _orderButton.clicked += Order;
            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable() => SeatingController.LocalSeatChanged -= OnLocalSeatChanged;

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            _table = table as TerraceTable;
            _seat = seat;
            _bar = _table != null && _table.IsBar ? _table.GetComponent<WineBar>() : null;
            _root.EnableInClassList("hidden", _table == null);
            _barBottom.EnableInClassList("hidden", _bar == null);
            _shownDrinks = -1;
            _orderText = null;
        }

        void Update()
        {
            if (_bar == null)
                return;
            UpdateOrderButton();
            UpdateCount();
            if (_orderReady && GameInput.KeyPressed(Key.Space))
                Order();
        }

        void Order() => _bar?.RequestOrder(_seat);

        /// <summary>One element, its text the state; only written when the text (or the countdown second) changes.</summary>
        void UpdateOrderButton()
        {
            var phase = _bar.PhaseAt(_seat, out var elapsed);
            var ready = phase == WinePhase.Idle && !_bar.IsOrderPending(_seat);
            var text = Platform.IsTouch ? TouchOrderText(phase, ready, elapsed) : phase switch
            {
                WinePhase.Idle when ready => $"Order wine · {WineBar.WinePrice}" + KeyHints.For("Space"),
                WinePhase.Idle => "Ordering…",
                WinePhase.Pour => "Pouring…",
                WinePhase.Paw or WinePhase.Slide => "Coming your way…",
                WinePhase.Drink => "Drinking…",
                _ => $"Another in {Mathf.CeilToInt(WineBar.EmptyEnd - elapsed)} s",
            };

            if (text == _orderText)
                return;
            _orderText = text;
            _orderReady = ready;
            _orderLabel.text = text;
            _orderButton.SetEnabled(ready);
            _orderButton.EnableInClassList("bj-button--wait", !ready);
        }

        /// <summary>The short touch label under the wine icon (UX §1.9): "20", "…", or "12 s".</summary>
        static string TouchOrderText(WinePhase phase, bool ready, float elapsed) => phase switch
        {
            WinePhase.Idle when ready => WineBar.WinePrice.ToString(),
            WinePhase.Empty => $"{Mathf.CeilToInt(WineBar.EmptyEnd - elapsed)} s",
            _ => "…",
        };

        void UpdateCount()
        {
            var cat = PlayerCat.Local;
            var drinks = cat != null && cat.Object != null && cat.Object.IsValid ? cat.DrinkCount : 0;
            if (drinks == _shownDrinks)
                return;
            _shownDrinks = drinks;
            var tipsy = drinks >= PlayerCat.DrunkAt;
            _count.text = drinks switch
            {
                0 => "Sober",
                1 => "1 glass",
                _ when !tipsy => $"{drinks} glasses",
                _ when drinks >= TipsyEffect.VeryDrunkAt => "Very drunk",
                _ when drinks > PlayerCat.DrunkAt => "Drunk",
                _ => "Tipsy",
            };
            _count.EnableInClassList("bar-count--tipsy", tipsy);
            for (var i = 0; i < _segments.Length; i++)
                _segments[i].EnableInClassList("bar-meter-seg--full", i < drinks);
            _meter.EnableInClassList("bar-meter--full", drinks >= TipsyEffect.VeryDrunkAt);
        }
    }
}
