using System.Collections.Generic;
using CasinoMares.Audio;
using CasinoMares.Blackjack;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Rules;
using CasinoMares.Slots;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>Seated slot machine UI: bet picker, Spin (Space), the result and a small paytable.</summary>
    public class SlotsHud : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _status;
        Button _spinButton;
        readonly List<Button> _betButtons = new();

        SlotMachine _machine;
        int _bet = SlotRules.BetOptions[1];

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("slots-root");
            _status = ui.Q<Label>("status-label");
            _spinButton = ui.Q<Button>("spin-button");
            _spinButton.clicked += Spin;
            ui.Q<Button>("leave-button").clicked += () => _machine?.RequestLeave(0);

            var row = ui.Q("bet-row");
            row.Clear();
            _betButtons.Clear();
            foreach (var value in SlotRules.BetOptions)
            {
                var button = new Button(() => _bet = value) { text = value.ToString(), userData = value };
                button.AddToClassList("bj-chip");
                button.style.backgroundColor = ChipStack.ColorFor(value == 50 ? 25 : value);
                if (button.style.backgroundColor.value.grayscale > 0.7f)
                    button.style.color = new Color(0.15f, 0.15f, 0.2f);
                row.Add(button);
                _betButtons.Add(button);
            }

            var paytable = ui.Q<Label>("paytable-label");
            var lines = new List<string>();
            for (var symbol = SlotRules.SymbolCount - 1; symbol >= 0; symbol--)
                lines.Add($"3× {SlotRules.Names[symbol]}  pays {SlotRules.ThreeOfAKind[symbol]}×");
            lines.Add("2× PAW  pays 3×");
            lines.Add("1× PAW  returns your bet");
            paytable.text = string.Join("\n", lines);

            SeatingController.LocalSeatChanged += OnLocalSeatChanged;
            SlotMachine.LocalSpinResult += OnSpinResult;
            OnLocalSeatChanged(SeatingController.LocalTable, SeatingController.LocalSeat);
        }

        void OnDisable()
        {
            SeatingController.LocalSeatChanged -= OnLocalSeatChanged;
            SlotMachine.LocalSpinResult -= OnSpinResult;
        }

        void OnLocalSeatChanged(IGameTable table, int seat)
        {
            _machine = table as SlotMachine;
            _root.EnableInClassList("hidden", _machine == null);
        }

        void OnSpinResult(SlotMachine machine, int net)
        {
            if (net > 0)
            {
                Sfx.Play(SoundId.Win, 0.8f);
                Toasts.Show($"Jackpot-ish! +{net:N0}");
            }
        }

        void Update()
        {
            if (_machine == null || _machine.Object == null || !_machine.Object.IsValid)
                return;

            foreach (var button in _betButtons)
            {
                var selected = (int)button.userData == _bet;
                button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor =
                    selected ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 1f, 1f, 0.6f);
            }

            var mine = _machine.SpinOwner == _machine.Runner.LocalPlayer;
            _spinButton.SetEnabled(!_machine.IsSpinning && ProfileStore.Current.Balance >= _bet);
            _status.text = _machine.IsSpinning
                ? "Spinning..."
                : !mine || _machine.SpinId == 0
                    ? $"Bet {_bet} and spin!"
                    : _machine.LastPayout > 0
                        ? $"{SlotRules.Names[_machine.ReelAt(0)]} {SlotRules.Names[_machine.ReelAt(1)]} {SlotRules.Names[_machine.ReelAt(2)]}: won {_machine.LastPayout:N0}"
                        : $"{SlotRules.Names[_machine.ReelAt(0)]} {SlotRules.Names[_machine.ReelAt(1)]} {SlotRules.Names[_machine.ReelAt(2)]}: no luck";

            if (GameInput.KeyPressed(Key.Space))
                Spin();
        }

        void Spin()
        {
            if (_machine == null || _machine.IsSpinning)
                return;
            if (ProfileStore.Current.Balance < _bet)
            {
                Toasts.Show("Not enough chips for that bet.");
                return;
            }

            _machine.Spin(_bet);
        }
    }
}
