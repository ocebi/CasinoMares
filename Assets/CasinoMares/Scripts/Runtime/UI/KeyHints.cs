using System.Text.RegularExpressions;
using CasinoMares.Core;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>
    /// Keyboard hints in button labels, such as "Deal (Space)". With touch controls there's no keyboard, so they're
    /// left out.
    /// </summary>
    public static class KeyHints
    {
        static readonly Regex Hint = new(@" \((Space|[A-Z])\)$");

        /// <summary>" (key)" to append to a label, or nothing with touch controls.</summary>
        public static string For(string key) => Platform.IsTouch ? "" : $" ({key})";

        /// <summary>Drops the hints from every button under <paramref name="root"/> (labels written in UXML).</summary>
        public static void Strip(VisualElement root)
        {
            if (!Platform.IsTouch)
                return;
            root.Query<Button>().ForEach(button => button.text = Hint.Replace(button.text, ""));
        }
    }
}
