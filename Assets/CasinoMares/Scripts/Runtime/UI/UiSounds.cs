using CasinoMares.Audio;
using UnityEngine.UIElements;

namespace CasinoMares.UI
{
    /// <summary>Click sound for every button under a UI root.</summary>
    public static class UiSounds
    {
        public static void Attach(VisualElement root)
        {
            root.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                    Sfx.Play(SoundId.UiClick, 0.5f);
            }, TrickleDown.TrickleDown);
        }
    }
}
