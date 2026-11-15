using System;
using System.Collections.Generic;
using System.IO;

using BepInEx;

using UnityEngine;

namespace ModAPI
{
    /*
     * Texture mods for characters and anything else drawn with materials (outfits, hair, weapons, accessories).
     * Every .png in ModResources\Textures (any sub folder, so each mod can bring its own folder) replaces the game
     * texture of the same name, e.g. ModResources\Textures\MyOutfit\in0101_body.png replaces "in0101_body".
     * The textures of a model are swapped when the game loads it (see ResourceRedirectManager.AssetLoaded),
     * Reload() reads the folder again and updates what is on screen, so a texture can be edited while the game runs.
     * With no files in the folder nothing is looked at.
     */
    public class TextureOverrides
    {
        public static readonly string FolderName = "ModResources" + Path.DirectorySeparatorChar + "Textures";

        private static TextureOverrides _Instance;
        public static TextureOverrides Instance { get { return _Instance ?? (_Instance = new TextureOverrides()); } }

        //Texture name => file. When two mods have the same name, the folder later in alphabetical order wins.
        private Dictionary<string, string> _Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _Loaded = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        //Our textures => the game texture they replace, to find the name again and to put it back
        private readonly Dictionary<Texture, Texture> _Originals = new Dictionary<Texture, Texture>();

        public string Folder { get { return Path.Combine(Paths.GameRootPath, FolderName); } }
        public int Count { get { return _Files.Count; } }

        private TextureOverrides()
        {
            Scan();
        }

        public void Scan()
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(Folder))
            {
                var paths = new List<string>(Directory.GetFiles(Folder, "*.png", SearchOption.AllDirectories));
                paths.AddRange(Directory.GetFiles(Folder, "*.jpg", SearchOption.AllDirectories));
                paths.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (files.ContainsKey(name))
                    {
                        Debug.LogWarning("[TextureOverrides] " + name + " is in more than one texture mod, using " + path);
                    }
                    files[name] = path;
                }
            }
            _Files = files;
            Debug.Log("[TextureOverrides] " + _Files.Count + " texture overrides in " + FolderName);
        }

        //The game texture behind one of ours (or the texture itself)
        public Texture OriginalOf(Texture texture)
        {
            return texture != null && _Originals.TryGetValue(texture, out Texture original) ? original : texture;
        }

        public void ApplyTo(GameObject root)
        {
            if (root == null || (_Files.Count == 0 && _Originals.Count == 0))
            {
                return;
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                ApplyTo(renderer);
            }
        }

        public int ApplyTo(Renderer renderer)
        {
            int changed = 0;
            if (renderer == null)
            {
                return 0;
            }
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    continue;
                }
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture current = material.GetTexture(property);
                    if (current == null)
                    {
                        continue;
                    }
                    Texture original = OriginalOf(current);
                    Texture wanted = original;
                    if (_Files.TryGetValue(original.name, out string file))
                    {
                        wanted = Load(file, original, IsLinear(property)) ?? original;
                    }
                    if (wanted != current)
                    {
                        material.SetTexture(property, wanted);
                        changed++;
                    }
                }
            }
            return changed;
        }

        //Normal maps hold directions, not colours: they are read without the colour conversion
        private static bool IsLinear(string property)
        {
            return property.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || property.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Texture2D Load(string file, Texture original, bool linear)
        {
            if (_Loaded.TryGetValue(file, out Texture2D cached) && cached != null)
            {
                return cached;
            }
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, !(original is Texture2D texture2D) || texture2D.mipmapCount > 1, linear);
                if (!texture.LoadImage(File.ReadAllBytes(file)))
                {
                    Debug.LogError("[TextureOverrides] Could not read " + file);
                    return null;
                }
                texture.name = original.name;
                texture.wrapMode = original.wrapMode;
                texture.filterMode = original.filterMode;
                texture.anisoLevel = original.anisoLevel;
                if (texture.width != original.width || texture.height != original.height)
                {
                    Debug.LogWarning("[TextureOverrides] " + Path.GetFileName(file) + " is " + texture.width + "x" + texture.height + ", the game texture is " + original.width + "x" + original.height + " (it still works if the layout is the same)");
                }
                _Loaded[file] = texture;
                _Originals[texture] = original;
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogError("[TextureOverrides] Could not load " + file + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Reads the texture folder again and updates everything on screen: edited files are shown, removed ones
        /// give the game texture back. Returns how many textures were changed.
        /// </summary>
        public int Reload()
        {
            var old = new List<Texture2D>(_Loaded.Values);
            _Loaded.Clear();
            Scan();
            int changed = 0;
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                changed += ApplyTo(renderer);
            }
            //Our old textures are not used by anything now
            foreach (Texture2D texture in old)
            {
                if (texture != null && !_Loaded.ContainsValue(texture))
                {
                    _Originals.Remove(texture);
                    UnityEngine.Object.Destroy(texture);
                }
            }
            return changed;
        }
    }
}
