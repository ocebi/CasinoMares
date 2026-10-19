using UnityEditor.Animations;

namespace CasinoMares.Editor
{
    /// <summary>
    /// Swim R&D (plan §11.1): a Swim state in the cat animator made from an existing locomotion clip, played slower
    /// so it reads as a paddle. The legs will be under the surface, so the head and upper body sell it. Entered from
    /// Locomotion (or mid-jump) while "Swimming" is set (CatAnimatorDriver writes it from PlayerCat.Swimming).
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>The paddle clip. The alternative is "trotting": quicker, stronger strokes; flip it here to compare.</summary>
        const string SwimClip = "walking";
        /// <summary>Playback speed of the paddle clip.</summary>
        const float SwimClipSpeed = 0.7f;

        static void BuildSwimState(AnimatorController controller, AnimatorStateMachine machine, AnimatorState locomotion,
            AnimatorState jump, AnimatorState sit)
        {
            controller.AddParameter("Swimming", UnityEngine.AnimatorControllerParameterType.Bool);
            var swim = State(machine, "Swim", SwimClip, SwimClipSpeed);
            Transition(locomotion, swim, 0.3f, ("Swimming", AnimatorConditionMode.If));
            Transition(jump, swim, 0.2f, ("Swimming", AnimatorConditionMode.If));
            Transition(swim, locomotion, 0.3f, ("Swimming", AnimatorConditionMode.IfNot));
            Transition(swim, sit, 0.15f, ("Sitting", AnimatorConditionMode.If));
        }
    }
}
