using System.Collections.Generic;
using System.IO;
using CasinoMares.Audio;
using CasinoMares.Baccarat;
using CasinoMares.Blackjack;
using CasinoMares.CameraRig;
using CasinoMares.Core;
using CasinoMares.Networking;
using CasinoMares.Npc;
using CasinoMares.Player;
using CasinoMares.Poker;
using CasinoMares.Props;
using CasinoMares.Roulette;
using CasinoMares.Rules;
using CasinoMares.Slots;
using CasinoMares.Social;
using CasinoMares.UI;
using Fusion;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CasinoMares.Editor
{
    /// <summary>
    /// Builds every generated asset (fur palette, cat animator, player and NPC prefabs, materials, UI panel
    /// settings) and the whole Casino scene from code, so the setup is reproducible and reviewable.
    /// Re-running overwrites the generated assets and the scene. The seaside casino is the same building with its
    /// own look and a terrace over the sea (CasinoSceneBuilder.Sea.cs); it has its own scene and themed assets, so
    /// rebuilding one never changes the other.
    /// </summary>
    public static partial class CasinoSceneBuilder
    {
        const string Root = "Assets/CasinoMares";
        const string CatPack = "Assets/ThirdParty/BlackCatAnimated";
        const string CatModelPath = CatPack + "/Animations/cat@idle.FBX";
        const string CatRigRoot = "GeneralController";
        const string FurTextureFolder = Root + "/Art/Textures";
        static string ScenePath => _sea ? SeaScenePath : ClassicScenePath;
        static string NavMeshPath => _sea ? Root + "/Scenes/CasinoSea_NavMesh.asset" : Root + "/Scenes/Casino_NavMesh.asset";
        const string PlayerPrefabPath = Root + "/Prefabs/PlayerCat.prefab";
        const string NpcPrefabPath = Root + "/Prefabs/NpcCat.prefab";
        const string ControllerPath = Root + "/Art/Animation/Cat.controller";
        const string PalettePath = Root + "/Settings/CatFurPalette.asset";
        const string PanelSettingsPath = Root + "/Settings/PanelSettings.asset";
        const string BoardPanelSettingsPath = Root + "/Settings/BoardPanelSettings.asset";
        const string BoardTexturePath = Root + "/Settings/LeaderboardBoard.renderTexture";
        static string VolumeProfilePath => _sea ? Root + "/Settings/CasinoSeaVolumeProfile.asset" : Root + "/Settings/CasinoVolumeProfile.asset";
        const string SoundBankPath = Root + "/Settings/SoundBank.asset";
        const string KenneyCasino = "Assets/ThirdParty/Kenney/CasinoAudio";
        const string KenneyInterface = "Assets/ThirdParty/Kenney/InterfaceSounds";

        // The pack's cat is ~0.36 m tall; scale it up so the hall, tables and camera work at a comfortable size.
        const float CatScale = 1.6f;

        // Every clip in the pack is its own FBX (cat@<name>.FBX). idle comes first: it creates the shared avatar.
        static readonly string[] CatClipNames =
        {
            "idle", "walking", "trotting", "running", "sneaking", "jumping", "sitting", "getup", "meow", "licking",
            "eating", "attack", "attack2", "hited", "died", "turnleft", "turnright",
        };

        static readonly HashSet<string> LoopingCatClips = new() { "idle", "walking", "trotting", "running", "sneaking", "licking", "turnleft", "turnright" };
        const float HallWidth = 22f;
        const float HallDepth = 14f;
        /// <summary>The entrance doorway in the south wall, over the carpet runner (NPC players come and go here).</summary>
        const float DoorX = 4.8f;
        const float DoorWidth = 1.6f;
        const float DoorHeight = 2.4f;
        const float CeilingHeight = 3.5f;
        const string ConeMeshPath = Root + "/Art/Meshes/LightCone.asset";
        /// <summary>
        /// Invisible colliders only the follow camera runs into (the entrance doorway, the ceiling). Cats, props and the
        /// NavMesh pass through them; they keep a far-out camera from ending up behind the curtain or above the roof.
        /// </summary>
        const int CameraBlockerLayer = 6;
        const string CameraBlockerLayerName = "CameraBlocker";
        /// <summary>
        /// The opposite: invisible walls that stop cats (and props) but not the follow camera, which can swing out
        /// past them (the terrace railing).
        /// </summary>
        const int CatBarrierLayer = 7;
        const string CatBarrierLayerName = "CatBarrier";
        static string ReflectionPath => _sea ? Root + "/Scenes/CasinoSea_Reflection.exr" : Root + "/Scenes/Casino_Reflection.exr";

        [MenuItem("Casino Mares/Setup/Rebuild Casino (Assets and Scene)")]
        public static void RebuildAll()
        {
            ConfigureCatImport();
            ConfigureRendering();
            ConfigureLayers();
            var palette = BuildFurPalette();
            var controller = BuildCatController();
            BuildNpcPrefab(palette, controller);
            BuildPlayerPrefab(palette, controller);
            BuildPanelSettings();
            BuildBoardPanelSettings();
            BuildVolumeProfile();
            BuildSoundBank();
            AssetDatabase.SaveAssets();

            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[CasinoSceneBuilder] Rebuilt casino assets and scene.");
        }

        /// <summary>Rebuilds only the player prefab (keeps its GUID, so the scene reference stays valid).</summary>
        [MenuItem("Casino Mares/Setup/Rebuild Player Prefab")]
        public static void RebuildPlayerPrefab()
        {
            BuildPlayerPrefab(Load<CatFurPalette>(PalettePath), Load<AnimatorController>(ControllerPath));
            AssetDatabase.SaveAssets();
            Debug.Log("[CasinoSceneBuilder] Rebuilt player prefab.");
        }

        // ================================================================ Assets

        static string CatClipPath(string clip) => $"{CatPack}/Animations/cat@{clip}.FBX";

        /// <summary>
        /// Import settings for the Black Cat Animated clips: one shared Generic avatar and no root-motion node, so the
        /// clips play exactly as authored. They move the rig root forward (walks drift, sitting starts offset);
        /// CatRigAnchor pins it back over the cat's origin every frame and CatMovement moves the cat.
        /// </summary>
        static void ConfigureCatImport()
        {
            Avatar avatar = null;
            foreach (var name in CatClipNames)
            {
                var path = CatClipPath(name);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var configured = string.IsNullOrEmpty(importer.motionNodeName) && importer.clipAnimations.Length == 1 &&
                                 importer.clipAnimations[0].name == name &&
                                 importer.materialImportMode == ModelImporterMaterialImportMode.None &&
                                 (avatar == null ? importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel : importer.sourceAvatar == avatar);
                if (!configured)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.motionNodeName = "";
                    if (avatar == null)
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    }
                    else
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                        importer.sourceAvatar = avatar;
                    }

                    var clip = importer.defaultClipAnimations[0];
                    clip.name = name;
                    clip.loopTime = LoopingCatClips.Contains(name);
                    clip.loopPose = false;
                    clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                    clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
                    // The idle loop opens its mouth halfway; CatAnimationEvents sometimes meows there.
                    clip.events = name == "idle"
                        ? new[] { new AnimationEvent { functionName = "AnimalSound", time = 0.5f } }
                        : System.Array.Empty<AnimationEvent>();
                    importer.clipAnimations = new[] { clip };
                    importer.SaveAndReimport();
                }

                if (avatar == null)
                {
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (asset is Avatar found)
                            avatar = found;
                    }
                }
            }
        }

        static void ConfigureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            foreach (var (index, name) in new[] { (CameraBlockerLayer, CameraBlockerLayerName), (CatBarrierLayer, CatBarrierLayerName) })
                layers.GetArrayElementAtIndex(index).stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();

            for (var other = 0; other < 32; other++)
                Physics.IgnoreLayerCollision(CameraBlockerLayer, other, true);
        }

        /// <summary>Stronger, wider ambient occlusion on the PC renderer so the props and cats sit in their pools of light.</summary>
        static void ConfigureRendering()
        {
            var renderer = Load<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
            if (renderer == null)
                return;
            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature == null || feature.name != "ScreenSpaceAmbientOcclusion")
                    continue;
                var serialized = new SerializedObject(feature);
                serialized.FindProperty("m_Settings.Intensity").floatValue = 0.9f;
                serialized.FindProperty("m_Settings.Radius").floatValue = 0.4f;
                serialized.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.35f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static CatFurPalette BuildFurPalette()
        {
            // The pack only has a black cat, so the other fur colors are recolored copies of its texture.
            // "shade" is how dark the darkest fur gets, relative to the fur color.
            var furSource = LoadFurSource(out var furSize);
            var options = new List<(string name, Color fur, float shade, Color swatch)>
            {
                ("Midnight", Color.clear, 0f, new Color(0.16f, 0.15f, 0.17f)),
                ("Ginger", new Color(0.93f, 0.55f, 0.22f), 0.45f, new Color(0.93f, 0.55f, 0.22f)),
                ("Snow", new Color(0.96f, 0.95f, 0.93f), 0.7f, new Color(0.95f, 0.94f, 0.92f)),
                ("Smoke", new Color(0.58f, 0.6f, 0.66f), 0.5f, new Color(0.55f, 0.57f, 0.62f)),
                ("Cream", new Color(1f, 0.87f, 0.68f), 0.62f, new Color(0.98f, 0.85f, 0.66f)),
                ("Cocoa", new Color(0.55f, 0.36f, 0.25f), 0.45f, new Color(0.48f, 0.3f, 0.2f)),
            };

            var palette = LoadOrCreate<CatFurPalette>(PalettePath);
            var serialized = new SerializedObject(palette);
            var array = serialized.FindProperty("_options");
            array.arraySize = options.Count;
            for (var i = 0; i < options.Count; i++)
            {
                var (name, fur, shade, swatch) = options[i];
                var texture = fur.a > 0f
                    ? RecolorFur(furSource, furSize, fur, shade, name)
                    : Load<Texture2D>($"{CatPack}/Textures/Cat_D.png");

                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Name").stringValue = name;
                element.FindPropertyRelative("Material").objectReferenceValue = FurMaterial(name, texture);
                element.FindPropertyRelative("Swatch").colorValue = swatch;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return palette;
        }

        static Material FurMaterial(string name, Texture2D baseMap)
        {
            var material = MaterialAsset($"CatFur_{name}", "Universal Render Pipeline/Lit");
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", Load<Texture2D>($"{CatPack}/Textures/Cat_N.png"));
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The black fur texture, read from the PNG and halved to 1024 (the size the game uses).</summary>
        static Color32[] LoadFurSource(out int size)
        {
            var full = new Texture2D(2, 2);
            full.LoadImage(File.ReadAllBytes($"{CatPack}/Textures/Cat_D.png"));
            var source = full.GetPixels32();
            var fullSize = full.width;
            Object.DestroyImmediate(full);

            size = fullSize / 2;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var p = source[(y * 2 + dy) * fullSize + x * 2 + dx];
                            r += p.r;
                            g += p.g;
                            b += p.b;
                            a += p.a;
                        }
                    }

                    pixels[y * size + x] = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), (byte)(a / 4));
                }
            }

            return pixels;
        }

        /// <summary>
        /// Recolors the dark, unsaturated fur pixels to <paramref name="fur"/>, keeping their light/dark detail.
        /// Eyes, teeth, claws, mouth and paw pads are brighter or more saturated, so they keep their colors.
        /// </summary>
        static Texture2D RecolorFur(Color32[] source, int size, Color fur, float shade, string name)
        {
            static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            static float FurAmount(Color c)
            {
                var chroma = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.34f, Luminance(c)))) *
                       (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.14f, chroma)));
            }

            // Stretch the fur's own luminance range (5th to 95th percentile) over shade..highlight.
            var furLuminance = new List<float>();
            for (var i = 0; i < source.Length; i += 7)
            {
                if (FurAmount(source[i]) > 0.9f)
                    furLuminance.Add(Luminance(source[i]));
            }

            furLuminance.Sort();
            var low = furLuminance[furLuminance.Count / 20];
            var high = furLuminance[furLuminance.Count * 19 / 20];

            var eyes = EyeMask(source, size);
            var pixels = new Color32[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                Color original = source[i];
                if (eyes[i])
                {
                    pixels[i] = source[i];
                    continue;
                }

                var brightness = Mathf.Lerp(shade, 1.1f, Mathf.InverseLerp(low, high, Luminance(original)));
                var recolored = new Color(fur.r * brightness, fur.g * brightness, fur.b * brightness, original.a);
                pixels[i] = Color.Lerp(original, recolored, FurAmount(original));
            }

            if (!AssetDatabase.IsValidFolder(FurTextureFolder))
                AssetDatabase.CreateFolder(Root + "/Art", "Textures");

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            var path = $"{FurTextureFolder}/CatFur_{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = size;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return Load<Texture2D>(path);
        }

        /// <summary>
        /// The irises are the only green in the texture. Protect them and the black slit pupils inside them (the
        /// short gaps between iris pixels on a row) from recoloring, plus a 1 px edge.
        /// </summary>
        static bool[] EyeMask(Color32[] source, int size)
        {
            var iris = new bool[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                Color c = source[i];
                var chroma = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                iris[i] = c.g > c.r + 0.06f && c.g >= c.b - 0.02f && chroma > 0.1f && c.g > 0.2f;
            }

            var maxPupilWidth = Mathf.Max(4, size / 80);
            var mask = new bool[source.Length];
            for (var y = 0; y < size; y++)
            {
                var lastIris = -1;
                for (var x = 0; x < size; x++)
                {
                    if (!iris[y * size + x])
                        continue;
                    if (lastIris >= 0 && x - lastIris <= maxPupilWidth)
                    {
                        for (var fill = lastIris; fill < x; fill++)
                            mask[y * size + fill] = true;
                    }

                    mask[y * size + x] = true;
                    lastIris = x;
                }
            }

            var edged = (bool[])mask.Clone();
            for (var y = 1; y < size - 1; y++)
            {
                for (var x = 1; x < size - 1; x++)
                {
                    var i = y * size + x;
                    edged[i] |= mask[i - 1] || mask[i + 1] || mask[i - size] || mask[i + size];
                }
            }

            return edged;
        }

        static AnimatorController BuildCatController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Sitting", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Angry", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Emote", AnimatorControllerParameterType.Int);
            controller.AddParameter("EmoteTrigger", AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            // How fast the ground moves under a planted paw in each clip, measured at scale 1 (m/s). Each clip sits
            // at the speed where its paws don't slide. Walking uses the trot and sprinting the run; both play a
            // similar ~1.2-1.4x faster than authored so their cadence matches.
            const float walkingPawSpeed = 0.31f;
            const float trottingPawSpeed = 0.73f;
            const float runningPawSpeed = 1.18f;
            tree.AddChild(Clip("idle"), 0f);
            tree.AddChild(Clip("walking"), walkingPawSpeed * CatScale);
            tree.AddChild(Clip("trotting"), CatMotion.WalkSpeed);
            tree.AddChild(Clip("running"), CatMotion.RunSpeed);
            var children = tree.children;
            children[2].timeScale = CatMotion.WalkSpeed / (trottingPawSpeed * CatScale);
            children[3].timeScale = CatMotion.RunSpeed / (runningPawSpeed * CatScale);
            tree.children = children;
            machine.defaultState = locomotion;

            // The jumping clip is a crouch, a short leap (paws leave the ground at ~36% and land at ~60%, measured
            // from the paw bones) and a recovery. Start at the push-off (the physics jump starts at once) and stretch
            // the leap over the real, floatier time in the air.
            const float jumpTakeoff = 0.32f;
            const float jumpLanding = 0.6f;
            var jump = State(machine, "Jump", "jumping");
            jump.speed = (jumpLanding - jumpTakeoff) * Clip("jumping").length / CatMotion.JumpAirTime;
            var sit = State(machine, "Sit", "sitting");
            var standUp = State(machine, "StandUp", "getup");
            var angry = State(machine, "Angry", "attack");

            Transition(locomotion, jump, 0.05f, ("Airborne", AnimatorConditionMode.If)).offset = jumpTakeoff;
            Transition(jump, locomotion, 0.15f, ("Airborne", AnimatorConditionMode.IfNot));
            Transition(locomotion, sit, 0.15f, ("Sitting", AnimatorConditionMode.If));
            Transition(sit, standUp, 0.1f, ("Sitting", AnimatorConditionMode.IfNot));
            ExitTransition(standUp, locomotion, 0.85f, 0.15f);
            Transition(standUp, sit, 0.15f, ("Sitting", AnimatorConditionMode.If));

            Transition(locomotion, angry, 0.1f, ("Angry", AnimatorConditionMode.If));
            Transition(sit, angry, 0.1f, ("Angry", AnimatorConditionMode.If));
            ExitTransition(angry, sit, 0.95f, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, "Sitting");
            ExitTransition(angry, locomotion, 0.95f, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Sitting");

            BuildEmoteStates(machine, locomotion, jump, sit);
            BuildPawLayer(controller);
            return controller;
        }

        /// <summary>
        /// Emote wheel states (CatReactions.PlayEmote sets "Emote" and fires "EmoteTrigger" from Locomotion).
        /// Meow, purr and groom sit down first; purr stays seated until the cat moves. Moving cancels any emote.
        /// </summary>
        static void BuildEmoteStates(AnimatorStateMachine machine, AnimatorState locomotion, AnimatorState jump, AnimatorState sit)
        {
            var sitDown = State(machine, "EmoteSitDown", "sitting", 1.8f);
            var meow = State(machine, "EmoteMeow", "meow", 0.8f);
            var groom = State(machine, "EmoteGroom", "licking");
            var getUp = State(machine, "EmoteGetUp", "getup", 1.4f);
            var pounce = State(machine, "EmotePounce", "attack");
            var hiss = State(machine, "EmoteHiss", "hited");
            var wiggle = State(machine, "EmoteWiggle", "attack2");
            var playDead = State(machine, "EmotePlayDead", "died");

            AnimatorStateTransition Start(AnimatorState to, CatEmote emote)
            {
                var transition = Transition(locomotion, to, 0.15f, ("EmoteTrigger", AnimatorConditionMode.If));
                transition.AddCondition(AnimatorConditionMode.Equals, (int)emote, "Emote");
                return transition;
            }

            Start(sitDown, CatEmote.Meow);
            Start(sitDown, CatEmote.Purr);
            Start(sitDown, CatEmote.Groom);
            Start(pounce, CatEmote.Pounce);
            Start(hiss, CatEmote.Hiss);
            Start(wiggle, CatEmote.Wiggle);
            Start(playDead, CatEmote.PlayDead);

            ExitTransition(sitDown, meow, 0.9f, 0.15f).AddCondition(AnimatorConditionMode.Equals, (int)CatEmote.Meow, "Emote");
            ExitTransition(sitDown, groom, 0.9f, 0.2f).AddCondition(AnimatorConditionMode.Equals, (int)CatEmote.Groom, "Emote");
            ExitTransition(meow, getUp, 0.95f, 0.15f);
            ExitTransition(groom, getUp, 0.95f, 0.2f);
            ExitTransition(getUp, locomotion, 0.85f, 0.15f);
            ExitTransition(pounce, locomotion, 0.95f, 0.2f);
            ExitTransition(hiss, locomotion, 0.95f, 0.2f);
            ExitTransition(wiggle, locomotion, 0.95f, 0.2f);
            // Stay "dead" for a few seconds (the clip holds its last frame), then spring back up.
            ExitTransition(playDead, locomotion, 3.2f, 0.45f);

            foreach (var state in new[] { sitDown, meow, groom, getUp, pounce, hiss, wiggle, playDead })
            {
                var seated = state == sitDown || state == meow || state == groom;
                if (state != getUp)
                    Transition(state, seated ? getUp : locomotion, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
                Transition(state, jump, 0.05f, ("Airborne", AnimatorConditionMode.If));
                Transition(state, sit, 0.15f, ("Sitting", AnimatorConditionMode.If));
            }
        }

        /// <summary>
        /// A second layer that only moves the right foreleg: the reaching paw from the pounce clip, used as a quick
        /// swat. Its weight stays 0 except while CatReactions plays a swipe.
        /// </summary>
        static void BuildPawLayer(AnimatorController controller)
        {
            const string maskPath = Root + "/Art/Animation/CatPawMask.mask";
            AssetDatabase.DeleteAsset(maskPath);
            var mask = new AvatarMask();
            mask.AddTransformPath(Load<GameObject>(CatModelPath).transform, true);
            for (var i = 0; i < mask.transformCount; i++)
                mask.SetTransformActive(i, mask.GetTransformPath(i).Contains("Cat_r_Scapula_bone"));
            AssetDatabase.CreateAsset(mask, maskPath);

            controller.AddLayer("Paw");
            var layers = controller.layers;
            layers[1].avatarMask = mask;
            layers[1].defaultWeight = 0f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            var swipe = State(controller.layers[1].stateMachine, "Swipe", "attack", 1.6f);
            controller.layers[1].stateMachine.defaultState = swipe;
        }

        static AnimatorState State(AnimatorStateMachine machine, string name, string clip, float speed = 1f)
        {
            var state = machine.AddState(name);
            state.motion = Clip(clip);
            state.speed = speed;
            return state;
        }

        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration, params (string parameter, AnimatorConditionMode mode)[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            foreach (var (parameter, mode) in conditions)
                transition.AddCondition(mode, 0f, parameter);
            return transition;
        }

        static AnimatorStateTransition ExitTransition(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = duration;
            return transition;
        }

        static AnimationClip Clip(string name)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(CatClipPath(name)))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }

            throw new System.InvalidOperationException($"No clip in {CatClipPath(name)}");
        }

        /// <summary>Model + Animator + reactions shared by the player and NPC cat prefabs.</summary>
        static (GameObject model, Animator animator, CatAnimatorDriver driver, CatReactions reactions) AddCatBody(GameObject root, AnimatorController controller)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(CatModelPath), root.transform);
            model.name = "Model";
            model.transform.localScale = Vector3.one * CatScale;
            var animator = model.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            model.AddComponent<CatAnimationEvents>();
            SetFields(model.AddComponent<CatRigAnchor>(), ("_rigRoot", model.transform.Find(CatRigRoot)));

            // Fur in slot 0 (swapped per fur color), a warm rim-light pass in slot 1.
            foreach (var fur in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                fur.sharedMaterials = new[] { fur.sharedMaterial, CatRimMaterial() };

            var driver = root.AddComponent<CatAnimatorDriver>();
            var reactions = root.AddComponent<CatReactions>();
            SetFields(reactions, ("_model", model.transform), ("_animator", animator));
            SetFields(driver, ("_animator", animator));
            return (model, animator, driver, reactions);
        }

        static Material CatRimMaterial()
        {
            var material = MaterialAsset("CatRim", "CasinoMares/CatRim");
            material.SetColor("_Color", new Color(1f, 0.78f, 0.52f));
            material.SetFloat("_Intensity", 0.3f);
            material.SetFloat("_Power", 3f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildNpcPrefab(CatFurPalette palette, AnimatorController controller)
        {
            var root = new GameObject("NpcCat");
            var (model, _, driver, reactions) = AddCatBody(root, controller);
            var visual = root.AddComponent<NpcCatVisual>();
            SetFields(visual, ("_palette", palette), ("_driver", driver), ("_reactions", reactions));
            SetArray(visual, "_furRenderers", model.GetComponentsInChildren<SkinnedMeshRenderer>());
            PrefabUtility.SaveAsPrefabAsset(root, NpcPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void BuildPlayerPrefab(CatFurPalette palette, AnimatorController controller)
        {
            var root = new GameObject("PlayerCat") { tag = "Player" };

            var characterController = root.AddComponent<CharacterController>();
            characterController.height = 0.6f;
            characterController.radius = 0.25f;
            characterController.center = new Vector3(0f, 0.3f, 0f);
            characterController.stepOffset = 0.15f;
            characterController.skinWidth = 0.02f;
            characterController.minMoveDistance = 0f;

            root.AddComponent<NetworkObject>();
            var networkCharacter = root.AddComponent<NetworkCharacterController>();
            networkCharacter.gravity = CatMotion.RiseGravity;
            networkCharacter.acceleration = 12f;
            networkCharacter.braking = 12f;
            networkCharacter.rotationSpeed = 12f;
            var movement = root.AddComponent<CatMovement>();
            var cat = root.AddComponent<PlayerCat>();
            root.AddComponent<SeatingController>();
            root.AddComponent<PlayerInteractor>();
            root.AddComponent<PawSwipe>();
            root.AddComponent<SessionWinnings>();

            var (model, _, driver, _) = AddCatBody(root, controller);
            SetFields(driver, ("_movement", movement));

            var cameraTarget = Child(root.transform, "CameraTarget", new Vector3(0f, 0.45f, 0f));
            var nameplateAnchor = Child(root.transform, "NameplateAnchor", new Vector3(0f, 0.85f, 0f));
            var gift = Child(root.transform, "GiftTarget", Vector3.zero).gameObject.AddComponent<GiftTarget>();
            SetFloat(gift, "_radius", 1.3f);

            SetFields(cat, ("_palette", palette), ("_cameraTarget", cameraTarget), ("_nameplateAnchor", nameplateAnchor));
            SetArray(cat, "_furRenderers", model.GetComponentsInChildren<SkinnedMeshRenderer>());

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void BuildPanelSettings()
        {
            var settings = LoadOrCreate<PanelSettings>(PanelSettingsPath);
            settings.themeStyleSheet = Load<ThemeStyleSheet>($"{Root}/UI/CasinoTheme.tss");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
        }

        static void BuildBoardPanelSettings()
        {
            var texture = Load<RenderTexture>(BoardTexturePath);
            if (texture == null)
            {
                texture = new RenderTexture(1024, 640, 0, RenderTextureFormat.ARGB32) { name = "LeaderboardBoard" };
                AssetDatabase.CreateAsset(texture, BoardTexturePath);
            }

            var settings = LoadOrCreate<PanelSettings>(BoardPanelSettingsPath);
            settings.themeStyleSheet = Load<ThemeStyleSheet>($"{Root}/UI/CasinoTheme.tss");
            settings.targetTexture = texture;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.clearColor = true;
            settings.colorClearValue = new Color(0.13f, 0.08f, 0.1f);
            EditorUtility.SetDirty(settings);
        }

        static void BuildVolumeProfile()
        {
            AssetDatabase.DeleteAsset(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            // Filmic and warm: ACES, a soft bloom for the lamps and neon, gentle vignette and a touch of grain.
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.75f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.68f);
            bloom.tint.Override(new Color(1f, 0.86f, 0.72f));
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.8f);
            color.contrast.Override(12f);
            color.saturation.Override(6f);
            color.colorFilter.Override(new Color(1f, 0.96f, 0.9f));
            var whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(6f);
            var splitToning = profile.Add<SplitToning>(true);
            splitToning.shadows.Override(new Color(0.48f, 0.48f, 0.53f));
            splitToning.highlights.Override(new Color(0.62f, 0.52f, 0.42f));
            splitToning.balance.Override(-10f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.45f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            grain.response.Override(0.85f);
            if (_sea)
                TuneSeaVolume(color, splitToning, vignette, bloom);

            foreach (var component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
        }

        static void BuildSoundBank()
        {
            AudioClip[] Clips(string folder, params string[] names)
            {
                var clips = new List<AudioClip>();
                foreach (var name in names)
                {
                    var clip = Load<AudioClip>($"{folder}/{name}.ogg");
                    if (clip != null)
                        clips.Add(clip);
                }

                return clips.ToArray();
            }

            string[] Numbered(string prefix, int count)
            {
                var names = new string[count];
                for (var i = 0; i < count; i++)
                    names[i] = $"{prefix}{i + 1}";
                return names;
            }

            var entries = new List<(SoundId id, AudioClip[] clips, float volume, Vector2 pitch)>
            {
                (SoundId.CardSlide, Clips(KenneyCasino, Numbered("card-slide-", 8)), 0.7f, new Vector2(0.95f, 1.08f)),
                (SoundId.CardPlace, Clips(KenneyCasino, Numbered("card-place-", 4)), 0.6f, new Vector2(0.95f, 1.05f)),
                (SoundId.Shuffle, Clips(KenneyCasino, "card-shuffle"), 0.9f, Vector2.one),
                (SoundId.ChipBet, Clips(KenneyCasino, Numbered("chips-stack-", 6)), 0.8f, new Vector2(0.95f, 1.05f)),
                (SoundId.ChipPayout, Clips(KenneyCasino, Numbered("chips-collide-", 4)), 0.9f, new Vector2(0.95f, 1.05f)),
                (SoundId.UiClick, Clips(KenneyInterface, "click_001", "click_002"), 0.6f, new Vector2(0.97f, 1.03f)),
                (SoundId.Win, Clips(KenneyInterface, "confirmation_001", "confirmation_002"), 0.8f, Vector2.one),
                (SoundId.Lose, Clips(KenneyInterface, "error_004", "error_006"), 0.6f, Vector2.one),
                (SoundId.Tick, Clips(KenneyInterface, "tick_001", "tick_002"), 0.6f, Vector2.one),
                (SoundId.Notify, Clips(KenneyInterface, "pluck_001", "glass_001"), 0.6f, Vector2.one),
                (SoundId.WheelSpin, Clips(KenneyCasino, Numbered("dice-shake-", 3)), 0.8f, new Vector2(0.8f, 0.9f)),
                (SoundId.BallDrop, Clips(KenneyCasino, Numbered("die-throw-", 4)), 0.7f, new Vector2(1.2f, 1.4f)),
            };

            var bank = LoadOrCreate<SoundBank>(SoundBankPath);
            var serialized = new SerializedObject(bank);
            var array = serialized.FindProperty("_entries");
            array.arraySize = entries.Count;
            for (var i = 0; i < entries.Count; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Id").enumValueIndex = (int)entries[i].id;
                var clips = element.FindPropertyRelative("Clips");
                clips.arraySize = entries[i].clips.Length;
                for (var c = 0; c < entries[i].clips.Length; c++)
                    clips.GetArrayElementAtIndex(c).objectReferenceValue = entries[i].clips[c];
                element.FindPropertyRelative("Volume").floatValue = entries[i].volume;
                element.FindPropertyRelative("PitchRange").vector2Value = entries[i].pitch;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================================================================ Scene

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Fusion bakes scene NetworkObjects on save using their GlobalObjectId, which needs the scene asset to
            // exist. Save the empty scene first so the real save below can bake.
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Creating the scene can unload asset objects loaded before it, which leaves missing references.
            // Load everything the scene needs only after the scene exists.
            var playerPrefab = Load<GameObject>(PlayerPrefabPath);
            var npcPrefab = Load<GameObject>(NpcPrefabPath).GetComponent<NpcCatVisual>();
            var palette = Load<CatFurPalette>(PalettePath);
            var panelSettings = Load<PanelSettings>(PanelSettingsPath);

            var materials = new Materials();
            BuildLighting();
            var hall = BuildHall(materials, out var spawnPoints);

            var tables = new[]
            {
                BuildBlackjackTable("Blackjack Table 1", new Vector3(-3.6f, 0f, 0.8f), materials, npcPrefab),
                BuildBlackjackTable("Blackjack Table 2", new Vector3(3.6f, 0f, 0.8f), materials, npcPrefab),
            };

            BuildRouletteTable("Roulette", new Vector3(0f, 0f, 4.3f), materials, npcPrefab);
            BuildBaccaratTable("Baccarat", new Vector3(-0.4f, 0f, -3.4f), materials, npcPrefab);
            for (var i = 0; i < 3; i++)
                BuildSlotMachine($"Slot Machine {i + 1}", new Vector3(HallWidth / 2f - 0.55f, 0f, -2.1f - i * 1.15f), materials);
            BuildPokerTable("Poker", new Vector3(-7.9f, 0f, -0.9f), materials, npcPrefab);
            BuildBar(hall, materials, npcPrefab);
            BuildCashier(hall, materials, npcPrefab);
            BuildLounge(hall, materials);
            BuildLeaderboardBoard(materials);
            BuildPatronSpots(tables);
            BuildPatrons(npcPrefab);
            BuildLamps(materials);
            BuildProps(materials);

            BuildCameras();
            BuildPostProcessing();

            var audio = new GameObject("Audio").AddComponent<Sfx>();
            SetFields(audio, ("_bank", Load<SoundBank>(SoundBankPath)));

            var connection = new GameObject("ConnectionManager").AddComponent<ConnectionManager>();
            SetFields(connection, ("_playerPrefab", playerPrefab.GetComponent<NetworkObject>()));
            SetArray(connection, "_spawnPoints", spawnPoints);

            BuildUI(panelSettings, palette);
            if (_sea)
                BuildSeaside(hall, materials);
            BakeNavMesh(hall);
            BakeReflectionProbe();

            EditorSceneManager.SaveScene(scene, ScenePath);
            UpdateBuildScenes();
        }

        class Materials
        {
            // The room's surfaces have a seaside version (Themed), so each scene keeps its own look.
            public readonly Material Floor = Lit(Themed("Graybox_Floor"), Color.white, 0.18f);
            public readonly Material Wall = Lit(Themed("Graybox_Wall"), Color.white, 0.08f);
            public readonly Material WallShort = Lit(Themed("Graybox_WallShort"), Color.white, 0.08f);
            public readonly Material Wood = Lit("Graybox_Wood", Color.white, 0.45f);
            public readonly Material DarkWood = Lit(Themed("Graybox_DarkWood"), _sea ? new Color(0.85f, 0.78f, 0.72f) : new Color(0.55f, 0.5f, 0.5f), 0.4f);
            /// <summary>The seaside hall's plaster ceiling (the lounge's is dark wood).</summary>
            public readonly Material Ceiling = _sea ? Lit("Sea_Ceiling", new Color(0.93f, 0.88f, 0.8f), 0.1f) : null;
            public readonly Material Felt = Lit("Graybox_Felt", new Color(0.1f, 0.4f, 0.24f), 0.05f);
            public readonly Material Velvet = Lit("Graybox_Velvet", new Color(0.55f, 0.1f, 0.14f), 0.15f);
            public readonly Material Brass = Lit("Graybox_Brass", new Color(0.8f, 0.62f, 0.32f), 0.75f, 0.9f);
            public readonly Material Cushion = Lit("Graybox_Cushion", new Color(0.62f, 0.44f, 0.25f), 0.1f);
            public readonly Material Chip = Lit("Chip", Color.white, 0.45f);
            public readonly Material Card = Unlit("Card", null);
            public readonly Material Wheel = Unlit("RouletteWheel", null);
            public readonly Material Reel = Unlit("SlotReel", null);
            public readonly Material BaccaratPlayer = Lit("BaccaratPlayer", new Color(0.18f, 0.32f, 0.7f), 0.2f);
            public readonly Material BaccaratBanker = Lit("BaccaratBanker", new Color(0.62f, 0.14f, 0.17f), 0.2f);
            public readonly Material Cabinet = Lit("SlotCabinet", new Color(0.45f, 0.08f, 0.14f), 0.55f, 0.2f);
            public readonly Material Ball = Lit("RouletteBall", new Color(0.96f, 0.95f, 0.92f), 0.8f);
            public readonly Material Board = Unlit("LeaderboardBoard", Load<RenderTexture>(BoardTexturePath));

            // Lamps, glow and atmosphere.
            public readonly Material LampShade = Lit("LampShade", new Color(0.08f, 0.2f, 0.14f), 0.7f, 0.3f);
            public readonly Material LampGlow = Emissive("LampGlow", new Color(1f, 0.9f, 0.75f), new Color(1f, 0.8f, 0.55f) * 6f);
            public readonly Material SconceGlow = Emissive("SconceGlow", new Color(1f, 0.85f, 0.65f), new Color(1f, 0.72f, 0.45f) * 2.5f);
            public readonly Material SlotGlow = Emissive("SlotGlow", new Color(1f, 0.8f, 0.5f), new Color(1f, 0.55f, 0.25f) * 3f);
            public readonly Material ShelfGlow = Emissive("ShelfGlow", new Color(1f, 0.8f, 0.55f), new Color(1f, 0.62f, 0.3f) * 2.5f);
            public readonly Material Cone = LightConeMaterial();
            public readonly Material Dust = SoftAdditive("DustMote", SoftDotTexture(), new Color(1f, 0.85f, 0.65f) * 0.9f);
            public readonly Material Neon = SoftAdditive("NeonSign", NeonSignTexture("CASINO MARES"), new Color(1f, 0.38f, 0.62f) * 3.2f);

            // Props on the tables (see BuildProps).
            public readonly Material Glass = TransparentLit("PropGlass", new Color(0.85f, 0.93f, 0.95f, 0.28f), 0.95f);
            public readonly Material BottleGlass = TransparentLit("PropBottleGlass", new Color(0.2f, 0.45f, 0.25f, 0.65f), 0.9f);
            public readonly Material Whisky = Emissive("PropWhisky", new Color(0.75f, 0.42f, 0.12f), new Color(0.35f, 0.16f, 0.03f));
            public readonly Material Ceramic = Lit("PropCeramic", new Color(0.93f, 0.88f, 0.8f), 0.7f);
            public readonly Material Coffee = Lit("PropCoffee", new Color(0.18f, 0.1f, 0.06f), 0.85f);
            public readonly Material[] Yarn =
            {
                Lit("PropYarnRed", new Color(0.75f, 0.16f, 0.18f), 0.05f),
                Lit("PropYarnBlue", new Color(0.2f, 0.4f, 0.75f), 0.05f),
                Lit("PropYarnGold", new Color(0.9f, 0.68f, 0.25f), 0.05f),
            };

            // Bottles on the bar's back shelf (decor only).
            public readonly Material[] ShelfBottles =
            {
                Lit("ShelfBottleAmber", new Color(0.55f, 0.28f, 0.08f), 0.92f),
                Lit("ShelfBottleGreen", new Color(0.12f, 0.35f, 0.18f), 0.92f),
                Lit("ShelfBottleClear", new Color(0.75f, 0.8f, 0.78f), 0.95f),
                Lit("ShelfBottleBlue", new Color(0.15f, 0.25f, 0.5f), 0.92f),
            };

            public Materials()
            {
                SetTexture(Floor, _sea ? SeaCarpetTexture() : CarpetTexture(), new Vector2(HallWidth / 2.4f, HallDepth / 2.4f));
                var wallpaper = _sea ? SeaWallpaperTexture() : WallpaperTexture();
                if (_sea)
                {
                    // Seaside walls are cut up around windows: their boxes carry metre UVs instead (WorldUvCube).
                    SetTexture(Wall, wallpaper, Vector2.one);
                    SetTexture(WallShort, wallpaper, Vector2.one);
                    WorldUvTiles[Wall] = WallpaperTile;
                    WorldUvTiles[WallShort] = WallpaperTile;
                }
                else
                {
                    SetTexture(Wall, wallpaper, new Vector2(HallWidth / 1.2f, CeilingHeight / 1.2f));
                    SetTexture(WallShort, wallpaper, new Vector2(HallDepth / 1.2f, CeilingHeight / 1.2f));
                }
                var wood = WoodTexture();
                SetTexture(Wood, wood, Vector2.one);
                SetTexture(DarkWood, wood, new Vector2(2f, 2f));
                var felt = FeltTexture();
                SetTexture(Felt, felt, new Vector2(2f, 2f));
                foreach (var yarn in Yarn)
                    SetTexture(yarn, felt, new Vector2(3f, 3f));
            }

            static void SetTexture(Material material, Texture texture, Vector2 tiling)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", tiling);
                EditorUtility.SetDirty(material);
            }
        }

        static Material Emissive(string name, Color color, Color emission)
        {
            var material = Lit(name, color, 0.3f);
            material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
            // URP turns _EMISSION back off whenever it validates a material whose GI flags say "not emissive".
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>URP Lit set up as alpha-blended transparent (what the material inspector would do).</summary>
        static Material TransparentLit(string name, Color color, float smoothness)
        {
            var material = Lit(name, color, smoothness);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("DepthOnly", false);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material SoftAdditive(string name, Texture texture, Color color)
        {
            var material = MaterialAsset(name, "CasinoMares/SoftAdditive");
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LightConeMaterial()
        {
            var material = MaterialAsset("LightCone", "CasinoMares/LightCone");
            material.SetColor("_Color", new Color(1f, 0.8f, 0.55f));
            material.SetFloat("_Intensity", 0.14f);
            material.SetFloat("_Falloff", 1.6f);
            material.SetFloat("_EdgeSoftness", 2.2f);
            material.SetFloat("_NearFade", 1.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- Generated textures and meshes

        /// <summary>Writes a PNG built from a per-pixel function (u, v in 0..1) into Art/Textures and imports it.</summary>
        static Texture2D GeneratedTexture(string name, int width, int height, System.Func<float, float, Color> pixel,
            TextureWrapMode wrap = TextureWrapMode.Repeat, System.Action<TextureImporter> configure = null)
        {
            if (!AssetDatabase.IsValidFolder(FurTextureFolder))
                AssetDatabase.CreateFolder(Root + "/Art", "Textures");

            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            var path = $"{FurTextureFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = Mathf.Max(width, height);
            configure?.Invoke(importer);
            importer.SaveAndReimport();
            return Load<Texture2D>(path);
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
        }

        /// <summary>Tileable value noise: u, v in 0..1 wrap around at <paramref name="period"/> cells.</summary>
        static float PeriodicNoise(float u, float v, int period, int seed)
        {
            var x = u * period;
            var y = v * period;
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wrap(int i) => ((i % period) + period) % period;
            var a = Hash(Wrap(x0), Wrap(y0), seed);
            var b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            var c = Hash(Wrap(x0), Wrap(y0 + 1), seed);
            var d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (var o = 0; o < octaves; o++)
            {
                sum += PeriodicNoise(u, v, period << o, seed + o) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
            }

            return sum / total;
        }

        /// <summary>Shader-style smoothstep: 0 below edge0, 1 above edge1 (Mathf.SmoothStep interpolates values instead).</summary>
        static float Edge(float edge0, float edge1, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));

        /// <summary>Casino carpet: burgundy with a gold diamond lattice and small medallions.</summary>
        static Texture2D CarpetTexture() => GeneratedTexture("Carpet", 512, 512, (u, v) =>
        {
            var a = Mathf.Repeat((u + v) * 4f, 1f);
            var b = Mathf.Repeat((u - v) * 4f, 1f);
            var line = Mathf.Min(Mathf.Min(a, 1f - a), Mathf.Min(b, 1f - b));
            var lattice = 1f - Edge(0.012f, 0.026f, line);
            var cell = Mathf.Abs(a - 0.5f) + Mathf.Abs(b - 0.5f);
            var medallion = 1f - Edge(0.08f, 0.11f, cell);
            var ring = 1f - Edge(0.01f, 0.02f, Mathf.Abs(cell - 0.22f));

            var color = Color.Lerp(new Color(0.32f, 0.07f, 0.1f), new Color(0.44f, 0.1f, 0.13f), Fbm(u, v, 8, 2, 3));
            var gold = new Color(0.62f, 0.44f, 0.2f);
            color = Color.Lerp(color, gold, lattice * 0.8f);
            color = Color.Lerp(color, gold * 0.85f, medallion * 0.75f);
            color = Color.Lerp(color, new Color(0.16f, 0.04f, 0.06f), ring * 0.6f);
            return color * (0.82f + 0.3f * Fbm(u, v, 64, 3, 11));
        });

        /// <summary>Striped wallpaper: two deep reds, gold pinstripes, a faint diamond damask.</summary>
        static Texture2D WallpaperTexture() => GeneratedTexture("Wallpaper", 512, 512, (u, v) =>
        {
            var stripe = Mathf.Repeat(u * 2f, 1f);
            var dark = new Color(0.3f, 0.07f, 0.1f);
            var light = new Color(0.4f, 0.1f, 0.13f);
            var color = stripe < 0.5f ? dark : light;
            var edge = Mathf.Min(Mathf.Abs(stripe - 0.5f), Mathf.Min(stripe, 1f - stripe));
            color = Color.Lerp(color, new Color(0.6f, 0.45f, 0.22f), 1f - Edge(0.005f, 0.011f, edge));
            if (stripe < 0.5f)
            {
                var d = Mathf.Abs(stripe - 0.25f) * 4f + Mathf.Abs(Mathf.Repeat(v * 3f, 1f) - 0.5f) * 2f;
                var outline = (1f - Edge(0.46f, 0.52f, d)) * Edge(0.34f, 0.4f, d);
                color = Color.Lerp(color, light * 1.08f, outline);
            }

            return color * (0.9f + 0.14f * Fbm(u, v, 16, 2, 5));
        });

        /// <summary>Warm wood grain, running along V.</summary>
        static Texture2D WoodTexture() => GeneratedTexture("Wood", 512, 512, (u, v) =>
        {
            var warp = Fbm(u, v, 4, 3, 21);
            var grain = Mathf.Repeat(u * 10f + warp * 2.2f + Mathf.Sin(v * Mathf.PI * 4f) * 0.15f, 1f);
            var rings = 1f - Mathf.Abs(grain - 0.5f) * 2f;
            var streaks = Fbm(u, v * 0.25f, 32, 2, 7);
            return Color.Lerp(new Color(0.2f, 0.11f, 0.06f), new Color(0.42f, 0.25f, 0.13f), 0.3f + 0.45f * rings * rings + 0.25f * streaks);
        });

        /// <summary>Fine fibrous noise; the material's color tints it (felt, yarn).</summary>
        static Texture2D FeltTexture() => GeneratedTexture("Felt", 256, 256, (u, v) =>
        {
            var value = 0.82f + 0.2f * Fbm(u, v, 32, 3, 9);
            return new Color(value, value, value);
        });

        /// <summary>A soft round dot (dust motes).</summary>
        static Texture2D SoftDotTexture() => GeneratedTexture("SoftDot", 64, 64, (u, v) =>
        {
            var d = new Vector2(u - 0.5f, v - 0.5f).magnitude / 0.5f;
            var alpha = Mathf.Exp(-d * d * 5f) * (1f - Edge(0.85f, 1f, d));
            return new Color(1f, 1f, 1f, alpha);
        }, TextureWrapMode.Clamp);

        /// <summary>Pixel-font neon lettering with a soft halo baked in (the material's HDR color feeds the bloom).</summary>
        static Texture2D NeonSignTexture(string text)
        {
            const int width = 1024;
            const int height = 160;
            const int scale = 11;
            var core = new float[width * height];
            var left = (width - PixelFont.MeasureWidth(text, scale)) / 2;
            var top = (height - 7 * scale) / 2;
            PixelFont.DrawText((x, y) =>
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                    core[y * width + x] = 1f;
            }, text, left, top, scale);

            var halo = BoxBlur(BoxBlur(core, width, height, 9), width, height, 9);
            return GeneratedTexture("NeonSign", width, height, (u, v) =>
            {
                // PixelFont draws with y pointing down; textures count rows from the bottom.
                var x = Mathf.Clamp((int)(u * width), 0, width - 1);
                var y = Mathf.Clamp(height - 1 - (int)(v * height), 0, height - 1);
                var i = y * width + x;
                var alpha = Mathf.Max(core[i], halo[i] * 0.55f);
                var white = core[i] * 0.7f;
                return new Color(0.6f + white, 0.6f + white, 0.6f + white, alpha);
            }, TextureWrapMode.Clamp);
        }

        static float[] BoxBlur(float[] source, int width, int height, int radius)
        {
            var horizontal = new float[source.Length];
            for (var y = 0; y < height; y++)
            {
                float sum = 0f;
                for (var x = -radius; x < width; x++)
                {
                    if (x + radius < width)
                        sum += source[y * width + x + radius];
                    if (x - radius - 1 >= 0)
                        sum -= source[y * width + x - radius - 1];
                    if (x >= 0)
                        horizontal[y * width + x] = sum / (radius * 2 + 1);
                }
            }

            var result = new float[source.Length];
            for (var x = 0; x < width; x++)
            {
                float sum = 0f;
                for (var y = -radius; y < height; y++)
                {
                    if (y + radius < height)
                        sum += horizontal[(y + radius) * width + x];
                    if (y - radius - 1 >= 0)
                        sum -= horizontal[(y - radius - 1) * width + x];
                    if (y >= 0)
                        result[y * width + x] = sum / (radius * 2 + 1);
                }
            }

            return result;
        }

        /// <summary>
        /// An open cone for fake light shafts: top ring at y 0, unit-radius bottom ring at y -1, v = 0 at the top.
        /// Scaled per lamp.
        /// </summary>
        static Mesh LightConeMesh()
        {
            const int segments = 40;
            const float topRadius = 0.16f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var normal = (direction + Vector3.up * (1f - topRadius)).normalized;
                vertices.Add(direction * topRadius);
                vertices.Add(direction + Vector3.down);
                normals.Add(normal);
                normals.Add(normal);
                uvs.Add(new Vector2(i / (float)segments, 0f));
                uvs.Add(new Vector2(i / (float)segments, 1f));
                if (i == segments)
                    continue;
                var top = i * 2;
                triangles.AddRange(new[] { top, top + 2, top + 1, top + 1, top + 2, top + 3 });
            }

            var mesh = new Mesh { name = "LightCone" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            Directory.CreateDirectory(Path.GetDirectoryName(ConeMeshPath)!);
            AssetDatabase.DeleteAsset(ConeMeshPath);
            AssetDatabase.CreateAsset(mesh, ConeMeshPath);
            return mesh;
        }

        static Material Lit(string name, Color color, float smoothness, float metallic = 0f)
        {
            var material = MaterialAsset(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Unlit(string name, Texture texture)
        {
            var material = MaterialAsset(name, "Universal Render Pipeline/Unlit");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material MaterialAsset(string name, string shader)
        {
            var path = $"{Root}/Art/Materials/{name}.mat";
            var material = Load<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader.name != shader)
            {
                material.shader = Shader.Find(shader);
            }

            return material;
        }

        static void BuildLighting()
        {
            if (_sea)
            {
                BuildSeaLighting();
                return;
            }

            var sun = new GameObject("Key Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            // A dim fill from above (the ceiling doesn't cast shadows); the lamps do the real lighting.
            sun.color = new Color(1f, 0.82f, 0.66f);
            sun.intensity = 0.28f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.Euler(62f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.3f, 0.28f);
            RenderSettings.ambientEquatorColor = new Color(0.27f, 0.19f, 0.17f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.07f, 0.07f);
            RenderSettings.skybox = null;
            // A little warm haze gives the far side of the room depth.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.022f;
            RenderSettings.fogColor = new Color(0.11f, 0.07f, 0.065f);
        }

        static Light PointLight(string name, Vector3 position, float range, float intensity, Color color, Transform parent = null)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            return light;
        }

        static Transform BuildHall(Materials m, out Transform[] spawnPoints)
        {
            const float wallHeight = CeilingHeight;
            var hall = new GameObject("Hall").transform;
            Box("Floor", hall, new Vector3(0f, -0.05f, 0f), new Vector3(HallWidth, 0.1f, HallDepth), m.Floor);
            if (_sea)
            {
                BuildSeaWalls(hall, m);
                BuildEntrance(hall, m);
            }
            else
            {
                Box("Wall_North", hall, new Vector3(0f, wallHeight / 2f, HallDepth / 2f), new Vector3(HallWidth, wallHeight, 0.3f), m.Wall);
                BuildEntrance(hall, m);
                Box("Wall_East", hall, new Vector3(HallWidth / 2f, wallHeight / 2f, 0f), new Vector3(0.3f, wallHeight, HallDepth), m.WallShort);
            }

            Box("Wall_West", hall, new Vector3(-HallWidth / 2f, wallHeight / 2f, 0f), new Vector3(0.3f, wallHeight, HallDepth), m.WallShort);
            BuildCeiling(hall, m);

            // Wainscoting and a brass rail make the walls read as a lounge.
            foreach (var (name, position, size) in new[]
                     {
                         ("Trim_North", new Vector3(0f, 0.45f, HallDepth / 2f - 0.17f), new Vector3(HallWidth, 0.9f, 0.05f)),
                         // The south trim stops at the door frame instead of running under it.
                         ("Trim_South_West", new Vector3((-HallWidth / 2f + DoorX - DoorWidth / 2f - 0.1f) / 2f, 0.45f, -HallDepth / 2f + 0.17f), new Vector3(HallWidth / 2f + DoorX - DoorWidth / 2f - 0.1f, 0.9f, 0.05f)),
                         ("Trim_South_East", new Vector3((HallWidth / 2f + DoorX + DoorWidth / 2f + 0.1f) / 2f, 0.45f, -HallDepth / 2f + 0.17f), new Vector3(HallWidth / 2f - DoorX - DoorWidth / 2f - 0.1f, 0.9f, 0.05f)),
                         ("Trim_East", new Vector3(HallWidth / 2f - 0.17f, 0.45f, 0f), new Vector3(0.05f, 0.9f, HallDepth)),
                         // By the sea the east wall has the terrace door; Trim_East is left out and split around it.
                         ("Trim_East_South", new Vector3(HallWidth / 2f - 0.17f, 0.45f, (-HallDepth / 2f + SeaDoorFrom - 0.1f) / 2f), new Vector3(0.05f, 0.9f, SeaDoorFrom - 0.1f + HallDepth / 2f)),
                         ("Trim_East_North", new Vector3(HallWidth / 2f - 0.17f, 0.45f, (HallDepth / 2f + SeaDoorTo + 0.1f) / 2f), new Vector3(0.05f, 0.9f, HallDepth / 2f - SeaDoorTo - 0.1f)),
                         ("Trim_West", new Vector3(-HallWidth / 2f + 0.17f, 0.45f, 0f), new Vector3(0.05f, 0.9f, HallDepth)),
                     })
            {
                if (_sea ? name == "Trim_East" : name.StartsWith("Trim_East_"))
                    continue;
                // White-painted panelling by the sea, which keeps the room bright.
                Box(name, hall, position, size, _sea ? SeaTrim() : m.DarkWood, collider: false);
            }

            // Carpet runner from the entrance.
            Box("Carpet", hall, new Vector3(4.8f, 0.005f, -4.4f), new Vector3(2.2f, 0.01f, 4.4f), m.Velvet, collider: false);

            // Soft fill between the pools of lamp light (BuildLamps) and the wall sconces. The seaside hall is lit by
            // its brighter ambient and the sunset instead (and the terrace needs the light slots).
            if (!_sea)
            {
                PointLight("Fill_Entrance", new Vector3(5f, 2.9f, -5f), 7f, 1.6f, new Color(1f, 0.78f, 0.55f), hall);
                PointLight("Fill_Center", new Vector3(0f, 3f, 0.8f), 12f, 1.4f, new Color(1f, 0.75f, 0.5f), hall);
            }
            BuildSconces(hall, m);
            BuildNeonSign(hall, m);

            // Rug under the roulette table (the rest of the floor stays free for future games, GDD §4.2).
            Box("Roulette_Rug", hall, new Vector3(0f, 0.005f, 4.1f), new Vector3(4.6f, 0.01f, 3f), m.Cushion, collider: false);

            var spawnRoot = new GameObject("SpawnPoints").transform;
            var points = new List<Transform>();
            for (var i = 0; i < 4; i++)
            {
                var point = Child(spawnRoot, $"Spawn_{i}", new Vector3(4.2f + i * 0.8f, 0.05f, -5.4f));
                points.Add(point);
            }

            spawnPoints = points.ToArray();
            return hall;
        }

        /// <summary>
        /// The south wall with the entrance: a brass-framed doorway over the carpet runner into a short, dim vestibule
        /// with a velvet curtain across the back. NPC players walk in and out through it (<see cref="CasinoDoor"/>
        /// sits behind the curtain, where they appear and vanish).
        /// </summary>
        static void BuildEntrance(Transform hall, Materials m)
        {
            const float wallZ = -HallDepth / 2f;
            const float height = CeilingHeight;
            const float doorLeft = DoorX - DoorWidth / 2f;
            const float doorRight = DoorX + DoorWidth / 2f;
            const float west = -HallWidth / 2f;
            const float east = HallWidth / 2f;
            Box("Wall_South_West", hall, new Vector3((west + doorLeft) / 2f, height / 2f, wallZ), new Vector3(doorLeft - west, height, 0.3f), m.Wall);
            Box("Wall_South_East", hall, new Vector3((doorRight + east) / 2f, height / 2f, wallZ), new Vector3(east - doorRight, height, 0.3f), m.Wall);
            Box("Wall_South_Lintel", hall, new Vector3(DoorX, (DoorHeight + height) / 2f, wallZ), new Vector3(DoorWidth, height - DoorHeight, 0.3f), m.Wall);
            CameraBlocker("Doorway_CameraBlocker", hall, new Vector3(DoorX, DoorHeight / 2f, wallZ), new Vector3(DoorWidth, DoorHeight, 0.3f));

            var entrance = Child(hall, "Entrance", new Vector3(DoorX, 0f, wallZ));
            // The frame sits proud of the wall (and of the trim beside it), so no faces share a plane.
            NoShadows(Box("Frame_Left", entrance, new Vector3(-DoorWidth / 2f - 0.05f, DoorHeight / 2f, 0.18f), new Vector3(0.1f, DoorHeight, 0.06f), m.Brass, collider: false));
            NoShadows(Box("Frame_Right", entrance, new Vector3(DoorWidth / 2f + 0.05f, DoorHeight / 2f, 0.18f), new Vector3(0.1f, DoorHeight, 0.06f), m.Brass, collider: false));
            NoShadows(Box("Frame_Top", entrance, new Vector3(0f, DoorHeight + 0.05f, 0.18f), new Vector3(DoorWidth + 0.2f, 0.1f, 0.06f), m.Brass, collider: false));

            // The vestibule behind the doorway. NavMesh agents keep 0.5 m from walls, so the curtain hangs 0.8 m in
            // front of the back wall and a walking cat's path ends just behind it.
            const float depth = 2.6f;
            const float width = 2.4f;
            const float back = -0.15f - depth;
            // Starts exactly where the hall floor ends (both tops at y = 0), so the two never overlap.
            Box("Vestibule_Floor", entrance, new Vector3(0f, -0.05f, (back - 0.15f) / 2f), new Vector3(width + 0.3f, 0.1f, 0.15f - back), m.Velvet);
            Box("Vestibule_Ceiling", entrance, new Vector3(0f, DoorHeight + 0.45f, back / 2f), new Vector3(width + 0.3f, 0.1f, depth + 0.3f), m.DarkWood, collider: false);
            Box("Vestibule_West", entrance, new Vector3(-width / 2f - 0.075f, (DoorHeight + 0.5f) / 2f, back / 2f), new Vector3(0.15f, DoorHeight + 0.5f, depth), m.WallShort);
            Box("Vestibule_East", entrance, new Vector3(width / 2f + 0.075f, (DoorHeight + 0.5f) / 2f, back / 2f), new Vector3(0.15f, DoorHeight + 0.5f, depth), m.WallShort);
            Box("Vestibule_Back", entrance, new Vector3(0f, (DoorHeight + 0.5f) / 2f, back - 0.075f), new Vector3(width + 0.3f, DoorHeight + 0.5f, 0.15f), m.DarkWood);
            NoShadows(Box("Curtain", entrance, new Vector3(0f, (DoorHeight + 0.4f) / 2f, back + 0.8f), new Vector3(width, DoorHeight + 0.4f, 0.05f), m.Velvet, collider: false));
            PointLight("Vestibule_Light", new Vector3(0f, DoorHeight + 0.2f, back / 2f + 0.3f), 3.5f, 0.9f, new Color(1f, 0.7f, 0.45f), entrance);

            Child(entrance, "CasinoDoor", new Vector3(0f, 0f, back + 0.35f)).gameObject.AddComponent<CasinoDoor>();
        }

        // ---------------------------------------------------------------- Ceiling, lamps and decor

        /// <summary>
        /// A dark wood ceiling (plaster by the sea) with beams and a brass crown molding. In the lounge it doesn't cast
        /// shadows, so the dim fill light still reaches in.
        /// </summary>
        static void BuildCeiling(Transform hall, Materials m)
        {
            var ceiling = new GameObject("Ceiling").transform;
            ceiling.SetParent(hall, false);
            // Just under the molding, so a camera pulled in by it never shows the beams from above.
            CameraBlocker("Ceiling_CameraBlocker", ceiling, new Vector3(0f, CeilingHeight - 0.3f, 0f), new Vector3(HallWidth, 0.1f, HallDepth));
            var panel = Box("Panel", ceiling, new Vector3(0f, CeilingHeight + 0.05f, 0f), new Vector3(HallWidth, 0.1f, HallDepth), _sea ? m.Ceiling : m.DarkWood, collider: false);
            // By the sea the sun is real: the roof keeps it out, so it only comes in through the windows.
            if (!_sea)
                NoShadows(panel);
            for (var x = -HallWidth / 2f + 2.75f; x < HallWidth / 2f; x += 2.75f)
                NoShadows(Box("Beam", ceiling, new Vector3(x, CeilingHeight - 0.1f, 0f), new Vector3(0.22f, 0.2f, HallDepth), m.Wood, collider: false));
            foreach (var (position, size) in new[]
                     {
                         (new Vector3(0f, CeilingHeight - 0.2f, HallDepth / 2f - 0.17f), new Vector3(HallWidth, 0.06f, 0.06f)),
                         (new Vector3(0f, CeilingHeight - 0.2f, -HallDepth / 2f + 0.17f), new Vector3(HallWidth, 0.06f, 0.06f)),
                         (new Vector3(HallWidth / 2f - 0.17f, CeilingHeight - 0.2f, 0f), new Vector3(0.06f, 0.06f, HallDepth)),
                         (new Vector3(-HallWidth / 2f + 0.17f, CeilingHeight - 0.2f, 0f), new Vector3(0.06f, 0.06f, HallDepth)),
                     })
            {
                NoShadows(Box("CrownMolding", ceiling, position, size, m.Brass, collider: false));
            }
        }

        /// <summary>Brass wall sconces with small warm point lights, spaced around the walls away from the bar, board and slots.</summary>
        static void BuildSconces(Transform hall, Materials m)
        {
            var root = new GameObject("Sconces").transform;
            root.SetParent(hall, false);
            const float inset = HallDepth / 2f - 0.16f;
            const float side = HallWidth / 2f - 0.16f;
            var spots = new List<(Vector3 position, Vector3 facing)>();
            // The seaside hall has windows in the north corners instead of the two outer sconces.
            foreach (var x in _sea ? new[] { -1.4f, 1.8f } : new[] { -9.6f, -1.4f, 1.8f, 8.9f })
                spots.Add((new Vector3(x, 2.15f, inset), Vector3.back));
            foreach (var x in new[] { -9.2f, -3.2f, 0.8f, 7.6f })
                spots.Add((new Vector3(x, 2.15f, -inset), Vector3.forward));
            foreach (var z in new[] { 1.2f, 4.6f })
                spots.Add((new Vector3(side, 2.15f, z), Vector3.left));
            // The seaside hall's west wall has the cat wall instead, with lamps of its own (BuildCatWall).
            foreach (var z in _sea ? new float[0] : new[] { -3.8f, 2.6f })
                spots.Add((new Vector3(-side, 2.15f, z), Vector3.right));

            for (var i = 0; i < spots.Count; i++)
            {
                var sconce = Child(root, $"Sconce_{i + 1}", spots[i].position, Quaternion.LookRotation(spots[i].facing));
                NoShadows(Box("Plate", sconce, new Vector3(0f, 0f, 0.02f), new Vector3(0.1f, 0.24f, 0.03f), m.Brass, collider: false));
                NoShadows(Box("Arm", sconce, new Vector3(0f, -0.04f, 0.08f), new Vector3(0.025f, 0.025f, 0.14f), m.Brass, collider: false));
                NoShadows(Cylinder("Shade", sconce, new Vector3(0f, 0.03f, 0.15f), new Vector3(0.13f, 0.07f, 0.13f), m.SconceGlow, collider: false));
                var light = PointLight("Light", new Vector3(0f, 0.03f, 0.2f), 3.2f, 1.1f, new Color(1f, 0.72f, 0.46f), sconce);
                light.renderMode = LightRenderMode.Auto;
            }
        }

        /// <summary>"CASINO MARES" in pink neon over the roulette area, on a dark board, with a little colored spill.</summary>
        static void BuildNeonSign(Transform hall, Materials m)
        {
            var sign = Child(hall, "NeonSign", new Vector3(0f, 2.72f, HallDepth / 2f - 0.17f));
            NoShadows(Box("Board", sign, new Vector3(0f, 0f, 0.01f), new Vector3(4.9f, 0.86f, 0.03f), m.DarkWood, collider: false));
            NoShadows(Box("Frame", sign, new Vector3(0f, 0f, 0.02f), new Vector3(5.02f, 0.98f, 0.02f), m.Brass, collider: false));
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Lettering";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(sign, false);
            quad.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            quad.transform.localScale = new Vector3(4.8f, 0.75f, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = m.Neon;
            NoShadows(quad);
            PointLight("Spill", new Vector3(0f, -0.2f, -0.6f), 3.2f, 1.4f, new Color(1f, 0.4f, 0.6f), sign);
        }

        /// <summary>Two shelves of bottles behind the bar, lit by glowing strips under each shelf.</summary>
        static void BuildBarShelf(Transform bar, Materials m)
        {
            var random = new System.Random(7);
            foreach (var y in new[] { 1.02f, 1.52f })
            {
                NoShadows(Box("Shelf", bar, new Vector3(-5.2f, y, 6.36f), new Vector3(4.6f, 0.04f, 0.22f), m.Wood, collider: false));
                NoShadows(Box("ShelfLight", bar, new Vector3(-5.2f, y - 0.03f, 6.3f), new Vector3(4.4f, 0.015f, 0.02f), m.ShelfGlow, collider: false));
                for (var x = -7.3f; x < -3.1f; x += 0.21f + (float)random.NextDouble() * 0.12f)
                {
                    var height = 0.2f + (float)random.NextDouble() * 0.12f;
                    var material = m.ShelfBottles[random.Next(m.ShelfBottles.Length)];
                    var bottle = Cylinder("Bottle", bar, new Vector3(x, y + 0.02f + height / 2f, 6.38f), new Vector3(0.075f, height / 2f, 0.075f), material, collider: false);
                    NoShadows(bottle);
                    NoShadows(Cylinder("Neck", bar, new Vector3(x, y + 0.02f + height + 0.04f, 6.38f), new Vector3(0.026f, 0.04f, 0.026f), material, collider: false));
                }
            }

            PointLight("ShelfGlow", new Vector3(-5.2f, 1.3f, 6.0f), 3f, 1.2f, new Color(1f, 0.62f, 0.32f), bar);
        }

        /// <summary>Pendant lamps: a pool of shadowed spot light over every game, with a soft light shaft and drifting dust.</summary>
        static void BuildLamps(Materials m)
        {
            var lamps = new GameObject("Lamps").transform;
            var cone = LightConeMesh();
            const float tableTop = 0.57f;
            Pendant(lamps, "Lamp_Blackjack_1", new Vector3(-3.6f, 2.75f, 0.8f), 1.45f, tableTop, 15f, true, cone, m);
            Pendant(lamps, "Lamp_Blackjack_2", new Vector3(3.6f, 2.75f, 0.8f), 1.45f, tableTop, 15f, true, cone, m);
            Pendant(lamps, "Lamp_Roulette_A", new Vector3(-0.75f, 2.75f, 4.3f), 1.1f, tableTop, 11f, true, cone, m);
            Pendant(lamps, "Lamp_Roulette_B", new Vector3(0.8f, 2.75f, 4.3f), 1.1f, tableTop, 11f, false, cone, m);
            Pendant(lamps, "Lamp_Baccarat", new Vector3(-0.4f, 2.75f, -3.4f), 1.35f, tableTop, 14f, true, cone, m);
            Pendant(lamps, "Lamp_Poker", new Vector3(-7.9f, 2.75f, -0.9f), 1.6f, tableTop, 15f, true, cone, m);
            Pendant(lamps, "Lamp_Bar_A", new Vector3(-6.4f, 2.55f, 5.15f), 0.85f, 0.85f, 8f, false, cone, m);
            Pendant(lamps, "Lamp_Bar_B", new Vector3(-4.0f, 2.55f, 5.15f), 0.85f, 0.85f, 8f, false, cone, m);
            Pendant(lamps, "Lamp_Lounge", new Vector3(6.6f, 2.65f, 1.4f), 1.5f, 0f, 11f, true, cone, m);
            Pendant(lamps, "Lamp_Cashier", new Vector3(-6f, 2.6f, -4.95f), 1.0f, 0.74f, 8f, false, cone, m);
            Pendant(lamps, "Lamp_Slots", new Vector3(9.4f, 2.75f, -3.25f), 1.4f, 0f, 10f, false, cone, m);
        }

        static void Pendant(Transform parent, string name, Vector3 position, float coneRadius, float coneBottom, float intensity, bool shadows, Mesh cone, Materials m)
        {
            var lamp = Child(parent, name, position);
            var cordLength = CeilingHeight - position.y - 0.12f;
            NoShadows(Cylinder("Cord", lamp, new Vector3(0f, 0.12f + cordLength / 2f, 0f), new Vector3(0.012f, cordLength / 2f, 0.012f), m.Brass, collider: false));
            NoShadows(Cylinder("Shade", lamp, new Vector3(0f, 0.06f, 0f), new Vector3(0.5f, 0.06f, 0.5f), m.LampShade, collider: false));
            NoShadows(Cylinder("Rim", lamp, new Vector3(0f, 0.004f, 0f), new Vector3(0.52f, 0.004f, 0.52f), m.Brass, collider: false));
            NoShadows(Cylinder("Glow", lamp, new Vector3(0f, -0.002f, 0f), new Vector3(0.42f, 0.002f, 0.42f), m.LampGlow, collider: false));

            var light = new GameObject("Spot").AddComponent<Light>();
            light.transform.SetParent(lamp, false);
            light.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            light.type = LightType.Spot;
            light.spotAngle = 88f;
            light.innerSpotAngle = 40f;
            light.range = position.y + 2f;
            light.intensity = intensity;
            light.color = new Color(1f, 0.8f, 0.58f);
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.85f;
            light.shadowNearPlane = 0.1f;

            var shaft = new GameObject("LightShaft");
            shaft.transform.SetParent(lamp, false);
            shaft.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            shaft.transform.localScale = new Vector3(coneRadius, position.y - coneBottom, coneRadius);
            shaft.AddComponent<MeshFilter>().sharedMesh = cone;
            var shaftRenderer = shaft.AddComponent<MeshRenderer>();
            shaftRenderer.sharedMaterial = m.Cone;
            NoShadows(shaft);

            Dust(lamp, coneRadius, position.y - coneBottom, m);
        }

        static void Dust(Transform lamp, float radius, float height, Materials m)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(lamp, false);
            go.transform.localPosition = new Vector3(0f, -height * 0.55f, 0f);
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.025f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new Color(1f, 0.9f, 0.75f, 0.7f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.002f;

            var emission = particles.emission;
            emission.rateOverTime = 5f;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 1.1f, height * 0.75f, radius * 1.1f);

            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.05f;

            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = m.Dust;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            NoShadows(go);
        }

        static void NoShadows(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = renderer is MeshRenderer && renderer.sharedMaterial != null &&
                                          renderer.sharedMaterial.shader.name == "Universal Render Pipeline/Lit";
            }
        }

        /// <summary>
        /// Cube map of the finished room for the brass, glass and glossy wood reflections (realtime probes are off
        /// in the quality settings, so it's baked here).
        /// </summary>
        static void BakeReflectionProbe() =>
            BakeReflectionProbe("ReflectionProbe", new Vector3(0f, 1.4f, 0f), new Vector3(HallWidth, CeilingHeight, HallDepth),
                new Vector3(0f, CeilingHeight / 2f - 1.4f, 0f), true, 256, ReflectionPath);

        static void BakeReflectionProbe(string name, Vector3 position, Vector3 size, Vector3 center, bool boxProjection, int resolution, string path)
        {
            var probe = new GameObject(name).AddComponent<ReflectionProbe>();
            probe.transform.position = position;
            probe.size = size;
            probe.center = center;
            probe.boxProjection = boxProjection;
            probe.resolution = resolution;
            probe.hdr = true;
            probe.mode = ReflectionProbeMode.Custom;
            AssetDatabase.DeleteAsset(path);
            if (Lightmapping.BakeReflectionProbe(probe, path))
            {
                AssetDatabase.ImportAsset(path);
                probe.customBakedTexture = Load<Cubemap>(path);
            }
        }

        // ---------------------------------------------------------------- Props (paw-pushable)

        enum PropKind { Glass, Mug, Bottle, Yarn }

        /// <summary>Glasses, mugs, a bottle and yarn balls on the tables, bar and floor, for cats to swat off (PawSwipe).</summary>
        static void BuildProps(Materials m)
        {
            var root = new GameObject("Props").transform;
            const float felt = 0.57f;
            const float barTop = 0.845f;
            var props = new List<(PropKind kind, Vector3 position, int variant)>
            {
                (PropKind.Glass, new Vector3(-3.6f - 1.0f, felt, 0.8f + 0.3f), 0),
                (PropKind.Glass, new Vector3(-3.6f + 1.0f, felt, 0.8f + 0.3f), 0),
                (PropKind.Glass, new Vector3(3.6f - 1.0f, felt, 0.8f + 0.3f), 0),
                (PropKind.Mug, new Vector3(3.6f + 1.0f, felt, 0.8f + 0.3f), 0),
                (PropKind.Glass, new Vector3(-1.25f, felt, 4.3f + 0.45f), 0),
                (PropKind.Glass, new Vector3(-0.4f + 1.05f, felt, -3.4f + 0.4f), 0),
                (PropKind.Glass, new Vector3(-7.9f + 1.1f, felt, -0.9f + 0.45f), 0),
                (PropKind.Mug, new Vector3(-7.9f - 1.1f, felt, -0.9f + 0.45f), 0),
                (PropKind.Glass, new Vector3(-7.2f, barTop, 4.95f), 0),
                (PropKind.Glass, new Vector3(-6.1f, barTop, 4.95f), 0),
                (PropKind.Bottle, new Vector3(-5.0f, barTop, 5.0f), 0),
                (PropKind.Glass, new Vector3(-4.3f, barTop, 4.95f), 0),
                (PropKind.Glass, new Vector3(-3.3f, barTop, 4.95f), 0),
                (PropKind.Mug, new Vector3(7.25f, 0.4f, 0.5f), 0),
                (PropKind.Yarn, new Vector3(7.6f, 0.4f, 0.8f), 0),
                (PropKind.Glass, new Vector3(-5.0f, 0.74f, -5.0f), 0),
                (PropKind.Yarn, new Vector3(5.7f, 0f, 0.1f), 1),
                (PropKind.Yarn, new Vector3(2.4f, 0f, -4.6f), 2),
                (PropKind.Yarn, new Vector3(-2.6f, 0f, 2.3f), 0),
            };

            for (var i = 0; i < props.Count; i++)
                Prop(root, $"{props[i].kind}_{i + 1}", props[i].kind, props[i].position, props[i].variant, m);
        }

        static void Prop(Transform parent, string name, PropKind kind, Vector3 position, int variant, Materials m)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, position.x * 97f % 360f, 0f));

            var body = root.AddComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 0.1f;
            body.angularDamping = 0.3f;

            var sound = SoundId.PropClink;
            switch (kind)
            {
                case PropKind.Glass:
                    body.mass = 0.3f;
                    Cylinder("Glass", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.075f, 0.05f, 0.075f), m.Glass, collider: false);
                    Cylinder("Drink", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(0.064f, 0.026f, 0.064f), m.Whisky, collider: false);
                    AddBoxCollider(root, new Vector3(0f, 0.05f, 0f), new Vector3(0.075f, 0.1f, 0.075f));
                    break;
                case PropKind.Mug:
                    body.mass = 0.35f;
                    sound = SoundId.PropThud;
                    Cylinder("Mug", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.09f, 0.05f, 0.09f), m.Ceramic, collider: false);
                    Cylinder("Coffee", root.transform, new Vector3(0f, 0.092f, 0f), new Vector3(0.08f, 0.004f, 0.08f), m.Coffee, collider: false);
                    Box("Handle", root.transform, new Vector3(0.055f, 0.05f, 0f), new Vector3(0.025f, 0.06f, 0.015f), m.Ceramic, collider: false);
                    AddBoxCollider(root, new Vector3(0f, 0.05f, 0f), new Vector3(0.09f, 0.1f, 0.09f));
                    break;
                case PropKind.Bottle:
                    body.mass = 0.6f;
                    Cylinder("Bottle", root.transform, new Vector3(0f, 0.11f, 0f), new Vector3(0.08f, 0.11f, 0.08f), m.BottleGlass, collider: false);
                    Cylinder("Neck", root.transform, new Vector3(0f, 0.26f, 0f), new Vector3(0.03f, 0.04f, 0.03f), m.BottleGlass, collider: false);
                    Cylinder("Cap", root.transform, new Vector3(0f, 0.305f, 0f), new Vector3(0.034f, 0.008f, 0.034f), m.Brass, collider: false);
                    AddBoxCollider(root, new Vector3(0f, 0.155f, 0f), new Vector3(0.08f, 0.31f, 0.08f));
                    break;
                case PropKind.Yarn:
                    body.mass = 0.08f;
                    body.angularDamping = 0.6f;
                    sound = SoundId.PropThud;
                    var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    ball.name = "Yarn";
                    Object.DestroyImmediate(ball.GetComponent<Collider>());
                    ball.transform.SetParent(root.transform, false);
                    ball.transform.localPosition = new Vector3(0f, 0.065f, 0f);
                    ball.transform.localScale = Vector3.one * 0.13f;
                    ball.GetComponent<MeshRenderer>().sharedMaterial = m.Yarn[variant % m.Yarn.Length];
                    var sphere = root.AddComponent<SphereCollider>();
                    sphere.center = new Vector3(0f, 0.065f, 0f);
                    sphere.radius = 0.065f;
                    break;
            }

            // Props move, so they shouldn't carve holes in the patrons' NavMesh.
            root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            var networkObject = root.AddComponent<NetworkObject>();
            networkObject.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
            networkObject.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
            root.AddComponent<NetworkTransform>();
            var knockable = root.AddComponent<Knockable>();
            var serialized = new SerializedObject(knockable);
            serialized.FindProperty("_impactSound").enumValueIndex = (int)sound;
            serialized.FindProperty("_homePosition").vector3Value = root.transform.position;
            serialized.FindProperty("_homeRotation").quaternionValue = root.transform.rotation;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddBoxCollider(GameObject go, Vector3 center, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        // ---------------------------------------------------------------- Blackjack table

        static BlackjackTable BuildBlackjackTable(string name, Vector3 position, Materials m, NpcCatVisual npcPrefab)
        {
            const float feltRadiusX = 1.25f;
            const float feltRadiusZ = 0.85f;
            const float feltTop = 0.57f;

            var root = new GameObject(name);
            root.transform.position = position;
            var networkObject = root.AddComponent<NetworkObject>();
            MakeMasterClientObject(networkObject);
            var table = root.AddComponent<BlackjackTable>();
            var view = root.AddComponent<BlackjackTableView>();
            var t = root.transform;

            // Body: pedestal, wooden rim, felt top (dealer side is +Z, players sit around the -Z half).
            Cylinder("Pedestal", t, new Vector3(0f, 0.26f, 0f), new Vector3(0.6f, 0.26f, 0.6f), m.DarkWood);
            Cylinder("Rim", t, new Vector3(0f, 0.535f, 0f), new Vector3(feltRadiusX * 2f + 0.14f, 0.025f, feltRadiusZ * 2f + 0.14f), m.Wood);
            Cylinder("Felt", t, new Vector3(0f, 0.55f, 0f), new Vector3(feltRadiusX * 2f, 0.02f, feltRadiusZ * 2f), m.Felt);
            Box("Shoe", t, new Vector3(0.72f, 0.62f, 0.42f), new Vector3(0.14f, 0.1f, 0.22f), m.DarkWood, collider: false);
            Box("DiscardTray", t, new Vector3(-0.72f, 0.585f, 0.42f), new Vector3(0.16f, 0.03f, 0.24f), m.DarkWood, collider: false);
            Box("ChipRack", t, new Vector3(0f, 0.58f, 0.62f), new Vector3(0.6f, 0.02f, 0.12f), m.Brass, collider: false);

            var shoe = Child(t, "ShoeMouth", new Vector3(0.72f, 0.68f, 0.3f), Quaternion.Euler(0f, 180f, 0f));
            var discard = Child(t, "Discard", new Vector3(-0.72f, 0.61f, 0.42f));
            var dealerCards = Child(t, "DealerCards", new Vector3(-0.2f, feltTop, 0.28f));

            // Dealer cat on a stool behind the table, facing the players.
            Cylinder("DealerStool", t, new Vector3(0f, 0.17f, 1.12f), new Vector3(0.42f, 0.17f, 0.42f), m.Velvet);
            var dealer = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, t);
            dealer.name = "Dealer";
            dealer.transform.localPosition = new Vector3(0f, 0.34f, 1.12f);
            dealer.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            dealer.SetFur(2);

            var seatAnchors = new Transform[BlackjackTable.SeatCount];
            var seatCameras = new CinemachineCamera[BlackjackTable.SeatCount];
            var handAnchors = new Transform[BlackjackTable.SeatCount];
            var dealerFocus = t.TransformPoint(new Vector3(0f, feltTop, 0.35f));

            for (var seat = 0; seat < BlackjackTable.SeatCount; seat++)
            {
                // Seat 0 is "first base" on the dealer's left (+X); seats sweep around the player side.
                var angle = Mathf.Deg2Rad * (-18f - seat * 36f);
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                var stoolLocal = new Vector3(dir.x * (feltRadiusX + 0.38f), 0f, dir.z * (feltRadiusZ + 0.38f));
                var stool = Cylinder($"Stool_{seat + 1}", t, stoolLocal + Vector3.up * 0.16f, new Vector3(0.42f, 0.16f, 0.42f), m.Velvet);
                Cylinder("Cushion", stool.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.95f, 0.12f, 0.95f), m.Cushion, collider: false, worldScale: false);

                var facing = Quaternion.LookRotation(-new Vector3(stoolLocal.x, 0f, stoolLocal.z).normalized, Vector3.up);
                var anchor = Child(t, $"Seat_{seat + 1}", stoolLocal + Vector3.up * 0.34f, facing);
                seatAnchors[seat] = anchor;

                var handLocal = new Vector3(dir.x * (feltRadiusX - 0.38f), feltTop, dir.z * (feltRadiusZ - 0.28f));
                var handFacing = Quaternion.LookRotation((new Vector3(0f, feltTop, 0.35f) - handLocal).WithY(0f).normalized, Vector3.up);
                handAnchors[seat] = Child(t, $"Hand_{seat + 1}", handLocal, handFacing);

                var seatComponent = stool.AddComponent<BlackjackSeat>();
                SetFields(seatComponent, ("_table", table));
                SetInt(seatComponent, "_seat", seat);
                SetFloat(seatComponent, "_radius", 0.85f);

                // Seat camera: high over the cat's shoulder so its own cards aren't hidden behind its head.
                var camera = new GameObject($"SeatCamera_{seat + 1}").AddComponent<CinemachineCamera>();
                camera.transform.SetParent(t, false);
                var cameraPosition = anchor.position - anchor.forward * 0.7f + anchor.right * 0.28f + Vector3.up * 1.25f;
                var focus = Vector3.Lerp(handAnchors[seat].position, dealerFocus, 0.3f);
                camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(focus - cameraPosition, Vector3.up));
                camera.Lens = LensSettings.Default;
                camera.Lens.FieldOfView = 52f;
                camera.Lens.NearClipPlane = 0.05f;
                camera.Priority = 0;
                seatCameras[seat] = camera;
            }


            SetArray(table, "_seatAnchors", seatAnchors);
            SetArray(table, "_seatCameras", seatCameras);
            SetString(table, "_displayName", name.Replace("Blackjack ", ""));

            SetFields(view,
                ("_table", table),
                ("_cardMaterial", m.Card),
                ("_chipMaterial", m.Chip),
                ("_chipMesh", Resources.GetBuiltinResource<Mesh>("Cylinder.fbx")),
                ("_shoe", shoe),
                ("_discard", discard),
                ("_dealerCards", dealerCards),
                ("_npcCatPrefab", npcPrefab),
                ("_dealer", dealer));
            SetArray(view, "_handAnchors", handAnchors);
            return table;
        }

        // ---------------------------------------------------------------- Roulette table

        static RouletteTable BuildRouletteTable(string name, Vector3 position, Materials m, NpcCatVisual npcPrefab)
        {
            const float feltTop = 0.57f;
            var root = new GameObject(name);
            root.transform.position = position;
            MakeMasterClientObject(root.AddComponent<NetworkObject>());
            var table = root.AddComponent<RouletteTable>();
            var view = root.AddComponent<RouletteView>();
            var t = root.transform;

            Box("Base", t, new Vector3(0f, 0.25f, 0f), new Vector3(2.3f, 0.5f, 0.8f), m.DarkWood);
            Box("Rim", t, new Vector3(0f, 0.53f, 0f), new Vector3(2.9f, 0.04f, 1.4f), m.Wood, collider: false);
            Box("Felt", t, new Vector3(0f, 0.55f, 0f), new Vector3(2.8f, 0.04f, 1.3f), m.Felt);

            // The wheel sits in a wooden bowl at the east end; the rotor spins, the ball is placed by the view. Bowl
            // and rotor meshes are lathed at runtime (RouletteWheelGeometry), with frets, diamonds and turret in brass.
            var wheelCenter = new Vector3(0.95f, feltTop, 0.1f);
            var bowl = new GameObject("WheelBowl");
            bowl.transform.SetParent(t, false);
            bowl.transform.localPosition = wheelCenter;
            var bowlMesh = bowl.AddComponent<MeshFilter>();
            bowl.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.Wood, m.DarkWood, m.Brass };
            // Something solid for cats to bump into; NPCs path around it rather than over the wheel.
            var wheelBlock = Cylinder("WheelCollider", t, wheelCenter + Vector3.up * 0.0675f, new Vector3(0.84f, 0.0675f, 0.84f), m.DarkWood);
            Object.DestroyImmediate(wheelBlock.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(wheelBlock.GetComponent<MeshFilter>());
            var blockArea = wheelBlock.AddComponent<NavMeshModifier>();
            blockArea.overrideArea = true;
            blockArea.area = 1; // Not Walkable

            var rotor = Child(t, "Rotor", wheelCenter);
            var face = new GameObject("WheelFace");
            face.transform.SetParent(rotor, false);
            face.AddComponent<MeshFilter>();
            var faceRenderer = face.AddComponent<MeshRenderer>();
            faceRenderer.sharedMaterials = new[] { m.Wheel, m.Brass };

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(t, false);
            ball.transform.localScale = Vector3.one * (RouletteWheelGeometry.BallRadius * 2f);
            ball.GetComponent<MeshRenderer>().sharedMaterial = m.Ball;

            // Seated players watch the spin from here: over the players' side of the wheel, looking down into the bowl.
            var wheelWorld = t.TransformPoint(wheelCenter);
            var wheelCamera = new GameObject("WheelCamera").AddComponent<CinemachineCamera>();
            wheelCamera.transform.SetParent(t, false);
            var wheelCameraPosition = t.TransformPoint(wheelCenter + new Vector3(-0.3f, 0.86f, -0.7f));
            var wheelFocus = t.TransformPoint(wheelCenter + new Vector3(0f, 0.03f, 0.04f));
            wheelCamera.transform.SetPositionAndRotation(wheelCameraPosition, Quaternion.LookRotation(wheelFocus - wheelCameraPosition, Vector3.up));
            wheelCamera.Lens = LensSettings.Default;
            wheelCamera.Lens.FieldOfView = 44f;
            wheelCamera.Lens.NearClipPlane = 0.05f;
            wheelCamera.Priority = 0;

            Cylinder("CroupierStool", t, new Vector3(0.1f, 0.17f, 1.02f), new Vector3(0.42f, 0.17f, 0.42f), m.Velvet);
            var croupier = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, t);
            croupier.name = "Croupier";
            croupier.transform.localPosition = new Vector3(0.1f, 0.34f, 1.02f);
            croupier.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            croupier.SetFur(0);

            var seatAnchors = new Transform[RouletteTable.SeatCount];
            var seatCameras = new CinemachineCamera[RouletteTable.SeatCount];
            var chipSpots = new Transform[RouletteTable.SeatCount];

            for (var seat = 0; seat < RouletteTable.SeatCount; seat++)
            {
                var x = -1.25f + seat * 0.5f;
                var stool = Cylinder($"Stool_{seat + 1}", t, new Vector3(x, 0.16f, -1.02f), new Vector3(0.42f, 0.16f, 0.42f), m.Velvet);
                Cylinder("Cushion", stool.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.95f, 0.12f, 0.95f), m.Cushion, collider: false, worldScale: false);
                var anchor = Child(t, $"Seat_{seat + 1}", new Vector3(x, 0.34f, -1.02f));
                seatAnchors[seat] = anchor;
                chipSpots[seat] = Child(t, $"Chips_{seat + 1}", new Vector3(x, feltTop, -0.42f));

                var seatComponent = stool.AddComponent<RouletteSeat>();
                SetFields(seatComponent, ("_table", table));
                SetInt(seatComponent, "_seat", seat);
                SetFloat(seatComponent, "_radius", 0.6f);

                var camera = new GameObject($"SeatCamera_{seat + 1}").AddComponent<CinemachineCamera>();
                camera.transform.SetParent(t, false);
                var cameraPosition = anchor.position - anchor.forward * 0.75f + Vector3.up * 1.3f;
                var focus = Vector3.Lerp(chipSpots[seat].position, wheelWorld, 0.55f);
                camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(focus - cameraPosition, Vector3.up));
                camera.Lens = LensSettings.Default;
                camera.Lens.FieldOfView = 55f;
                camera.Lens.NearClipPlane = 0.05f;
                camera.Priority = 0;
                seatCameras[seat] = camera;
            }


            SetArray(table, "_seatAnchors", seatAnchors);
            SetArray(table, "_seatCameras", seatCameras);
            SetFields(table, ("_wheelCamera", wheelCamera));
            SetString(table, "_displayName", name);
            SetFields(view,
                ("_table", table),
                ("_bowl", bowlMesh),
                ("_rotor", rotor),
                ("_wheelFace", faceRenderer),
                ("_ball", ball.transform),
                ("_chipMaterial", m.Chip),
                ("_chipMesh", Resources.GetBuiltinResource<Mesh>("Cylinder.fbx")),
                ("_npcCatPrefab", npcPrefab),
                ("_croupier", croupier));
            SetArray(view, "_chipSpots", chipSpots);
            return table;
        }

        // ---------------------------------------------------------------- Baccarat table

        static BaccaratTable BuildBaccaratTable(string name, Vector3 position, Materials m, NpcCatVisual npcPrefab)
        {
            const float feltTop = 0.57f;
            var root = new GameObject(name);
            root.transform.position = position;
            MakeMasterClientObject(root.AddComponent<NetworkObject>());
            var table = root.AddComponent<BaccaratTable>();
            var view = root.AddComponent<BaccaratView>();
            var t = root.transform;

            Box("Base", t, new Vector3(0f, 0.25f, 0f), new Vector3(1.8f, 0.5f, 0.7f), m.DarkWood);
            Box("Rim", t, new Vector3(0f, 0.53f, 0f), new Vector3(2.5f, 0.04f, 1.3f), m.Wood, collider: false);
            Box("Felt", t, new Vector3(0f, 0.55f, 0f), new Vector3(2.4f, 0.04f, 1.2f), m.Felt);
            Box("PlayerArea", t, new Vector3(-0.45f, feltTop + 0.001f, 0.1f), new Vector3(0.62f, 0.002f, 0.32f), m.BaccaratPlayer, collider: false);
            Box("BankerArea", t, new Vector3(0.45f, feltTop + 0.001f, 0.1f), new Vector3(0.62f, 0.002f, 0.32f), m.BaccaratBanker, collider: false);
            Box("Shoe", t, new Vector3(0.95f, 0.62f, 0.38f), new Vector3(0.14f, 0.1f, 0.22f), m.DarkWood, collider: false);
            Box("DiscardTray", t, new Vector3(-0.95f, 0.585f, 0.38f), new Vector3(0.16f, 0.03f, 0.24f), m.DarkWood, collider: false);

            var playerBox = Child(t, "PlayerBox", new Vector3(-0.45f, feltTop, 0.1f));
            var bankerBox = Child(t, "BankerBox", new Vector3(0.45f, feltTop, 0.1f));
            var shoe = Child(t, "ShoeMouth", new Vector3(0.95f, 0.68f, 0.26f), Quaternion.Euler(0f, 180f, 0f));
            var discard = Child(t, "Discard", new Vector3(-0.95f, 0.61f, 0.38f));

            Cylinder("CroupierStool", t, new Vector3(0f, 0.17f, 0.95f), new Vector3(0.42f, 0.17f, 0.42f), m.Velvet);
            var croupier = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, t);
            croupier.name = "Croupier";
            croupier.transform.localPosition = new Vector3(0f, 0.34f, 0.95f);
            croupier.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            croupier.SetFur(3);

            var seatAnchors = new Transform[BaccaratTable.SeatCount];
            var seatCameras = new CinemachineCamera[BaccaratTable.SeatCount];
            var chipSpots = new Transform[BaccaratTable.SeatCount];
            var focusPoint = t.TransformPoint(new Vector3(0f, feltTop, 0.1f));
            for (var seat = 0; seat < BaccaratTable.SeatCount; seat++)
            {
                var x = -1.0f + seat * 0.5f;
                var stool = Cylinder($"Stool_{seat + 1}", t, new Vector3(x, 0.16f, -0.95f), new Vector3(0.42f, 0.16f, 0.42f), m.Velvet);
                Cylinder("Cushion", stool.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.95f, 0.12f, 0.95f), m.Cushion, collider: false, worldScale: false);
                var anchor = Child(t, $"Seat_{seat + 1}", new Vector3(x, 0.34f, -0.95f));
                seatAnchors[seat] = anchor;
                chipSpots[seat] = Child(t, $"Chips_{seat + 1}", new Vector3(x, feltTop, -0.38f));

                var seatComponent = stool.AddComponent<BaccaratSeat>();
                SetFields(seatComponent, ("_table", table));
                SetInt(seatComponent, "_seat", seat);
                SetFloat(seatComponent, "_radius", 0.6f);

                var camera = new GameObject($"SeatCamera_{seat + 1}").AddComponent<CinemachineCamera>();
                camera.transform.SetParent(t, false);
                var cameraPosition = anchor.position - anchor.forward * 0.7f + Vector3.up * 1.25f;
                var focus = Vector3.Lerp(chipSpots[seat].position, focusPoint, 0.6f);
                camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(focus - cameraPosition, Vector3.up));
                camera.Lens = LensSettings.Default;
                camera.Lens.FieldOfView = 52f;
                camera.Lens.NearClipPlane = 0.05f;
                camera.Priority = 0;
                seatCameras[seat] = camera;
            }


            SetArray(table, "_seatAnchors", seatAnchors);
            SetArray(table, "_seatCameras", seatCameras);
            SetString(table, "_displayName", name);
            SetFields(view,
                ("_table", table),
                ("_cardMaterial", m.Card),
                ("_chipMaterial", m.Chip),
                ("_chipMesh", Resources.GetBuiltinResource<Mesh>("Cylinder.fbx")),
                ("_shoe", shoe),
                ("_discard", discard),
                ("_playerBox", playerBox),
                ("_bankerBox", bankerBox),
                ("_npcCatPrefab", npcPrefab),
                ("_croupier", croupier));
            SetArray(view, "_chipSpots", chipSpots);
            return table;
        }

        // ---------------------------------------------------------------- Poker table

        static PokerTable BuildPokerTable(string name, Vector3 position, Materials m, NpcCatVisual npcPrefab)
        {
            const float radiusX = 1.45f;
            const float radiusZ = 1.0f;
            const float feltTop = 0.57f;

            var root = new GameObject(name);
            root.transform.position = position;
            MakeMasterClientObject(root.AddComponent<NetworkObject>());
            var table = root.AddComponent<PokerTable>();
            var view = root.AddComponent<PokerView>();
            var t = root.transform;

            Cylinder("Pedestal", t, new Vector3(0f, 0.26f, 0f), new Vector3(0.7f, 0.26f, 0.7f), m.DarkWood);
            Cylinder("Rim", t, new Vector3(0f, 0.535f, 0f), new Vector3(radiusX * 2f + 0.16f, 0.025f, radiusZ * 2f + 0.16f), m.Wood);
            Cylinder("Felt", t, new Vector3(0f, 0.55f, 0f), new Vector3(radiusX * 2f, 0.02f, radiusZ * 2f), m.Felt);
            Box("DeckBox", t, new Vector3(0.3f, 0.6f, 0.72f), new Vector3(0.1f, 0.05f, 0.14f), m.DarkWood, collider: false);

            var board = Child(t, "Board", new Vector3(0f, feltTop, 0.08f));
            var pot = Child(t, "Pot", new Vector3(0f, feltTop, -0.28f));
            var deck = Child(t, "Deck", new Vector3(0.3f, 0.64f, 0.72f), Quaternion.Euler(0f, 180f, 0f));
            var muck = Child(t, "Muck", new Vector3(-0.35f, 0.59f, 0.72f));

            var button = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            button.name = "DealerButton";
            Object.DestroyImmediate(button.GetComponent<Collider>());
            button.transform.SetParent(t, false);
            button.transform.localPosition = new Vector3(0f, feltTop + 0.005f, 0f);
            button.transform.localScale = new Vector3(0.07f, 0.006f, 0.07f);
            button.GetComponent<MeshRenderer>().sharedMaterial = m.Ball;

            Cylinder("DealerStool", t, new Vector3(0f, 0.17f, radiusZ + 0.42f), new Vector3(0.42f, 0.17f, 0.42f), m.Velvet);
            var dealer = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, t);
            dealer.name = "Dealer";
            dealer.transform.localPosition = new Vector3(0f, 0.34f, radiusZ + 0.42f);
            dealer.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            dealer.SetFur(5);

            var seatAnchors = new Transform[PokerTable.SeatCount];
            var seatCameras = new CinemachineCamera[PokerTable.SeatCount];
            var cardSpots = new Transform[PokerTable.SeatCount];
            var stackSpots = new Transform[PokerTable.SeatCount];
            var betSpots = new Transform[PokerTable.SeatCount];
            var boardWorld = board.position;

            for (var seat = 0; seat < PokerTable.SeatCount; seat++)
            {
                // Six seats around the oval, leaving the dealer's end (+Z) free.
                var angle = Mathf.Deg2Rad * (20f - seat * 44f);
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var toCenter = Quaternion.LookRotation(-dir, Vector3.up);

                var stoolLocal = new Vector3(dir.x * (radiusX + 0.38f), 0f, dir.z * (radiusZ + 0.38f));
                var stool = Cylinder($"Stool_{seat + 1}", t, stoolLocal + Vector3.up * 0.16f, new Vector3(0.42f, 0.16f, 0.42f), m.Velvet);
                Cylinder("Cushion", stool.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.95f, 0.12f, 0.95f), m.Cushion, collider: false, worldScale: false);
                var anchor = Child(t, $"Seat_{seat + 1}", stoolLocal + Vector3.up * 0.34f, Quaternion.LookRotation(-new Vector3(stoolLocal.x, 0f, stoolLocal.z).normalized, Vector3.up));
                seatAnchors[seat] = anchor;

                cardSpots[seat] = Child(t, $"Cards_{seat + 1}", new Vector3(dir.x * radiusX * 0.66f, feltTop, dir.z * radiusZ * 0.62f), toCenter);
                var right = Vector3.Cross(Vector3.up, -dir).normalized;
                stackSpots[seat] = Child(t, $"Stack_{seat + 1}", new Vector3(dir.x * radiusX * 0.8f, feltTop, dir.z * radiusZ * 0.78f) + right * 0.22f, toCenter);
                betSpots[seat] = Child(t, $"Bet_{seat + 1}", new Vector3(dir.x * radiusX * 0.45f, feltTop, dir.z * radiusZ * 0.42f), toCenter);

                var seatComponent = stool.AddComponent<PokerSeat>();
                SetFields(seatComponent, ("_table", table));
                SetInt(seatComponent, "_seat", seat);
                SetFloat(seatComponent, "_radius", 0.7f);

                var camera = new GameObject($"SeatCamera_{seat + 1}").AddComponent<CinemachineCamera>();
                camera.transform.SetParent(t, false);
                var cameraPosition = anchor.position - anchor.forward * 0.7f + anchor.right * 0.25f + Vector3.up * 1.3f;
                var focus = Vector3.Lerp(cardSpots[seat].position, boardWorld, 0.5f);
                camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(focus - cameraPosition, Vector3.up));
                camera.Lens = LensSettings.Default;
                camera.Lens.FieldOfView = 55f;
                camera.Lens.NearClipPlane = 0.05f;
                camera.Priority = 0;
                seatCameras[seat] = camera;
            }


            SetArray(table, "_seatAnchors", seatAnchors);
            SetArray(table, "_seatCameras", seatCameras);
            SetString(table, "_displayName", name);
            SetFields(view,
                ("_table", table),
                ("_cardMaterial", m.Card),
                ("_chipMaterial", m.Chip),
                ("_chipMesh", Resources.GetBuiltinResource<Mesh>("Cylinder.fbx")),
                ("_deck", deck),
                ("_muck", muck),
                ("_board", board),
                ("_pot", pot),
                ("_button", button.transform),
                ("_npcCatPrefab", npcPrefab),
                ("_dealer", dealer));
            SetArray(view, "_cardSpots", cardSpots);
            SetArray(view, "_stackSpots", stackSpots);
            SetArray(view, "_betSpots", betSpots);
            return table;
        }

        // ---------------------------------------------------------------- Slot machines

        /// <summary>A slot machine against the east wall, facing west (-X), with its stool in front.</summary>
        static SlotMachine BuildSlotMachine(string name, Vector3 position, Materials m)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, -90f, 0f));
            MakeMasterClientObject(root.AddComponent<NetworkObject>());
            var machine = root.AddComponent<SlotMachine>();
            var view = root.AddComponent<SlotMachineView>();
            var t = root.transform;

            // Local frame: +Z is the machine's front (facing the player), +Y up.
            Box("Cabinet", t, new Vector3(0f, 0.65f, 0f), new Vector3(0.8f, 1.3f, 0.5f), m.Cabinet);
            Box("Topper", t, new Vector3(0f, 1.38f, 0.02f), new Vector3(0.84f, 0.16f, 0.54f), m.Brass, collider: false);
            Box("Marquee", t, new Vector3(0f, 1.38f, 0.292f), new Vector3(0.72f, 0.1f, 0.01f), m.SlotGlow, collider: false);
            Box("ReelWindow", t, new Vector3(0f, 0.95f, 0.251f), new Vector3(0.66f, 0.3f, 0.01f), m.DarkWood, collider: false);
            Box("Ledge", t, new Vector3(0f, 0.55f, 0.32f), new Vector3(0.7f, 0.05f, 0.18f), m.Brass, collider: false);

            var reels = new MeshRenderer[SlotRules.Reels];
            for (var i = 0; i < SlotRules.Reels; i++)
            {
                var reel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                reel.name = $"Reel_{i + 1}";
                Object.DestroyImmediate(reel.GetComponent<Collider>());
                reel.transform.SetParent(t, false);
                // Seen from the front, the machine's local +X is on the player's left, so reel 0 goes at +X.
                reel.transform.localPosition = new Vector3((1 - i) * 0.21f, 0.95f, 0.258f);
                // Quads face -Z; turn it to face the machine's front (+Z).
                reel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                reel.transform.localScale = new Vector3(0.19f, 0.26f, 1f);
                var renderer = reel.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = m.Reel;
                reels[i] = renderer;
            }

            var light = PointLight("MachineLight", new Vector3(0f, 1.2f, 0.6f), 2f, 1.2f, new Color(1f, 0.6f, 0.4f), t);

            var stool = Cylinder("Stool", t, new Vector3(0f, 0.16f, 0.72f), new Vector3(0.42f, 0.16f, 0.42f), m.Velvet);
            Cylinder("Cushion", stool.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.95f, 0.12f, 0.95f), m.Cushion, collider: false, worldScale: false);
            var anchor = Child(t, "Seat", new Vector3(0f, 0.34f, 0.72f), Quaternion.Euler(0f, 180f, 0f));
            var seat = stool.AddComponent<SlotSeat>();
            SetFields(seat, ("_machine", machine));
            SetFloat(seat, "_radius", 0.7f);

            var camera = new GameObject("SeatCamera").AddComponent<CinemachineCamera>();
            camera.transform.SetParent(t, false);
            var cameraPosition = anchor.position - anchor.forward * 0.8f + Vector3.up * 0.95f;
            var focus = t.TransformPoint(new Vector3(0f, 0.95f, 0.25f));
            camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(focus - cameraPosition, Vector3.up));
            camera.Lens = LensSettings.Default;
            camera.Lens.FieldOfView = 50f;
            camera.Lens.NearClipPlane = 0.05f;
            camera.Priority = 0;

            SetFields(machine, ("_seatAnchor", anchor), ("_seatCamera", camera));
            SetString(machine, "_displayName", name);
            SetFields(view, ("_machine", machine), ("_light", light));
            SetArray(view, "_reels", reels);
            return machine;
        }

        // ---------------------------------------------------------------- Bar, cashier, lounge, board

        static void BuildBar(Transform hall, Materials m, NpcCatVisual npcPrefab)
        {
            var bar = new GameObject("Bar").transform;
            bar.SetParent(hall, false);
            Box("Counter", bar, new Vector3(-5.2f, 0.4f, 5.3f), new Vector3(5f, 0.8f, 0.8f), m.Wood);
            // Solid tops so props (and cats that jump up) rest on the visible surface.
            Box("CounterTop", bar, new Vector3(-5.2f, 0.82f, 5.25f), new Vector3(5.2f, 0.05f, 0.95f), m.DarkWood);
            Box("BackShelf", bar, new Vector3(-5.2f, 1.4f, 6.65f), new Vector3(5f, 1.6f, 0.4f), m.DarkWood);
            Box("BartenderStep", bar, new Vector3(-5.2f, 0.25f, 6.0f), new Vector3(1.2f, 0.5f, 0.6f), m.DarkWood);
            BuildBarShelf(bar, m);
            for (var i = 0; i < 4; i++)
                Cylinder($"BarStool_{i + 1}", bar, new Vector3(-6.9f + i * 1.1f, 0.2f, 4.45f), new Vector3(0.4f, 0.2f, 0.4f), m.Velvet);

            var bartender = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, bar);
            bartender.name = "Bartender";
            bartender.transform.localPosition = new Vector3(-5.2f, 0.5f, 6.0f);
            bartender.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var staff = bartender.gameObject.AddComponent<StaffCat>();
            SetFields(staff, ("_visual", bartender));
            SetInt(staff, "_fur", 1);

        }

        static void BuildCashier(Transform hall, Materials m, NpcCatVisual npcPrefab)
        {
            var cashier = new GameObject("Cashier").transform;
            cashier.SetParent(hall, false);
            Box("Desk", cashier, new Vector3(-6f, 0.35f, -5.3f), new Vector3(3f, 0.7f, 0.7f), m.Wood);
            Box("DeskTop", cashier, new Vector3(-6f, 0.72f, -5.3f), new Vector3(3.1f, 0.04f, 0.8f), m.DarkWood);
            Box("CashierStep", cashier, new Vector3(-6f, 0.22f, -6.1f), new Vector3(1f, 0.44f, 0.6f), m.DarkWood);

            var clerk = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, cashier);
            clerk.name = "CashierCat";
            clerk.transform.localPosition = new Vector3(-6f, 0.44f, -6.1f);
            var staff = clerk.gameObject.AddComponent<StaffCat>();
            SetFields(staff, ("_visual", clerk));
            SetInt(staff, "_fur", 4);

            var desk = Child(cashier, "CatWelfareWindow", new Vector3(-6f, 0f, -4.6f)).gameObject.AddComponent<CashierDesk>();
            SetFloat(desk, "_radius", 1.4f);

        }

        static void BuildLounge(Transform hall, Materials m)
        {
            var lounge = new GameObject("Lounge").transform;
            lounge.SetParent(hall, false);
            Box("Rug", lounge, new Vector3(6.4f, 0.005f, 1.5f), new Vector3(3.2f, 0.01f, 3.6f), m.Velvet, collider: false);
            for (var i = 0; i < 3; i++)
                Cylinder($"Cushion_{i + 1}", lounge, new Vector3(6.4f + (i - 1) * 0.9f, 0.08f, 2.6f - i * 0.9f), new Vector3(0.75f, 0.08f, 0.75f), m.Cushion);
            Box("LowTable", lounge, new Vector3(7.4f, 0.2f, 0.6f), new Vector3(0.9f, 0.4f, 0.9f), m.DarkWood);
        }

        static void BuildLeaderboardBoard(Materials m)
        {
            var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
            board.name = "LeaderboardBoard";
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.transform.position = new Vector3(5.2f, 1.85f, HallDepth / 2f - 0.17f);
            board.transform.localScale = new Vector3(3.2f, 2f, 1f);
            board.GetComponent<MeshRenderer>().sharedMaterial = m.Board;
            Box("BoardFrame", null, new Vector3(5.2f, 1.85f, HallDepth / 2f - 0.14f), new Vector3(3.4f, 2.2f, 0.04f), m.Brass, collider: false);

            var document = new GameObject("UI_LeaderboardBoard").AddComponent<UIDocument>();
            document.panelSettings = Load<PanelSettings>(BoardPanelSettingsPath);
            document.visualTreeAsset = Load<VisualTreeAsset>($"{Root}/UI/LeaderboardBoard.uxml");
            SetFields(document.gameObject.AddComponent<LeaderboardBoard>(), ("_document", document));
        }

        // ---------------------------------------------------------------- Patrons

        static void BuildPatronSpots(BlackjackTable[] tables)
        {
            var root = new GameObject(PatronSpotsName).transform;
            void Spot(string name, Vector3 position, float yaw, bool sit, Vector2? stay = null) => AddPatronSpot(root, name, position, yaw, sit, stay);

            for (var i = 0; i < 4; i++)
                Spot($"BarStool_{i + 1}", new Vector3(-6.9f + i * 1.1f, 0.4f, 4.45f), 0f, true, new Vector2(10f, 25f));
            for (var i = 0; i < 3; i++)
                Spot($"LoungeCushion_{i + 1}", new Vector3(6.4f + (i - 1) * 0.9f, 0.16f, 2.6f - i * 0.9f), -120f, true, new Vector2(12f, 30f));

            foreach (var table in tables)
            {
                var p = table.transform.position;
                Spot($"Watch_{table.name}_A", p + new Vector3(-1.9f, 0f, -1.9f), 40f, false);
                Spot($"Watch_{table.name}_B", p + new Vector3(1.9f, 0f, -1.9f), -40f, false);
            }

            Spot("Watch_Leaderboard", new Vector3(5.2f, 0f, 5f), 0f, false);
            Spot("Watch_Poker_A", new Vector3(-5.6f, 0f, -2.6f), -120f, false);
            Spot("Watch_Poker_B", new Vector3(-9.8f, 0f, 1.4f), 140f, false);
            Spot("Entrance", new Vector3(3f, 0f, -5f), 90f, false);
            Spot("Cashier_Queue", new Vector3(-4f, 0f, -4.2f), -110f, false);
            Spot("Watch_Roulette_A", new Vector3(-2.2f, 0f, 3.2f), 60f, false);
            Spot("Watch_Roulette_B", new Vector3(2.4f, 0f, 3.0f), -60f, false);
        }

        const string PatronSpotsName = "PatronSpots";

        static PatronSpot AddPatronSpot(Transform root, string name, Vector3 position, float yaw, bool sit, Vector2? stay = null)
        {
            var spot = Child(root, name, position, Quaternion.Euler(0f, yaw, 0f)).gameObject.AddComponent<PatronSpot>();
            var serialized = new SerializedObject(spot);
            serialized.FindProperty("_sit").boolValue = sit;
            if (stay.HasValue)
                serialized.FindProperty("_stayRange").vector2Value = stay.Value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return spot;
        }

        static void BuildPatrons(NpcCatVisual npcPrefab)
        {
            var root = new GameObject("Patrons").transform;
            // Enough cats to wander, hang around the bar and still form a crowd around a winning streak (WinnerCrowd).
            var starts = new[]
            {
                new Vector3(-1f, 0f, -3f), new Vector3(1.5f, 0f, -2f), new Vector3(0f, 0f, 3f),
                new Vector3(6f, 0f, -1f), new Vector3(-6f, 0f, 3f), new Vector3(2.5f, 0f, -5f),
                new Vector3(-3f, 0f, -5.5f), new Vector3(8f, 0f, 1.5f), new Vector3(-9f, 0f, -4f),
                new Vector3(0.5f, 0f, 1.2f),
            };

            for (var i = 0; i < starts.Length; i++)
            {
                var patron = new GameObject($"Patron_{i + 1}");
                patron.transform.SetParent(root, false);
                patron.transform.position = starts[i];
                patron.transform.rotation = Quaternion.Euler(0f, i * 137f, 0f);
                var networkObject = patron.AddComponent<NetworkObject>();
                MakeMasterClientObject(networkObject);
                patron.AddComponent<NetworkTransform>();
                var brain = patron.AddComponent<PatronBrain>();

                var visual = (NpcCatVisual)PrefabUtility.InstantiatePrefab(npcPrefab, patron.transform);
                visual.name = "Body";
                SetFields(brain, ("_visual", visual));
            }
        }

        // ---------------------------------------------------------------- Cameras, post, UI, navmesh

        static void BuildCameras()
        {
            var mainCamera = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = mainCamera.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
            camera.clearFlags = _sea ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.05f, 0.06f);
            mainCamera.AddComponent<AudioListener>();
            var cameraData = mainCamera.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var brain = mainCamera.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.8f);

            var menuCamera = new GameObject("MenuCamera").AddComponent<CinemachineCamera>();
            // Under the ceiling, looking across the room toward the neon sign.
            menuCamera.transform.SetPositionAndRotation(new Vector3(0f, 2.9f, -6.3f), Quaternion.Euler(17f, 0f, 0f));
            menuCamera.Lens = LensSettings.Default;
            menuCamera.Lens.FieldOfView = 55f;
            menuCamera.Lens.NearClipPlane = 0.05f;

            var followCamera = new GameObject("PlayerFollowCamera").AddComponent<CinemachineCamera>();
            followCamera.Lens = LensSettings.Default;
            followCamera.Lens.FieldOfView = 55f;
            followCamera.Lens.NearClipPlane = 0.05f;
            var orbital = followCamera.gameObject.AddComponent<CinemachineOrbitalFollow>();
            orbital.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbital.Radius = 2.2f;
            orbital.HorizontalAxis.Range = new Vector2(-180f, 180f);
            orbital.HorizontalAxis.Wrap = true;
            orbital.VerticalAxis.Range = new Vector2(-10f, 60f);
            orbital.VerticalAxis.Value = 15f;
            orbital.VerticalAxis.Center = 15f;
            var composer = followCamera.gameObject.AddComponent<CinemachineRotationComposer>();
            composer.Damping = new Vector2(0.1f, 0.1f);
            var deoccluder = followCamera.gameObject.AddComponent<CinemachineDeoccluder>();
            deoccluder.IgnoreTag = "Player";
            deoccluder.CollideAgainst = 1 | (1 << CameraBlockerLayer);
            deoccluder.MinimumDistanceFromTarget = 0.3f;
            deoccluder.AvoidObstacles.Enabled = true;
            deoccluder.AvoidObstacles.CameraRadius = 0.12f;
            deoccluder.AvoidObstacles.DistanceLimit = 0f;
            deoccluder.AvoidObstacles.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            deoccluder.AvoidObstacles.Damping = 0.2f;

            var rig = new GameObject("PlayerCameraRig").AddComponent<PlayerCameraRig>();
            SetFields(rig, ("_menuCamera", menuCamera), ("_followCamera", followCamera));
        }

        static void BuildPostProcessing()
        {
            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = Load<VolumeProfile>(VolumeProfilePath);
        }

        static void BuildUI(PanelSettings panelSettings, CatFurPalette palette)
        {
            var menu = CreateDocument("UI_MainMenu", "MainMenu.uxml", panelSettings, 10);
            SetFields(menu.gameObject.AddComponent<MainMenuController>(), ("_document", menu), ("_palette", palette));

            var hud = CreateDocument("UI_Hud", "Hud.uxml", panelSettings, 5);
            SetFields(hud.gameObject.AddComponent<HudController>(), ("_document", hud));

            var blackjack = CreateDocument("UI_Blackjack", "Blackjack.uxml", panelSettings, 6);
            SetFields(blackjack.gameObject.AddComponent<BlackjackHud>(), ("_document", blackjack));

            var roulette = CreateDocument("UI_Roulette", "Roulette.uxml", panelSettings, 6);
            SetFields(roulette.gameObject.AddComponent<RouletteHud>(), ("_document", roulette));

            var slots = CreateDocument("UI_Slots", "Slots.uxml", panelSettings, 6);
            SetFields(slots.gameObject.AddComponent<SlotsHud>(), ("_document", slots));

            var baccarat = CreateDocument("UI_Baccarat", "Baccarat.uxml", panelSettings, 6);
            SetFields(baccarat.gameObject.AddComponent<BaccaratHud>(), ("_document", baccarat));

            var poker = CreateDocument("UI_Poker", "Poker.uxml", panelSettings, 6);
            SetFields(poker.gameObject.AddComponent<PokerHud>(), ("_document", poker));

            var chat = CreateDocument("UI_Chat", "Chat.uxml", panelSettings, 7);
            SetFields(chat.gameObject.AddComponent<ChatPanel>(), ("_document", chat));

            var overlay = CreateDocument("UI_TableOverlay", "Overlay.uxml", panelSettings, 1);
            SetFields(overlay.gameObject.AddComponent<TableOverlay>(), ("_document", overlay));

            var nameplates = CreateDocument("UI_Nameplates", "Nameplates.uxml", panelSettings, 0);
            SetFields(nameplates.gameObject.AddComponent<NameplateLayer>(), ("_document", nameplates));

            var gift = CreateDocument("UI_GiftDialog", "GiftDialog.uxml", panelSettings, 20);
            SetFields(gift.gameObject.AddComponent<GiftDialog>(), ("_document", gift));

            var emotes = CreateDocument("UI_EmoteWheel", "EmoteWheel.uxml", panelSettings, 8);
            SetFields(emotes.gameObject.AddComponent<EmoteWheel>(), ("_document", emotes));

            // Below every other document: its look/move surface only gets the touches no button or panel wants.
            var touch = CreateDocument("UI_Touch", "TouchControls.uxml", panelSettings, -10);
            SetFields(touch.gameObject.AddComponent<TouchControls>(), ("_document", touch));
        }

        static UIDocument CreateDocument(string name, string uxml, PanelSettings panelSettings, int sortOrder)
        {
            var document = new GameObject(name).AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = Load<VisualTreeAsset>($"{Root}/UI/{uxml}");
            document.sortingOrder = sortOrder;
            return document;
        }

        static void BakeNavMesh(Transform hall)
        {
            var surface = hall.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = ~(1 << CameraBlockerLayer);
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            AssetDatabase.DeleteAsset(NavMeshPath);
            if (surface.navMeshData != null)
                AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        }

        // ================================================================ Helpers

        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collider = true)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (material != null && WorldUvTiles.TryGetValue(material, out var tile))
                box.GetComponent<MeshFilter>().sharedMesh = WorldUvCube(size, tile);
            if (!collider)
                Object.DestroyImmediate(box.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(box, StaticEditorFlags.BatchingStatic);
            return box;
        }

        /// <summary>
        /// A cylinder with a mesh collider (the primitive's capsule collider turns flat, wide cylinders into big
        /// spheres). Size is the full width/height/depth for world-scaled cylinders.
        /// </summary>
        static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collider = true, bool worldScale = true)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = position;
            // Unity's cylinder is 1 wide and 2 tall; "size" y is a half-height to match the other call sites.
            cylinder.transform.localScale = worldScale ? new Vector3(size.x, size.y, size.z) : size;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            if (collider)
            {
                var meshCollider = cylinder.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = cylinder.GetComponent<MeshFilter>().sharedMesh;
                meshCollider.convex = true;
            }

            return cylinder;
        }

        static void CameraBlocker(string name, Transform parent, Vector3 position, Vector3 size)
        {
            var blocker = Child(parent, name, position);
            blocker.gameObject.layer = CameraBlockerLayer;
            blocker.gameObject.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// Authority follows the master client and moves to a new one when it leaves, so the object must not be
        /// destroyed with its authority (the NetworkObject default).
        /// </summary>
        static void MakeMasterClientObject(NetworkObject networkObject)
        {
            networkObject.Flags |= NetworkObjectFlags.MasterClientObject;
            networkObject.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        }

        static Transform Child(Transform parent, string name, Vector3 localPosition, Quaternion? localRotation = null)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            child.localRotation = localRotation ?? Quaternion.identity;
            return child;
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = Load<T>(path);
            if (asset != null)
                return asset;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void SetFields(Object target, params (string field, Object value)[] values)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in values)
                serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray(Object target, string field, IReadOnlyList<Object> values)
        {
            var serialized = new SerializedObject(target);
            var array = serialized.FindProperty(field);
            array.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetString(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Vector3 WithY(this Vector3 v, float y) => new(v.x, y, v.z);
    }
}
