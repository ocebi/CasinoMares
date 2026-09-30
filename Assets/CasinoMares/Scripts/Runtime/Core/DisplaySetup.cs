using System;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// Standalone builds remember their last window size. A test bot started with <c>-screen-width 800</c> leaves that
    /// tiny window behind for the next normal launch, so a normal launch that finds a small window goes back to
    /// borderless full screen at the display's resolution. Launches that pass their own size (bots, tests) are left
    /// alone.
    /// </summary>
    public static class DisplaySetup
    {
        /// <summary>Windows narrower than this share of the display count as leftovers.</summary>
        const float SmallWindowShare = 0.6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RestoreSensibleWindow()
        {
            if (Application.isEditor)
                return;

            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-bot") >= 0 || Array.IndexOf(args, "-screen-width") >= 0 || Array.IndexOf(args, "-screen-fullscreen") >= 0)
                return;

            var display = Screen.currentResolution;
            if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width >= display.width * SmallWindowShare)
                return;

            Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
        }
    }
}
