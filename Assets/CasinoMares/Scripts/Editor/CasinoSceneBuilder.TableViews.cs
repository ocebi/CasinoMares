using CasinoMares.CameraRig;
using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The overhead table cameras (GDD §3.5): one <see cref="CinemachineCamera"/> per card table, above the felt on the
    /// players' side, pitched down toward the dealer. <see cref="OverheadFraming"/> places it at runtime for the screen's
    /// aspect, as close as the felt allows (not the stools: a phone held sideways then sees the cards large), at most
    /// <see cref="OverheadMaxHeight"/> high, with a fixed lens. Pinch or the scroll wheel zooms it (PlayerCameraRig).
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>Degrees below horizontal the overhead camera looks (the plan allows 68–75°).</summary>
        const float OverheadPitch = 70f;
        /// <summary>Its vertical field of view, the same on every screen (the seat cameras use 52°).</summary>
        const float OverheadFov = 50f;
        /// <summary>Highest the fitted camera may sit above the floor; zooming out may take it a little higher.</summary>
        const float OverheadMaxHeight = 2.2f;
        /// <summary>Share of the frame the felt may fill, leaving a little air at the edges.</summary>
        const float OverheadMargin = 0.95f;
        /// <summary>Metres above the felt the framed box reaches: the cards and chip stacks lying on it.</summary>
        const float OverheadHeadroom = 0.05f;

        /// <summary>
        /// Builds a table's overhead camera and assigns it to the table's <c>_overheadCamera</c>. The felt is an
        /// axis-aligned box in the table's local space (x across the table, +z toward the dealer) from
        /// <paramref name="feltMin"/> to <paramref name="feltMax"/>; the camera looks at its centre.
        /// </summary>
        static CinemachineCamera BuildOverheadCamera(Transform table, Object tableComponent, Vector3 feltMin, Vector3 feltMax)
        {
            var camera = new GameObject("OverheadCamera").AddComponent<CinemachineCamera>();
            camera.transform.SetParent(table, false);
            camera.Lens = LensSettings.Default;
            camera.Lens.FieldOfView = OverheadFov;
            camera.Lens.NearClipPlane = 0.05f;
            camera.Priority = 0;

            var framing = camera.gameObject.AddComponent<OverheadFraming>();
            SetVector3(framing, "_regionMin", feltMin);
            SetVector3(framing, "_regionMax", feltMax);
            SetFloat(framing, "_pitch", OverheadPitch);
            SetFloat(framing, "_fieldOfView", OverheadFov);
            SetFloat(framing, "_margin", OverheadMargin);
            SetFloat(framing, "_maxHeight", OverheadMaxHeight);
            // A pose for the scene as saved; it's placed again for the real screen when it runs.
            framing.Place();

            SetFields(tableComponent, ("_overheadCamera", camera));
            return camera;
        }

        static void SetVector3(Object target, string field, Vector3 value)
        {
            var serialized = new UnityEditor.SerializedObject(target);
            serialized.FindProperty(field).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
