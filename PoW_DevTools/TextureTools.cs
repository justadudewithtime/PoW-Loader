using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PoWDevTools
{
    /*
     * Export: writes the textures of a character part as PNG files (TextureDump\<model>\), the starting point for a
     * texture mod. Reload: tells ModAPI to read ModResources\Textures again, so edited files show up at once.
     */
    internal static class TextureTools
    {
        private static object _overrides;
        private static Type _type;

        //ModAPI's TextureOverrides, null with a ModAPI from before texture mods
        private static object Overrides
        {
            get
            {
                if (_overrides != null) return _overrides;
                _type = Type.GetType("ModAPI.TextureOverrides, PoW_ModAPI");
                _overrides = _type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                return _overrides;
            }
        }

        public static string ModFolder { get { return Path.Combine(BepInEx.Paths.GameRootPath, Path.Combine("ModResources", "Textures")); } }

        public static string Reload()
        {
            object o = Overrides;
            if (o == null) return "Texture mods need the new PoW_ModAPI.dll.";
            try
            {
                Directory.CreateDirectory(ModFolder);
                int changed = (int)_type.GetMethod("Reload").Invoke(o, null);
                int count = (int)_type.GetProperty("Count").GetValue(o, null);
                return "Texture mods reloaded: " + count + " file(s) in ModResources\\Textures, " + changed + " texture(s) changed on screen.";
            }
            catch (Exception e)
            {
                Debug.LogError("[DevTools] Reload: " + e);
                return "Reload failed: " + (e.InnerException ?? e).Message;
            }
        }

        private static Texture GameTexture(Texture t)
        {
            t = Recolor.OriginalOf(t);
            object o = Overrides;
            if (o != null && t != null)
            {
                try { t = (Texture)_type.GetMethod("OriginalOf").Invoke(o, new object[] { t }) ?? t; } catch { }
            }
            return t;
        }

        public static string Export(Renderer[] renderers, string model)
        {
            string folder = Path.Combine(Path.Combine(BepInEx.Paths.GameRootPath, "TextureDump"), string.IsNullOrEmpty(model) ? "character" : model);
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(ModFolder);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shared = new List<string>();
            foreach (Renderer r in renderers)
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    foreach (string property in m.GetTexturePropertyNames())
                    {
                        Texture t = GameTexture(m.GetTexture(property));
                        if (t == null || string.IsNullOrEmpty(t.name) || !done.Add(t.name)) continue;
                        bool linear = property.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || property.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!Save(t, Path.Combine(folder, t.name + ".png"), linear)) continue;
                        int others = UsersOf(t.name) - 1;
                        if (others > 0) shared.Add(t.name + " (" + others + " other)");
                    }
                }
            }
            WriteReadme(folder);
            if (done.Count == 0) return "This part has no textures to export.";
            return "Exported " + done.Count + " texture(s) to TextureDump\\" + Path.GetFileName(folder) + ". Edit, save into ModResources\\Textures\\<your mod>\\ with the same file name, press Reload."
                + (shared.Count > 0 ? "\nShared with other models on screen: " + string.Join(", ", shared.ToArray()) : "");
        }

        //How many renderers on screen draw a texture of that name (shared textures change on every one of them)
        private static int UsersOf(string name)
        {
            int n = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    bool uses = false;
                    foreach (string p in m.GetTexturePropertyNames())
                    {
                        Texture t = GameTexture(m.GetTexture(p));
                        if (t != null && t.name == name) { uses = true; break; }
                    }
                    if (uses) { n++; break; }
                }
            }
            return n;
        }

        //Game textures can't be read directly: copied through the GPU, then written as PNG
        private static bool Save(Texture texture, string file, bool linear)
        {
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, linear);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                File.WriteAllBytes(file, copy.EncodeToPNG());
                UnityEngine.Object.Destroy(copy);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[DevTools] Export of " + texture.name + " failed: " + e.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void WriteReadme(string folder)
        {
            string readme = Path.Combine(Path.GetDirectoryName(folder), "README.txt");
            if (File.Exists(readme)) return;
            File.WriteAllText(readme,
                "Texture dumps from the Dev Tools wardrobe (F8 > Colours > Export textures).\r\n\r\n" +
                "To make a texture mod:\r\n" +
                "1. Edit a PNG from here in any image editor. Keep the size and the layout (the parts of the outfit stay where they are).\r\n" +
                "2. Save it with the SAME file name into ModResources\\Textures\\<your mod name>\\ (any sub folders are fine).\r\n" +
                "3. In the game press Reload texture mods in the wardrobe, or restart the game.\r\n\r\n" +
                "To share it, zip your ModResources\\Textures\\<your mod name> folder. Players need the Mod API with texture mods.\r\n" +
                "Files ending in _n, _normal or _bump are normal maps (surface bumps), not colours: edit them only if you know what they are.\r\n");
        }
    }
}
