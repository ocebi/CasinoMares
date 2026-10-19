using System.Collections.Generic;
using System.Linq;
using CasinoMares.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasinoMares.Editor
{
    /// <summary>
    /// Takes the on-demand art (<see cref="LazyContentSetup"/>'s Sky and Landscape groups) out of each scene as a player
    /// build processes it, so it ships only in its bundle: mesh filters showing an island lose their mesh, and materials
    /// sampling the sky photo are swapped for scene copies holding a blurred 64×32 stand-in. A
    /// <see cref="LazySceneContent"/> puts the real art back at runtime. Play mode and the scene assets are untouched.
    /// </summary>
    class LazySceneProcessor : IProcessSceneWithReport
    {
        /// <summary>Size of the stand-in a lazy texture shows until it loads (the sky: a soft gradient is enough).</summary>
        const int PlaceholderWidth = 64;
        const int PlaceholderHeight = 32;

        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // A null report is Play mode loading the scene.
            if (report == null)
                return;
            var addresses = LazyContentSetup.SceneAddresses();
            if (addresses.Count == 0)
                return;

            var roots = scene.GetRootGameObjects();
            var meshes = new List<LazySceneContent.MeshSlot>();
            var colliderMeshes = new HashSet<Mesh>(roots.SelectMany(root => root.GetComponentsInChildren<MeshCollider>(true))
                .Select(collider => collider.sharedMesh));
            foreach (var filter in roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)))
            {
                // Something walked on keeps its mesh: a collider needs it from the first frame.
                if (filter.sharedMesh == null || colliderMeshes.Contains(filter.sharedMesh) || !TryAddress(addresses, filter.sharedMesh, out var address))
                    continue;
                meshes.Add(new LazySceneContent.MeshSlot { Filter = filter, Address = address });
                filter.sharedMesh = null;
            }

            var textures = new List<LazySceneContent.TextureSlot>();
            var copies = new Dictionary<Material, Material>();
            var placeholders = new Dictionary<Texture, Texture2D>();
            Material Swap(Material material)
            {
                if (material == null)
                    return null;
                if (copies.TryGetValue(material, out var copy))
                    return copy;
                foreach (var property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    if (texture == null || !TryAddress(addresses, texture, out var address))
                        continue;
                    copy ??= new Material(material) { name = material.name };
                    if (!placeholders.TryGetValue(texture, out var placeholder))
                        placeholders[texture] = placeholder = Placeholder(texture);
                    copy.SetTexture(property, placeholder);
                    textures.Add(new LazySceneContent.TextureSlot { Material = copy, Property = property, Address = address });
                }

                copies[material] = copy ?? material;
                return copies[material];
            }

            RenderSettings.skybox = Swap(RenderSettings.skybox);
            foreach (var renderer in roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var swapped = Swap(materials[i]);
                    changed |= swapped != materials[i];
                    materials[i] = swapped;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }

            if (meshes.Count == 0 && textures.Count == 0)
                return;
            var holder = new GameObject("LazySceneContent");
            SceneManager.MoveGameObjectToScene(holder, scene);
            holder.AddComponent<LazySceneContent>().Configure(meshes.ToArray(), textures.ToArray());
            Debug.Log($"[LazyContent] {scene.name}: {meshes.Count} meshes and {textures.Count} texture slots load on demand.");
        }

        static bool TryAddress(Dictionary<string, string> addresses, Object asset, out string address)
        {
            address = null;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long _) && addresses.TryGetValue(guid, out address);
        }

        /// <summary>A small blurred copy of the texture, saved into the scene.</summary>
        static Texture2D Placeholder(Texture texture)
        {
            var target = RenderTexture.GetTemporary(PlaceholderWidth, PlaceholderHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var placeholder = new Texture2D(PlaceholderWidth, PlaceholderHeight, TextureFormat.RGBA32, false)
            {
                name = texture.name + " (stand-in)",
                wrapMode = texture.wrapMode,
            };
            placeholder.ReadPixels(new Rect(0, 0, PlaceholderWidth, PlaceholderHeight), 0, 0);
            placeholder.Apply(false, true);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return placeholder;
        }
    }
}
