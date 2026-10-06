using CasinoMares.Audio;
using CasinoMares.Core;
using CasinoMares.Player;
using CasinoMares.Rules;
using UnityEngine;

namespace CasinoMares.Slots
{
    /// <summary>
    /// What every slot machine does around a spin, on every client. When a spin starts, the seated cat paws the
    /// spin button. When it lands, the win is celebrated by its size (<see cref="SlotWin"/>): a chime and a hop for a
    /// small one; coins bursting from the tray for a big one; and for a top win, a bell the whole room hears as
    /// well. The machine's light flashes while a win stands, faster the bigger it was. On the Mobile quality level the
    /// light stays off and the marquee's glow flashes instead: four machine lights side by side cover most of a seated
    /// player's screen, and lights are what a phone that has run hot struggles with most. The views call in here.
    /// </summary>
    public class SlotCelebration : MonoBehaviour
    {
        /// <summary>When the paw meets the button, after the swipe starts.</summary>
        const float PawHitDelay = 0.18f;
        const float ButtonTravel = 0.012f;
        const float ButtonPressTime = 0.25f;
        /// <summary>How much more the marquee's glow swings than the light did: it's small, and the light lit the floor too.</summary>
        const float MarqueeSwing = 2.5f;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] SlotMachine _machine;
        [SerializeField] Light _light;
        [SerializeField] Transform _button;
        [SerializeField] ParticleSystem _coins;

        float _baseIntensity;
        Vector3 _buttonRest;
        float _pressAt = float.NegativeInfinity;
        bool _clicked = true;
        float _glow;
        Renderer _marquee;
        MaterialPropertyBlock _marqueeBlock;
        Color _marqueeEmission;
        float _marqueeGlow = 1f;

        void Awake()
        {
            if (_light != null)
                _baseIntensity = _light.intensity;
            if (_light != null && Platform.MobileQuality)
            {
                _light.enabled = false;
                // CasinoSceneBuilder names it; both cabinets have one.
                _marquee = transform.Find("Marquee")?.GetComponent<Renderer>();
                if (_marquee != null)
                {
                    _marqueeBlock = new MaterialPropertyBlock();
                    _marqueeEmission = _marquee.sharedMaterial.GetColor(EmissionColorId);
                }
            }
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

            var flash = 1f;
            if (_machine.IsSpinning)
            {
                flash = 1f + _glow * (0.4f + 0.2f * Mathf.Sin(Time.time * 18f));
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
                    flash = 1.2f + 0.4f * Mathf.Sin(Time.time * speed);
            }

            if (_light.enabled)
                _light.intensity = _baseIntensity * flash;
            else if (_marquee != null)
                SetMarqueeGlow(Mathf.Max(0.3f, 1f + (flash - 1f) * MarqueeSwing));
        }

        void SetMarqueeGlow(float glow)
        {
            if (Mathf.Approximately(glow, _marqueeGlow))
                return;
            _marqueeGlow = glow;
            _marquee.GetPropertyBlock(_marqueeBlock);
            _marqueeBlock.SetColor(EmissionColorId, _marqueeEmission * glow);
            _marquee.SetPropertyBlock(_marqueeBlock);
        }
    }
}
