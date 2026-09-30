using CasinoMares.Core;
using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Local player only. Finds the nearest usable <see cref="Interactable"/> and triggers it with [E].
    /// The HUD shows <see cref="Prompt"/>.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public static string Prompt { get; private set; }
        public static bool PromptEnabled { get; private set; }

        PlayerCat _cat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Prompt = null;
            PromptEnabled = false;
        }

        void Awake()
        {
            _cat = GetComponent<PlayerCat>();
        }

        void OnDisable()
        {
            if (_cat != null && _cat.IsLocal)
                Prompt = null;
        }

        void Update()
        {
            if (_cat == null || !_cat.IsLocal)
                return;

            // Seated: the table UI has its own Stand up button, so no world prompt.
            if (SeatingController.LocalTable != null)
            {
                Prompt = null;
                PromptEnabled = false;
                return;
            }

            var best = FindBest(out var prompt);
            Prompt = prompt;
            PromptEnabled = best != null && best.CanInteract(_cat);

            if (best != null && PromptEnabled && GameInput.InteractPressed())
                best.Interact(_cat);
        }

        Interactable FindBest(out string prompt)
        {
            Interactable best = null;
            prompt = null;
            var bestDistance = float.MaxValue;
            var position = transform.position;

            foreach (var interactable in Interactable.All)
            {
                if (interactable == null || interactable.transform.IsChildOf(transform))
                    continue;

                var offset = interactable.transform.position - position;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance > interactable.Radius || distance >= bestDistance)
                    continue;

                var candidatePrompt = interactable.GetPrompt(_cat);
                if (candidatePrompt == null)
                    continue;

                best = interactable;
                bestDistance = distance;
                prompt = candidatePrompt;
            }

            return best;
        }
    }
}
