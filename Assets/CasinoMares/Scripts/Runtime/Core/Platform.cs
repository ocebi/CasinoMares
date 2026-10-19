using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CasinoMares.Core
{
    /// <summary>
    /// What the game is running on. In a browser (WebGL) there's nothing to quit, a hidden tab only gets about one
    /// frame a second, and phones and tablets play with on-screen controls (<see cref="IsTouch"/>). Browser queries
    /// go through CasinoMaresWeb.jslib.
    /// </summary>
    public static class Platform
    {
        /// <summary>EditorPrefs key for Casino Mares ▸ Web ▸ Simulate Touch Controls.</summary>
        public const string SimulateTouchKey = "CasinoMares.SimulateTouch";

        public static bool IsWeb => Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>A browser tab is closed by the browser, so there's no Quit button.</summary>
        public static bool CanQuit => !IsWeb;

        /// <summary>
        /// Play with on-screen controls (joystick, buttons, drag to look). Decided once at startup: phones and tablets,
        /// or <c>?touch=1</c> in the page URL (<c>?touch=0</c> forces keyboard and mouse).
        /// </summary>
        public static bool IsTouch { get; private set; }

        /// <summary>The browser can go full screen (iPhone Safari can't).</summary>
        public static bool CanFullscreen => IsWeb && CM_CanFullscreen() != 0;

        /// <summary>
        /// The phone can't go full screen from the page (iPhone Safari) and the game wasn't opened from the home screen,
        /// which is the only way there to hide the browser bars.
        /// </summary>
        public static bool NeedsHomeScreenForFullscreen => IsWeb && IsTouch && CM_CanFullscreen() == 0 && CM_IsStandalone() == 0;

        /// <summary>True while the browser tab is in the background.</summary>
        public static bool PageHidden => IsWeb && CM_IsPageHidden() != 0;

        /// <summary>The compressed texture set the page loaded: "DXT" (desktop) or "ASTC" (phones, or <c>?astc=1</c>).</summary>
        public static string TextureFormat => IsWeb ? CM_TextureFormat() : "native";

        /// <summary>Rendering at the Mobile quality level (phones and tablets in a browser): fewer lights where it counts.</summary>
        public static bool MobileQuality => QualitySettings.names[QualitySettings.GetQualityLevel()] == "Mobile";

        /// <summary>
        /// The screen height in CSS pixels, which follows the device's physical size (a phone held sideways is about
        /// 390). Outside a browser it's the height in pixels.
        /// </summary>
        public static float CssHeight => IsWeb ? CM_CanvasCssHeight() : Screen.height;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            IsTouch = DetectTouch();
        }

        /// <summary>
        /// Browsers start on WebGL's default quality level (Mobile). Desktop browsers get the PC level; phones and
        /// tablets stay on Mobile, which renders the same way at full resolution but skips SSAO and uses smaller
        /// shadow maps. <c>?hdr=0</c> renders without HDR, to rule out a GPU that can't render to float targets.
        /// <c>?shadows=hi</c> gives the main light the PC level's shadow map (1024, 2 cascades, 50 m) on a phone and
        /// <c>?shadows=lo</c> the Mobile level's (512, 1 cascade, 25 m) on a desktop, to compare; the Mobile level's
        /// other shadow savings (no additional light shadows, no soft shadows) are fixed in the asset.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyQualityLevel()
        {
            if (!IsWeb)
                return;

            var level = Array.IndexOf(QualitySettings.names, IsTouch ? "Mobile" : "PC");
            if (level >= 0 && level != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(level, true);

            var pipeline = UniversalRenderPipeline.asset;
            if (pipeline == null)
                return;
            if (QueryParameter("hdr") == "0")
                pipeline.supportsHDR = false;

            switch (QueryParameter("shadows"))
            {
                case "hi":
                    pipeline.mainLightShadowmapResolution = 1024;
                    pipeline.shadowCascadeCount = 2;
                    pipeline.shadowDistance = 50f;
                    break;
                case "lo":
                    pipeline.mainLightShadowmapResolution = 512;
                    pipeline.shadowCascadeCount = 1;
                    pipeline.shadowDistance = 25f;
                    break;
            }
        }

        static bool DetectTouch()
        {
            var forced = QueryParameter("touch");
            if (forced != null)
                return forced != "0";
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(SimulateTouchKey, false))
                return true;
#endif
            // Application.isMobilePlatform also covers the Device Simulator in the editor.
            return Application.isMobilePlatform || (IsWeb && CM_PrefersTouch() != 0);
        }

        /// <summary>A value from the page URL's query string (<c>?profile=bot</c>), or null.</summary>
        public static string QueryParameter(string name)
        {
            if (!IsWeb)
                return null;

            var url = Application.absoluteURL;
            var start = url.IndexOf('?');
            if (start < 0)
                return null;
            var end = url.IndexOf('#', start);
            var query = end < 0 ? url.Substring(start + 1) : url.Substring(start + 1, end - start - 1);

            foreach (var pair in query.Split('&'))
            {
                var equals = pair.IndexOf('=');
                var key = Uri.UnescapeDataString(equals < 0 ? pair : pair.Substring(0, equals));
                if (key == name)
                    return equals < 0 ? "" : Uri.UnescapeDataString(pair.Substring(equals + 1));
            }

            return null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern float CM_CanvasCssHeight();
        [DllImport("__Internal")] static extern int CM_PrefersTouch();
        [DllImport("__Internal")] static extern int CM_IsPageHidden();
        [DllImport("__Internal")] static extern int CM_CanFullscreen();
        [DllImport("__Internal")] static extern int CM_IsStandalone();
        [DllImport("__Internal")] static extern string CM_TextureFormat();
#else
        static float CM_CanvasCssHeight() => Screen.height;
        static int CM_PrefersTouch() => 0;
        static int CM_IsPageHidden() => 0;
        static int CM_CanFullscreen() => 0;
        static int CM_IsStandalone() => 0;
        static string CM_TextureFormat() => "native";
#endif
    }
}
