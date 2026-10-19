using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace CasinoMares.Core
{
    /// <summary>
    /// The only code that talks to Addressables. The big art the game can start without (the recolored cat furs, the
    /// sky photo, the far islands) ships as bundles next to the web build (GDD §15.1) instead of in the data file, which
    /// keeps that file under jsDelivr's 20 MB limit. Each asset loads once and stays loaded for the session. A failed
    /// load is logged and never retried: callers keep their fallback (the black fur, the blurred sky, no islands).
    /// Phones load the ASTC bundles from <c>aa_Mobile</c>, desktop browsers the DXT ones from <c>aa</c>.
    /// </summary>
    public static class LazyContent
    {
        /// <summary>The folder the mobile (ASTC) pass's bundles and catalog ship in, next to the desktop <c>aa</c>.</summary>
        public const string MobileFolder = "aa_Mobile";

        static readonly Dictionary<string, AsyncOperationHandle> Loads = new();
        static readonly Dictionary<string, Object> Loaded = new();

        /// <summary>Loads started and not yet finished (succeeded or failed), for <see cref="AutoRenderScale"/>.</summary>
        public static int InFlight { get; private set; }

        /// <summary>When the last load finished (<c>Time.unscaledTime</c>); minus infinity before any has.</summary>
        public static float LastFinishedAt { get; private set; } = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            Loads.Clear();
            Loaded.Clear();
            InFlight = 0;
            LastFinishedAt = float.NegativeInfinity;
            if (Platform.IsWeb && Platform.TextureFormat == "ASTC")
                Addressables.InternalIdTransformFunc = location => location.InternalId.Replace("/StreamingAssets/aa/", $"/StreamingAssets/{MobileFolder}/");
        }

        /// <summary>The asset at <paramref name="address"/> if it has finished loading, else null.</summary>
        public static T Get<T>(string address) where T : Object
        {
            return !string.IsNullOrEmpty(address) && Loaded.TryGetValue(address, out var asset) ? asset as T : null;
        }

        /// <summary>
        /// Calls <paramref name="ready"/> with the asset once it's loaded (at once if it already is); never on a
        /// failure. Repeated calls share one load.
        /// </summary>
        public static void Load<T>(string address, Action<T> ready = null) where T : Object
        {
            // The scene builder sets furs in edit mode, where Addressables isn't running: callers keep the fallback.
            if (string.IsNullOrEmpty(address) || !Application.isPlaying)
                return;
            if (Loaded.TryGetValue(address, out var asset))
            {
                if (asset is T loaded)
                    ready?.Invoke(loaded);
                return;
            }

            if (!Loads.TryGetValue(address, out var handle))
            {
                handle = Addressables.LoadAssetAsync<T>(address);
                Loads[address] = handle;
                InFlight++;
                handle.Completed += done =>
                {
                    InFlight--;
                    LastFinishedAt = Time.unscaledTime;
                    if (done.Status == AsyncOperationStatus.Succeeded && done.Result is T result)
                        Loaded[address] = result;
                    else
                        Debug.LogWarning($"[LazyContent] Could not load {address}: {done.OperationException?.Message}");
                };
            }

            if (ready != null)
                handle.Completed += done =>
                {
                    if (done.Status == AsyncOperationStatus.Succeeded && done.Result is T result)
                        ready(result);
                };
        }
    }
}
