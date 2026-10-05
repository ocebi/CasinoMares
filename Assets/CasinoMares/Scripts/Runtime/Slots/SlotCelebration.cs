using CasinoMares.Audio;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// What every slot machine does around a spin, on every client. When a spin starts, the seated cat paws the
    /// spin button. When it lands, the win is celebrated by its size (<see cref="SlotWin"/>): a chime and a hop for a
    /// small one; coins bursting from the tray for a big one; and for a top win, a bell the whole room hears as
    /// well. The machine's light flashes while a win stands, faster the bigger it was. The views call in here.
    /// </summary>
    public class SlotCelebration : MonoBehaviour
    {
        /// <summary>When the paw meets the button, after the swipe starts.</summary>
        const float PawHitDelay = 0.18f;
        const float ButtonTravel = 0.012f;
        const float ButtonPressTime = 0.25f;

        [SerializeField] SlotMachine _machine;
        [SerializeField] Light _light;
        [SerializeField] Transform _button;
        [SerializeField] ParticleSystem _coins;

        float _baseIntensity;
        Vector3 _buttonRest;
        float _pressAt = float.NegativeInfinity;
        bool _clicked = true;
        float _glow;

        void Awake()
        {
            if (_light != null)
                _baseIntensity = _light.intensity;
            if (_button != null)
                _buttonRest = _button.localPosition;
        }

        /// <summary>A new spin started: the seated cat swats the spin button.</summary>
        public void OnSpinStarted()
        {
            PlayerCat.Find(_machine.SpinOwner)?.Reactions?.PlaySwipe();
            _pressAt = Time.time + PawHitDelay;
            _clicked = false;
        }

        /// <summary>A paw swat during the spin (a Yarn Tumble chain), without the button.</summary>
        public void Swat() => PlayerCat.Find(_machine.SpinOwner)?.Reactions?.PlaySwipe();

        /// <summary>How much the machine glows in anticipation, 0 to 1 (the third reel teasing).</summary>
        public void SetGlow(float amount) => _glow = amount;

        /// <summary>The spin's result is showing: celebrate it.</summary>
        public void OnLanded()
        {
            var tier = _machine.LastWin;
            if (tier == SlotWin.None)
                return;

            var owner = PlayerCat.Find(_machine.SpinOwner);
            var front = transform.position + transform.forward * 0.4f + Vector3.up;
            switch (tier)
            {
                case SlotWin.Small:
                    Sfx.PlayAt(SoundId.SlotChime, front);
                    owner?.Reactions?.PlayHappy();
                    break;
                case SlotWin.Big:
                    Sfx.PlayAt(SoundId.SlotChime, front);
                    Sfx.PlayAt(SoundId.ChipPayout, front);
                    Coins(24);
                    owner?.Reactions?.PlayHappy(2);
                    _machine.RaiseBigWin();
                    break;
                case SlotWin.Top:
                    // Not positional: everyone in the room hears it.
                    Sfx.Play(SoundId.SlotBell, 0.8f);
                    Sfx.PlayAt(SoundId.ChipPayout, front);
                    Coins(70);
                    owner?.Reactions?.PlayHappy(3);
                    _machine.RaiseBigWin();
                    break;
            }
        }

        void Coins(int count)
        {
            if (_coins != null)
                _coins.Emit(count);
        }

        void LateUpdate()
        {
            if (_button != null)
            {
                var pressed = Time.time - _pressAt;
                if (!_clicked && pressed >= 0f)
                {
                    _clicked = true;
                    Sfx.PlayAt(SoundId.UiClick, _button.position, 0.7f);
                }

                var depth = pressed >= 0f && pressed < ButtonPressTime ? Mathf.Sin(pressed / ButtonPressTime * Mathf.PI) : 0f;
                _button.localPosition = _buttonRest - Vector3.up * (ButtonTravel * depth);
            }

            if (_light == null || _machine.Object == null || !_machine.Object.IsValid)
                return;

            var intensity = _baseIntensity;
            if (_machine.IsSpinning)
            {
                intensity *= 1f + _glow * (0.4f + 0.2f * Mathf.Sin(Time.time * 18f));
            }
            else
            {
                var speed = _machine.LastWin switch
                {
                    SlotWin.Small => 10f,
                    SlotWin.Big => 16f,
                    SlotWin.Top => 24f,
                    _ => 0f,
                };
                if (speed > 0f)
                    intensity *= 1.2f + 0.4f * Mathf.Sin(Time.time * speed);
            }

            _light.intensity = intensity;
        }
    }
}
