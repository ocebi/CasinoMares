using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// Holds the frame rate at 30 on a phone that can't keep up: a steady 30 looks better than juddering between
    /// 15 and 40, and the phone cools instead of throttling further. Two things ask for it: the pause menu's
    /// "Frame rate" setting on touch devices (<see cref="Preference"/>, saved in PlayerPrefs) and
    /// <see cref="AutoRenderScale"/>, once rendering fewer pixels has run out of road (<see cref="AutoCapped"/>).
    /// Either one caps; <c>?fps30=1</c> in the page URL caps from the start and <c>?fps30=0</c> never caps, to compare.
    /// Without a cap the browser sets the pace (<c>targetFrameRate</c> −1, its animation frame).
    /// </summary>
    public static class FrameRateCap
    {
        public const string PrefsKey = "FrameRate.Cap30";
        public const int CappedRate = 30;
        const int Uncapped = -1;

        static bool _urlOn;
        static bool _urlOff;
        static bool _preference;
        static bool _autoCapped;

        /// <summary>The player's choice in the pause menu: hold 30 (true) or let it run (false, the default).</summary>
        public static bool Preference
        {
            get => _preference;
            set
            {
                if (value == _preference)
                    return;
                _preference = value;
                PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
                Apply();
            }
        }

        /// <summary><see cref="AutoRenderScale"/>'s verdict; it only sets this while <see cref="AutoAllowed"/>.</summary>
        public static bool AutoCapped
        {
            get => _autoCapped;
            set
            {
                if (value == _autoCapped)
                    return;
                _autoCapped = value;
                Apply();
            }
        }

        /// <summary>The automatic cap has a say: nothing in the URL or the pause menu has decided already.</summary>
        public static bool AutoAllowed => !_urlOn && !_urlOff && !_preference;

        /// <summary>Held at 30 by the URL or the pause menu, not by <see cref="AutoRenderScale"/>.</summary>
        public static bool UserCapped => Target == CappedRate && !_autoCapped;

        /// <summary>What <c>Application.targetFrameRate</c> should be right now.</summary>
        public static int Target => _urlOff ? Uncapped : _urlOn || _preference || _autoCapped ? CappedRate : Uncapped;

        /// <summary>For the debug overlay: "30 (auto)", "30", or "browser".</summary>
        public static string Describe => Target == Uncapped ? "browser" : _autoCapped && !_urlOn && !_preference ? "30 (auto)" : "30";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            var setting = Platform.QueryParameter("fps30");
            _urlOn = setting != null && setting != "0";
            _urlOff = setting == "0";
            _preference = PlayerPrefs.GetInt(PrefsKey, 0) == 1;
            _autoCapped = false;
            Apply();
        }

        static void Apply()
        {
            if (Application.targetFrameRate != Target)
                Application.targetFrameRate = Target;
        }
    }
}
