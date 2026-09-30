using System.Collections.Generic;
using CasinoMares.Player;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// Something the local cat can use with [E] when close enough: a seat, the cashier, another player (gifting).
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> All = new();

        [SerializeField] float _radius = 1.1f;

        public float Radius => _radius;

        /// <summary>Text for the HUD prompt, e.g. "Sit down". Return null to hide this interactable.</summary>
        public abstract string GetPrompt(PlayerCat player);

        /// <summary>When false the prompt is shown greyed out (e.g. seat taken) and [E] does nothing.</summary>
        public virtual bool CanInteract(PlayerCat player) => true;

        public abstract void Interact(PlayerCat player);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        protected virtual void OnEnable() => All.Add(this);

        protected virtual void OnDisable() => All.Remove(this);
    }
}
