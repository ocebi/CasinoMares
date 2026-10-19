using UnityEngine;

namespace CasinoMares.CameraRig
{
    /// <summary>
    /// Places a card table's overhead camera (GDD §3.5) for the screen it's on: pitched down toward the dealer, as
    /// close as it can be while the felt (a box in the table's local space) still fits the frame at the current
    /// aspect, so a phone held sideways gets a much closer view than a 16:9 monitor would need. It never rises above
    /// <see cref="_maxHeight"/>, and the lens stays as built. <see cref="Zoom"/> (pinch or the scroll wheel, see
    /// <see cref="PlayerCameraRig"/>) scales that distance.
    /// </summary>
    public class OverheadFraming : MonoBehaviour
    {
        public const float MinZoom = 0.7f;
        public const float MaxZoom = 1.3f;
        /// <summary>However far it's zoomed out, the camera stays under the hall's ceiling beams (3.3 m).</summary>
        const float CeilingHeight = 3.1f;

        [Tooltip("The felt, in the table's local space (x across, +z toward the dealer): the box the frame must show.")]
        [SerializeField] Vector3 _regionMin;
        [SerializeField] Vector3 _regionMax;
        [Tooltip("Degrees below horizontal the camera looks.")]
        [SerializeField] float _pitch = 70f;
        [Tooltip("Vertical field of view of the camera's lens (degrees).")]
        [SerializeField] float _fieldOfView = 50f;
        [Tooltip("Share of the frame the felt may fill, leaving a little air at the edges.")]
        [SerializeField] float _margin = 0.95f;
        [Tooltip("Highest the fitted camera may sit above the table's floor, in metres, before zooming.")]
        [SerializeField] float _maxHeight = 2.2f;

        float _zoom = 1f;
        float _placedAspect;
        float _placedZoom;

        /// <summary>The fitted distance times this: below 1 is closer.</summary>
        public float Zoom
        {
            get => _zoom;
            set => _zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
        }

        void OnEnable()
        {
            _placedAspect = 0f;
            Place();
        }

        void LateUpdate()
        {
            var aspect = ScreenAspect;
            if (!Mathf.Approximately(aspect, _placedAspect) || !Mathf.Approximately(_zoom, _placedZoom))
                Place();
        }

        static float ScreenAspect => Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;

        /// <summary>Moves the camera along its view line to the fitted distance for the current aspect and zoom.</summary>
        public void Place()
        {
            _placedAspect = ScreenAspect;
            _placedZoom = _zoom;
            var pitch = _pitch * Mathf.Deg2Rad;
            var view = new Vector3(0f, -Mathf.Sin(pitch), Mathf.Cos(pitch));
            var focus = new Vector3((_regionMin.x + _regionMax.x) / 2f, _regionMin.y, (_regionMin.z + _regionMax.z) / 2f);

            var distance = FittedDistance(focus, view, _placedAspect);
            var highest = (_maxHeight - focus.y) / Mathf.Sin(pitch);
            distance = Mathf.Min(distance, highest) * _zoom;
            distance = Mathf.Min(distance, (CeilingHeight - focus.y) / Mathf.Sin(pitch));

            transform.SetLocalPositionAndRotation(focus - view * distance, Quaternion.LookRotation(view, Vector3.up));
        }

        /// <summary>
        /// The least distance back along <paramref name="view"/> from <paramref name="focus"/> at which every corner of
        /// the region is inside the frame. Moving along the view line leaves each corner's sideways and up offsets as
        /// they are and only adds to its depth, so each corner needs depth ≥ offset / tan(half angle).
        /// </summary>
        float FittedDistance(Vector3 focus, Vector3 view, float aspect)
        {
            var rotation = Quaternion.LookRotation(view, Vector3.up);
            var up = rotation * Vector3.up;
            var right = rotation * Vector3.right;
            var tanVertical = Mathf.Tan(_fieldOfView * 0.5f * Mathf.Deg2Rad) * _margin;
            var tanHorizontal = tanVertical * aspect;

            var distance = 0f;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? _regionMin.x : _regionMax.x,
                    (corner & 2) == 0 ? _regionMin.y : _regionMax.y,
                    (corner & 4) == 0 ? _regionMin.z : _regionMax.z);
                var offset = point - focus;
                var depth = Vector3.Dot(offset, view);
                var needed = Mathf.Max(Mathf.Abs(Vector3.Dot(offset, right)) / tanHorizontal, Mathf.Abs(Vector3.Dot(offset, up)) / tanVertical);
                distance = Mathf.Max(distance, needed - depth);
            }

            return distance;
        }
    }
}
