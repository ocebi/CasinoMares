using System;
using UnityEngine;

namespace CasinoMares.Core
{
    /// <summary>
    /// Fills in the scene's on-demand art (<see cref="LazyContent"/>) once the scene has opened: the far islands' meshes
    /// (nothing shows there until they arrive) and the sky photo in the sky, water and land materials (a blurred copy
    /// stands in until then). Added and filled in by the editor's <c>LazySceneProcessor</c> while a player build
    /// processes the scene; the scene assets themselves keep their direct references.
    /// </summary>
    public class LazySceneContent : MonoBehaviour
    {
        [Serializable]
        public struct MeshSlot
        {
            public MeshFilter Filter;
            public string Address;
        }

        [Serializable]
        public struct TextureSlot
        {
            public Material Material;
            public string Property;
            public string Address;
        }

        [SerializeField] MeshSlot[] _meshes = Array.Empty<MeshSlot>();
        [SerializeField] TextureSlot[] _textures = Array.Empty<TextureSlot>();

        public void Configure(MeshSlot[] meshes, TextureSlot[] textures)
        {
            _meshes = meshes;
            _textures = textures;
        }

        void Start()
        {
            foreach (var slot in _textures)
            {
                var (material, property) = (slot.Material, slot.Property);
                LazyContent.Load<Texture2D>(slot.Address, texture =>
                {
                    if (material != null)
                        material.SetTexture(property, texture);
                });
            }

            foreach (var slot in _meshes)
            {
                var filter = slot.Filter;
                LazyContent.Load<Mesh>(slot.Address, mesh =>
                {
                    if (filter != null)
                        filter.sharedMesh = mesh;
                });
            }
        }
    }
}
