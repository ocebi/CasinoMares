using System;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>Short on-screen notifications ("Luna gifted you 200 chips"). The HUD displays them.</summary>
    public static class Toasts
    {
        public static event Action<string> Shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Shown = null;

        public static void Show(string message) => Shown?.Invoke(message);
    }
}
