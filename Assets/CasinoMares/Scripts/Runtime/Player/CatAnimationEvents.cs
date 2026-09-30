using System;
using CasinoMares.Audio;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Receives the animation events on the cat clips (set up in CasinoSceneBuilder's import settings; Unity logs
    /// errors for events without a receiver). Must sit on the same GameObject as the Animator.
    /// </summary>
    public class CatAnimationEvents : MonoBehaviour
    {
        /// <summary>The idle loop fires this halfway through, when the cat opens its mouth. Hook for a meow sound.</summary>
        public event Action AnimalSoundPlayed;

        void AnimalSound()
        {
            // The 2 s idle loop fires this every time; only meow now and then (about every 30 s per idle cat) or
            // the room gets very chatty.
            if (UnityEngine.Random.value < 0.07f)
                Sfx.PlayAt(SoundId.Meow, transform.position, 0.6f);
            AnimalSoundPlayed?.Invoke();
        }
    }
}
