using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace CasinoMares.Core
{
    /// <summary>
    /// <c>?debug=1</c> in the page URL shows a line of render stats in the top-left corner: frame rate, quality level,
    /// texture format, render size, HDR and the GPU. It's for checking how the game looks and runs on a phone.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        const float SampleSeconds = 0.5f;

        GUIStyle _style;
        string _text = "";
        int _frames;
        float _elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Platform.QueryParameter("debug") != "1")
                return;

            var overlay = new GameObject("DebugOverlay");
            DontDestroyOnLoad(overlay);
            overlay.AddComponent<DebugOverlay>();
        }

        void Update()
        {
            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < SampleSeconds)
                return;

            var fps = _frames / _elapsed;
            _frames = 0;
            _elapsed = 0f;

            var pipeline = UniversalRenderPipeline.asset;
            var scale = pipeline != null ? pipeline.renderScale : 1f;
            // URP quietly renders without HDR where the GPU can't render to a float format.
            var hdr = pipeline != null && pipeline.supportsHDR &&
                      SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Render);
            _text = $"{fps:0} fps  {QualitySettings.names[QualitySettings.GetQualityLevel()]}  {Platform.TextureFormat}  " +
                    $"{Screen.width}x{Screen.height} x{scale:0.##}  HDR {(hdr ? "on" : "off")}  {SystemInfo.graphicsDeviceName}";
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft };
            _style.fontSize = Mathf.Max(14, Screen.height / 45);
            var size = _style.CalcSize(new GUIContent(_text));
            GUI.Box(new Rect(8f, 8f, size.x, size.y), _text, _style);
        }
    }
}
