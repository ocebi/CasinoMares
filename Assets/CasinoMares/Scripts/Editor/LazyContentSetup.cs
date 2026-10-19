using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace CasinoMares.Editor
{
    /// <summary>
    /// The Addressables groups behind <see cref="CasinoMares.Core.LazyContent"/>, set up from code so they're
    /// reproducible (Casino Mares ▸ Setup ▸ Set Up Lazy Content; <see cref="WebBuild"/> runs it before every build).
    /// The bundles build with the player into StreamingAssets/aa. Each fur is its own bundle, so a cat downloads only its
    /// own; the normal map the furs share is a bundle of its own so it isn't copied into each. The scenes keep referencing
    /// the sky photo and the islands; <see cref="LazySceneProcessor"/> takes them out of the built scenes.
    /// </summary>
    public static class LazyContentSetup
    {
        const string ArtRoot = "Assets/CasinoMares/Art";
        const string CatPack = "Assets/ThirdParty/BlackCatAnimated";

        /// <summary>The sky photo the sea casino's sky, water and land materials sample.</summary>
        public const string SkyPhotoPath = "Assets/ThirdParty/PolyHaven/industrial_sunset_02_puresky.png";

        /// <summary>The far islands and coast: big meshes nobody walks on, so the scene can show the sea without them a moment.</summary>
        static readonly string[] LandscapeMeshes =
        {
            "Sea_FarCoast", "Sea_Island_Chapel", "Sea_Island_Cliffs", "Sea_Island_North", "Sea_Island_Village",
        };

        static readonly string[] Furs = { "Ginger", "Snow", "Smoke", "Cream", "Cocoa" };

        [MenuItem("Casino Mares/Setup/Set Up Lazy Content")]
        public static void SetUp()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            settings.BuildRemoteCatalog = false;
            // Play mode reads the assets straight from the project.
            settings.ActivePlayModeDataBuilderIndex = settings.DataBuilders.FindIndex(builder => builder is BuildScriptFastMode);

            var furs = Group(settings, "CatFur", BundledAssetGroupSchema.BundlePackingMode.PackSeparately);
            foreach (var fur in Furs)
                Entry(settings, furs, $"{ArtRoot}/Materials/CatFur_{fur}.mat");
            Entry(settings, Group(settings, "CatShared", BundledAssetGroupSchema.BundlePackingMode.PackTogether),
                $"{CatPack}/Textures/Cat_N.png");
            Entry(settings, Group(settings, "Sky", BundledAssetGroupSchema.BundlePackingMode.PackTogether), SkyPhotoPath);
            var landscape = Group(settings, "Landscape", BundledAssetGroupSchema.BundlePackingMode.PackTogether);
            foreach (var mesh in LandscapeMeshes)
                Entry(settings, landscape, $"{ArtRoot}/Meshes/{mesh}.asset");

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Addresses by asset GUID for the assets the built scenes load lazily (the sky photo and the islands).</summary>
        public static Dictionary<string, string> SceneAddresses()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var addresses = new Dictionary<string, string>();
            if (settings == null)
                return addresses;
            foreach (var name in new[] { "Sky", "Landscape" })
            {
                var group = settings.FindGroup(name);
                if (group != null)
                    foreach (var entry in group.entries)
                        addresses[entry.guid] = entry.address;
            }

            return addresses;
        }

        static AddressableAssetGroup Group(AddressableAssetSettings settings, string name, BundledAssetGroupSchema.BundlePackingMode packing)
        {
            var group = settings.FindGroup(name)
                ?? settings.CreateGroup(name, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            schema.BundleMode = packing;
            schema.UseAssetBundleCache = true;
            schema.UseAssetBundleCrc = true;
            schema.UseAssetBundleCrcForCachedBundles = false;
            schema.IncludeInBuild = true;
            schema.IncludeGUIDInCatalog = false;
            schema.IncludeLabelsInCatalog = false;
            EditorUtility.SetDirty(schema);
            EditorUtility.SetDirty(group);
            return group;
        }

        /// <summary>Adds the asset with its file name as the address.</summary>
        static void Entry(AddressableAssetSettings settings, AddressableAssetGroup group, string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[LazyContent] No asset at {path}; run Casino Mares ▸ Setup ▸ Rebuild Sea Casino first.");
                return;
            }

            settings.CreateOrMoveEntry(guid, group, false, false).SetAddress(Path.GetFileNameWithoutExtension(path), false);
        }
    }
}
