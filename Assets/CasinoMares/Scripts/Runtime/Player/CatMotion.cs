using UnityEngine;

namespace CasinoMares.Player
{
    /// <summary>
    /// Movement tuning shared by <see cref="CatMovement"/> (physics) and the scene builder (animation playback
    /// speeds), so the paws and the body always agree.
    /// </summary>
    public static class CatMotion
    {
        /// <summary>A gentle joystick push: the blend tree's walking clip (0.31 m/s at the pack's scale 1.6).</summary>
        public const float SlowWalkSpeed = 0.5f;
        /// <summary>A full push (or any key): the trot.</summary>
        public const float WalkSpeed = 1.4f;
        public const float RunSpeed = 2.6f;

        /// <summary>
        /// Input magnitude where the slow walk starts (matches the joystick's deadzone) and where the full walk speed is
        /// reached (matches where the stick starts to run), so speed grows smoothly with the push in between.
        /// </summary>
        public const float SlowWalkInput = 0.15f;
        public const float FullWalkInput = 0.9f;

        /// <summary>
        /// Fusion's <c>NetworkCharacterController.rotationSpeed</c>: a slerp factor per second toward the move direction.
        /// 16 turns the cat ~95% of the way in about 0.17 s at 60 ticks/s, so the camera (which follows the cat's facing)
        /// has a settled heading to aim for almost at once.
        /// </summary>
        public const float TurnSharpness = 16f;
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
