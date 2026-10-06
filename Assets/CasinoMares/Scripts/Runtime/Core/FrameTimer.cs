using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace CasinoMares.Core
{
    /// <summary>
    /// Times each frame's work on the main thread, from the start of the player loop to the end of rendering. In a
    /// browser the rest of the frame is spent waiting for the next one, which the GPU can hold up: a low frame rate with
    /// little CPU time means the GPU is what's slow. Whatever needs the numbers installs it (<see cref="DebugOverlay"/>,
    /// <see cref="AutoRenderScale"/>).
    /// </summary>
    public static class FrameTimer
    {
        struct FrameStart { }
        struct FrameEnd { }

        static double _frameStartedAt;

        /// <summary>Main-thread seconds spent on frames so far (with <see cref="FrameCount"/>, for averages).</summary>
        public static double CpuSeconds { get; private set; }

        /// <summary>Frames timed so far.</summary>
        public static int FrameCount { get; private set; }

        /// <summary>Starts timing frames; calling it again does nothing.</summary>
        public static void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                var phase = loop.subSystemList[i];
                var systems = new List<PlayerLoopSystem>(phase.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                if (systems.Exists(system => system.type == typeof(FrameStart)))
                    return;
                if (phase.type == typeof(Initialization))
                    systems.Insert(0, new PlayerLoopSystem { type = typeof(FrameStart), updateDelegate = () => _frameStartedAt = Time.realtimeSinceStartupAsDouble });
                else if (phase.type == typeof(PostLateUpdate))
                    systems.Add(new PlayerLoopSystem { type = typeof(FrameEnd), updateDelegate = EndFrame });
                else
                    continue;
                phase.subSystemList = systems.ToArray();
                loop.subSystemList[i] = phase;
            }

            PlayerLoop.SetPlayerLoop(loop);
        }

        static void EndFrame()
        {
            CpuSeconds += Time.realtimeSinceStartupAsDouble - _frameStartedAt;
            FrameCount++;
        }
    }
}
