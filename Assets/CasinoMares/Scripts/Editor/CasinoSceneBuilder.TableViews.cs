using Unity.Cinemachine;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The overhead table cameras (GDD §3.5): one fixed <see cref="CinemachineCamera"/> per card table, above the
    /// felt on the players' side, pitched down toward the dealer, framed so every seat, the dealer's cards and the
    /// chip circles fit on a phone held sideways. The pose is solved per table from the region it has to show: the
    /// camera backs away until the region fits a 16:9 frame (a 19.5:9 phone only sees more at the sides), and where
    /// that would put it up in the ceiling beams it stays under them and widens its lens instead.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        /// <summary>Degrees below horizontal the overhead camera looks (the plan allows 68–75°).</summary>
        const float OverheadPitch = 70f;
        /// <summary>Vertical field of view it starts from (the seat cameras use 52°)...</summary>
        const float OverheadFov = 50f;
        /// <summary>...and the widest it may go when the ceiling stops it backing away any further.</summary>
        const float OverheadMaxFov = 70f;
        /// <summary>Highest the camera may sit: under the ceiling beams (3.3 m) and the lamp cords.</summary>
        const float OverheadMaxHeight = 3.1f;
        /// <summary>Share of the frame the region may fill, leaving a little air at the edges.</summary>
        const float OverheadMargin = 0.95f;
        /// <summary>Metres above the felt the region reaches: the seated cats' heads.</summary>
        const float OverheadHeadroom = 0.4f;
        /// <summary>The narrowest phone shape held sideways that has to show the whole region.</summary>
        const float OverheadAspect = 16f / 9f;
        const float OverheadMinDistance = 1.5f;
        const float OverheadMaxDistance = 6f;
        const float OverheadDistanceStep = 0.05f;

        /// <summary>
        /// Builds a table's overhead camera and assigns it to the table's <c>_overheadCamera</c>. The region is an
        /// axis-aligned box in the table's local space (x across the table, +z toward the dealer) from
        /// <paramref name="regionMin"/> to <paramref name="regionMax"/>; the camera looks at its centre on the felt.
        /// </summary>
        static CinemachineCamera BuildOverheadCamera(Transform table, Object tableComponent, Vector3 regionMin, Vector3 regionMax)
        {
            var pitch = OverheadPitch * Mathf.Deg2Rad;
            var viewDirection = new Vector3(0f, -Mathf.Sin(pitch), Mathf.Cos(pitch));
            var focus = new Vector3(0f, regionMin.y, (regionMin.z + regionMax.z) / 2f);

            // Back away at the base lens until everything fits...
            var distance = OverheadMinDistance;
            var fov = OverheadFov;
            while (distance < OverheadMaxDistance && !RegionFits(focus - viewDirection * distance, viewDirection, fov, regionMin, regionMax))
                distance += OverheadDistanceStep;

            // ...but no higher than the ceiling allows: from there, widen the lens instead.
            var maxDistance = (OverheadMaxHeight - focus.y) / Mathf.Sin(pitch);
            if (distance > maxDistance)
            {
                distance = maxDistance;
                while (fov < OverheadMaxFov && !RegionFits(focus - viewDirection * distance, viewDirection, fov, regionMin, regionMax))
                    fov += 1f;
            }

            var camera = new GameObject("OverheadCamera").AddComponent<CinemachineCamera>();
            camera.transform.SetParent(table, false);
            camera.transform.localPosition = focus - viewDirection * distance;
            camera.transform.localRotation = Quaternion.LookRotation(viewDirection, Vector3.up);
            camera.Lens = LensSettings.Default;
            camera.Lens.FieldOfView = fov;
            camera.Lens.NearClipPlane = 0.05f;
            camera.Priority = 0;
            SetFields(tableComponent, ("_overheadCamera", camera));
            return camera;
        }

        /// <summary>Whether every corner of the region box projects inside a 16:9 frame at this lens, with the margin.</summary>
        static bool RegionFits(Vector3 cameraPosition, Vector3 viewDirection, float fov, Vector3 regionMin, Vector3 regionMax)
        {
            var rotation = Quaternion.LookRotation(viewDirection, Vector3.up);
            var up = rotation * Vector3.up;
            var right = rotation * Vector3.right;
            var tanVertical = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * OverheadMargin;
            var tanHorizontal = tanVertical * OverheadAspect;

            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? regionMin.x : regionMax.x,
                    (corner & 2) == 0 ? regionMin.y : regionMax.y,
                    (corner & 4) == 0 ? regionMin.z : regionMax.z);
                var offset = point - cameraPosition;
                var depth = Vector3.Dot(offset, viewDirection);
                if (depth < 0.2f)
                    return false;
                if (Mathf.Abs(Vector3.Dot(offset, right)) > depth * tanHorizontal)
                    return false;
                if (Mathf.Abs(Vector3.Dot(offset, up)) > depth * tanVertical)
                    return false;
            }

            return true;
        }
    }
}
