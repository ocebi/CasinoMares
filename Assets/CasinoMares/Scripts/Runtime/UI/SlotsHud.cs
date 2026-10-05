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
    /// <summary>
    /// Seated slot machine UI: bet picker, Spin (Space), the result and a small paytable, for either kind of
    /// machine. Big and top wins get a banner whose payout counts up.
    /// </summary>
    public class SlotsHud : MonoBehaviour
    {
        const float BannerTime = 4f;
        const float TickEvery = 0.09f;

        [SerializeField] UIDocument _document;

        VisualElement _root;
        Label _status;
        Label _paytable;
        Button _spinButton;
        readonly List<Button> _betButtons = new();
        VisualElement _banner;
        Label _bannerTitle;
        Label _bannerAmount;

        SlotMachine _machine;
        int _bet = SlotRules.BetOptions[1];
        int _countTo;
        float _countStart;
        float _countTime;
        float _bannerUntil;
        float _nextTick;

        void OnEnable()
        {
            var ui = _document.rootVisualElement;
            UiSounds.Attach(ui);
            _root = ui.Q("slots-root");
            _status = ui.Q<Label>("status-label");
            _paytable = ui.Q<Label>("paytable-label");
            _spinButton = ui.Q<Button>("spin-button");
            _spinButton.clicked += Spin;
            ui.Q<Button>("leave-button").clicked += () => _machine?.RequestLeave(0);
            _banner = ui.Q("win-banner");
            _bannerTitle = ui.Q<Label>("win-title");
            _bannerAmount = ui.Q<Label>("win-amount");

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
            HideBanner();
            if (_machine != null)
                _paytable.text = _machine.Kind == SlotKind.YarnTumble ? TumblePaytable() : ReelsPaytable();
        }

        static string ReelsPaytable()
        {
            var lines = new List<string>();
            for (var symbol = SlotRules.SymbolCount - 1; symbol >= 0; symbol--)
                lines.Add($"3× {SlotRules.Names[symbol]}  pays {SlotRules.ThreeOfAKind[symbol]}×");
            lines.Add($"2× PAW  pays {SlotRules.TwoPaws}×");
            lines.Add($"1× PAW  pays {SlotRules.OnePaw}×");
            return string.Join("\n", lines);
        }

        static string TumblePaytable()
        {
            var lines = new List<string> { $"{YarnTumbleRules.MinGroup}+ touching side by side pay:" };
            for (var symbol = YarnTumbleRules.SymbolCount - 1; symbol >= 0; symbol--)
                lines.Add($"{YarnTumbleRules.Names[symbol]}  {YarnTumbleRules.MultiplierText(YarnTumbleRules.GroupOfFive[symbol])}×");
            lines.Add("Each one more adds a quarter.");
            lines.Add("Winners get batted away and new ones fall in.");
            var chains = new List<string>();
            foreach (var tenths in YarnTumbleRules.ChainMultipliers)
                chains.Add("×" + YarnTumbleRules.MultiplierText(tenths));
            lines.Add("Chains: " + string.Join(" ", chains));
            return string.Join("\n", lines);
        }

        void OnSpinResult(SlotMachine machine, int net)
        {
            var tier = machine.LastWin;
            if (tier == SlotWin.None)
                return;
            Sfx.Play(SoundId.Win, 0.8f);
            if (tier == SlotWin.Small)
                return;

            _bannerTitle.text = tier == SlotWin.Top ? "MEGA WIN!" : "BIG WIN!";
            _banner.EnableInClassList("slots-banner--top", tier == SlotWin.Top);
            _banner.AddToClassList("slots-banner--show");
            _countTo = machine.LastPayout;
            _countStart = _nextTick = Time.time;
            _countTime = tier == SlotWin.Top ? 2.6f : 1.6f;
            _bannerUntil = Time.time + BannerTime;
            _bannerAmount.text = "+0";
        }

        void HideBanner()
        {
            _bannerUntil = 0f;
            _banner?.RemoveFromClassList("slots-banner--show");
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

            _spinButton.SetEnabled(!_machine.IsSpinning && ProfileStore.Current.Balance >= _bet);
            _status.text = Status();
            UpdateBanner();

            if (GameInput.KeyPressed(Key.Space))
                Spin();
        }

        string Status()
        {
            var mine = _machine.SpinOwner == _machine.Runner.LocalPlayer;
            if (!_machine.IsSpinning && (!mine || _machine.SpinId == 0))
                return $"Bet {_bet} and spin!";
            return _machine.Kind == SlotKind.YarnTumble ? TumbleStatus() : ReelsStatus();
        }

        string ReelsStatus()
        {
            var a = SlotRules.Names[_machine.ReelAt(0)];
            var b = SlotRules.Names[_machine.ReelAt(1)];
            var c = SlotRules.Names[_machine.ReelAt(2)];
            if (_machine.IsSpinning)
            {
                // Once the second reel is down, the third teases.
                return _machine.Teases && _machine.SpinElapsed > SlotMachine.ReelStops[1] ? $"{a} {b} ...come on!" : "Spinning...";
            }

            return _machine.LastPayout > 0 ? $"{a} {b} {c}: won {_machine.LastPayout:N0}" : $"{a} {b} {c}: no luck";
        }

        string TumbleStatus()
        {
            var spin = _machine.Tumble;
            if (_machine.IsSpinning)
            {
                var chain = YarnTumbleTiming.ChainAt(_machine.SpinElapsed, spin.Cascades.Count, out _);
                if (chain < 0)
                    return "Tumbling...";
                if (chain >= spin.Cascades.Count)
                    return _machine.LastPayout > 0 ? $"Won {_machine.LastPayout:N0}!" : "No luck this time";
                var paid = YarnTumbleRules.Payout(_machine.Bet, spin.PayThrough(chain + 1));
                return $"Chain ×{YarnTumbleRules.MultiplierText(spin.Cascades[chain].Multiplier)}!  +{paid:N0}";
            }

            if (_machine.LastPayout == 0)
                return "No luck this time";
            return spin.Cascades.Count > 1
                ? $"{spin.Cascades.Count} chains: won {_machine.LastPayout:N0}"
                : $"Won {_machine.LastPayout:N0}";
        }

        void UpdateBanner()
        {
            if (_bannerUntil <= 0f)
                return;
            if (Time.time > _bannerUntil || _machine.IsSpinning)
            {
                HideBanner();
                return;
            }

            var t = Mathf.Clamp01((Time.time - _countStart) / _countTime);
            var shown = Mathf.RoundToInt(_countTo * (1f - (1f - t) * (1f - t)));
            _bannerAmount.text = $"+{shown:N0}";
            if (t < 1f && Time.time >= _nextTick)
            {
                _nextTick = Time.time + TickEvery;
                Sfx.Play(SoundId.Tick, 0.35f);
            }
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
