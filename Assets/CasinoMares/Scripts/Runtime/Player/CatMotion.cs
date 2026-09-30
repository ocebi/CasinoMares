using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Movement tuning shared by <see cref="CatMovement"/> (physics) and the scene builder (animation playback
    /// speeds), so the paws and the body always agree.
    /// </summary>
    public static class CatMotion
    {
        public const float WalkSpeed = 1.4f;
        public const float RunSpeed = 2.6f;
        public const float JumpHeight = 1.0f;

        /// <summary>Gravity on the way up and on the ground.</summary>
        public const float RiseGravity = -18f;

        /// <summary>Gentler gravity on the way down, so cats float a little before landing.</summary>
        public const float FallGravity = -10f;

        public const float MaxFallSpeed = 6f;

        public static float JumpImpulse => Mathf.Sqrt(2f * JumpHeight * -RiseGravity);

        /// <summary>Seconds in the air for a jump on flat ground.</summary>
        public static float JumpAirTime => JumpImpulse / -RiseGravity + Mathf.Sqrt(2f * JumpHeight / -FallGravity);
    }
}
