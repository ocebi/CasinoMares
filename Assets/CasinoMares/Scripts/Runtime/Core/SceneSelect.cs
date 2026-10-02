using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasinoMares.Core
{
    /// <summary>
    /// The game ships two casinos: the original lounge (<c>Casino</c>) and the seaside one (<c>CasinoSea</c>). The
    /// first scene in the build is the default; <c>?scene=classic</c> or <c>?scene=sea</c> in the page URL opens the
    /// other one instead. Each has its own room (see ConnectionManager.SessionName).
    /// </summary>
    public static class SceneSelect
    {
        public const string ClassicScene = "Casino";
        public const string SeaScene = "CasinoSea";

        public static bool IsClassic(string sceneName) => sceneName == ClassicScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void LoadRequestedScene()
        {
            var wanted = Platform.QueryParameter("scene") switch
            {
                "classic" => ClassicScene,
                "sea" => SeaScene,
                _ => null,
            };

            if (wanted != null && wanted != SceneManager.GetActiveScene().name && Application.CanStreamedLevelBeLoaded(wanted))
                SceneManager.LoadScene(wanted);
        }
    }
}
