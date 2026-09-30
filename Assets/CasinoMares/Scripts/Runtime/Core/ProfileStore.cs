using System;
using System.IO;
using UnityEngine;

namespace CasinoMares.Core
{
    [Serializable]
    public class PlayerProfile
    {
        public string DisplayName = "";
        public int FurIndex;
        public int Balance = ProfileStore.StartingBalance;
    }

    /// <summary>
    /// The single local profile (GDD §3.2, §7.1), saved as JSON. The client is trusted, so this file is
    /// the source of truth for the player's balance.
    /// </summary>
    public static class ProfileStore
    {
        public const int StartingBalance = 1000;
        public const int MaxNameLength = 16;

        static PlayerProfile _current;

        public static PlayerProfile Current => _current ??= Load();

        public static event Action<PlayerProfile> Changed;

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        static string FileName
        {
            get
            {
                // "-profile name" keeps test instances (e.g. a -bot build) away from the real profile.
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, "-profile");
                if (index >= 0 && index + 1 < args.Length)
                    return $"profile_{args[index + 1]}.json";
#if UNITY_EDITOR
                // Multiplayer Play Mode virtual players share persistentDataPath with the main editor, but each
                // runs from its own Library/VP folder. Give them separate profiles so they don't overwrite each other.
                if (Application.dataPath.Contains("/Library/VP/"))
                    return $"profile_{Application.dataPath.GetHashCode():X8}.json";
#endif
                return "profile.json";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _current = null;
            Changed = null;
        }

        static PlayerProfile Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var profile = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(FilePath));
                    if (profile != null)
                        return profile;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProfileStore] Could not read {FilePath}, starting fresh: {e.Message}");
            }

            return new PlayerProfile();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProfileStore] Could not write {FilePath}: {e.Message}");
            }
        }

        public static void SetIdentity(string displayName, int furIndex)
        {
            Current.DisplayName = SanitizeName(displayName);
            Current.FurIndex = furIndex;
            Save();
            Changed?.Invoke(Current);
        }

        public static void SetBalance(int balance)
        {
            if (Current.Balance == balance)
                return;

            Current.Balance = balance;
            Save();
            Changed?.Invoke(Current);
        }

        public static string SanitizeName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > MaxNameLength)
                name = name.Substring(0, MaxNameLength);
            return name.Length == 0 ? "Cat" : name;
        }
    }
}
