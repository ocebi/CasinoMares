using CasinoMares.CameraRig;
using CasinoMares.Core;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace CasinoMares.UI
{
    /// <summary>
    /// The mouse cursor: a cat's paw that closes while the left button is down, and arrows circling round while a
    /// right-drag turns the camera. In a browser the page draws them (with sharper versions for high-DPI screens, see
    /// TemplateData/style.css); elsewhere they're hardware cursors from Resources/Cursors. Both are drawn by
    /// Tools/make_cursors.py. Touch play has no cursor.
    /// </summary>
    public class CursorStyle : MonoBehaviour
    {
        enum Kind
        {
            None = -1,
            Paw = 0,
            Rotate = 1,
            PawClosed = 2,
        }

        /// <summary>A quick click still shows the closed paw for this long.</summary>
        const float ClosedAtLeast = 0.15f;

        // Where the cursor points, in pixels from its top left: the front toe (open or closed, the pad stays put), and
        // the middle of the circle.
        static readonly Vector2 PawHotspot = new(6f, 5f);
        static readonly Vector2 RotateHotspot = new(16f, 16f);

        Texture2D _paw;
        Texture2D _pawClosed;
        Texture2D _rotate;
        Kind _shown = Kind.None;
        float _closedUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Platform.IsTouch)
                return;
            var cursor = new GameObject("CursorStyle");
            DontDestroyOnLoad(cursor);
            cursor.AddComponent<CursorStyle>();
        }

        void Awake()
        {
            _paw = Resources.Load<Texture2D>("Cursors/Paw");
            _pawClosed = Resources.Load<Texture2D>("Cursors/PawClosed");
            _rotate = Resources.Load<Texture2D>("Cursors/Rotate");
        }

        void OnDestroy()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
#endif
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
                _closedUntil = Time.unscaledTime + ClosedAtLeast;
            var kind = TurningCamera() ? Kind.Rotate : Time.unscaledTime < _closedUntil ? Kind.PawClosed : Kind.Paw;
            if (kind == _shown)
                return;

            _shown = kind;
#if UNITY_WEBGL && !UNITY_EDITOR
            CM_SetCursor((int)kind);
#else
            if (kind == Kind.Rotate)
                Cursor.SetCursor(_rotate, RotateHotspot, CursorMode.Auto);
            else
                Cursor.SetCursor(kind == Kind.PawClosed ? _pawClosed : _paw, PawHotspot, CursorMode.Auto);
#endif
        }

        /// <summary>The right button is held in game, where it turns the camera (walking about, or at a table).</summary>
        static bool TurningCamera()
        {
            var mouse = Mouse.current;
            var rig = PlayerCameraRig.Instance;
            return mouse != null && mouse.rightButton.isPressed && GameInput.GameplayEnabled && !GameInput.LookBlocked &&
                   rig != null && rig.IsFollowing;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CM_SetCursor(int kind);
#endif
    }
}
