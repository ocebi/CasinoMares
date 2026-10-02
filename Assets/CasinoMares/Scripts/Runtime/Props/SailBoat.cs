using System;
using UnityEngine;

namespace CasinoMares.Props
{
    /// <summary>
    /// A sailboat going round a long loop out at sea, scenery for the seaside casino. Its place on the loop comes
    /// from the UTC clock, so every player sees the same boats in the same places without any networking. It heels
    /// away from the wind and rocks gently on the swell.
    /// </summary>
    public class SailBoat : MonoBehaviour
    {
        [Tooltip("The middle of the loop, on the water.")]
        [SerializeField] Vector3 _center;
        [Tooltip("Half the loop's size across x and z, in metres.")]
        [SerializeField] Vector2 _radii = new(80f, 200f);
        [Tooltip("Seconds for one lap.")]
        [SerializeField] float _lapSeconds = 400f;
        [Tooltip("Where on the loop the boat is at a lap's start (0..1), so boats sharing a loop spread out.")]
        [Range(0f, 1f)] [SerializeField] float _phase;
        [SerializeField] bool _clockwise;
        [Tooltip("Degrees the boat leans over.")]
        [SerializeField] float _heel = 8f;
        [Tooltip("Degrees of rocking on the swell.")]
        [SerializeField] float _rock = 2.5f;
        [Tooltip("Metres the boat rises and falls on the swell.")]
        [SerializeField] float _bob = 0.15f;
        [Tooltip("The foam trail: follows the boat but stays flat on the water while the boat heels and bobs.")]
        [SerializeField] Transform _wake;

        void OnEnable() => Place();

        void Update() => Place();

        /// <summary>Puts the boat (and its wake) where it is on its loop right now.</summary>
        public void Place()
        {
            var seconds = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
            var lap = (float)((seconds / _lapSeconds + _phase) % 1.0);
            var turn = _clockwise ? -1f : 1f;
            var angle = lap * Mathf.PI * 2f * turn;
            var offset = new Vector3(Mathf.Cos(angle) * _radii.x, 0f, Mathf.Sin(angle) * _radii.y);
            var heading = new Vector3(-Mathf.Sin(angle) * _radii.x, 0f, Mathf.Cos(angle) * _radii.y) * turn;

            var time = Time.time + _phase * 17f;
            var bob = Vector3.up * (Mathf.Sin(time * 0.9f) * _bob);
            // The sails are let out to port, so the wind comes over starboard and the boat leans to port.
            var sway = Quaternion.Euler(Mathf.Sin(time * 0.7f) * _rock * 0.5f, 0f, _heel + Mathf.Sin(time * 0.6f + 1f) * _rock);
            var facing = Quaternion.LookRotation(heading.normalized);
            transform.SetPositionAndRotation(_center + offset + bob, facing * sway);
            if (_wake != null)
                _wake.SetPositionAndRotation(_center + offset, facing);
        }
    }
}
