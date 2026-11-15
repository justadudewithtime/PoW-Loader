using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Heluo.Features.Wearing;
using UnityEngine;

namespace DevToolsProbe
{
    internal enum ColorPart { Hair, Head, Body, Accessories }

    /*
     * Recolouring a character's parts on its own material copies (other characters with the same outfit keep theirs).
     * Tint: the material's colour property (whichever of the usual names the shader has) multiplies the texture.
     * Hue shift: a copy of the texture with hue / saturation / brightness changed, skin tones left alone if wanted.
     */
    internal static class Recolor
    {
        private static readonly string[] ColorProperties = { "_Color", "_MainColor", "_BaseColor", "_Tint", "_TintColor" };

        private static readonly Dictionary<Material, Color> _originalTint = new Dictionary<Material, Color>();
        private static readonly Dictionary<Material, Texture> _originalTexture = new Dictionary<Material, Texture>();
        private static readonly Dictionary<Material, Texture2D> _madeTexture = new Dictionary<Material, Texture2D>();

        //The texture behind one of our recoloured copies
        public static Texture OriginalOf(Texture t)
        {
            if (t == null) return null;
            foreach (var made in _madeTexture) if (made.Value == t && _originalTexture.TryGetValue(made.Key, out Texture o)) return o;
            return t;
        }

        public static Renderer[] RenderersOf(AvatarComponent avatar, ColorPart part)
        {
            if (avatar == null) return new Renderer[0];
            switch (part)
            {
                case ColorPart.Hair: return One(avatar[RendererType.hair]);
                case ColorPart.Head: return One(avatar[RendererType.head]);
                case ColorPart.Body: return One(avatar[RendererType.body]);
                default:
                    var list = new List<Renderer>();
                    foreach (AttachPointType p in Enum.GetValues(typeof(AttachPointType)))
                    {
                        Transform t = avatar[p];
                        if (t != null) list.AddRange(t.GetComponentsInChildren<Renderer>(true));
                    }
                    return list.ToArray();
            }
        }

        private static Renderer[] One(Renderer r) { return r != null ? new[] { r } : new Renderer[0]; }

        private static string ColorPropertyOf(Material m)
        {
            foreach (string p in ColorProperties) if (m != null && m.HasProperty(p)) return p;
            return null;
        }

        //Shader names and colour properties of a part, shown in the panel so odd shaders can be spotted
        public static string Describe(Renderer[] renderers)
        {
            var parts = new List<string>();
            foreach (Renderer r in renderers)
                foreach (Material m in r.sharedMaterials)
                    if (m != null) parts.Add(m.shader.name + " (" + (ColorPropertyOf(m) ?? "no colour") + (m.mainTexture != null ? ", texture" : "") + ")");
            return parts.Count == 0 ? "nothing to recolour" : string.Join(", ", parts.Distinct().ToArray());
        }

        //-------------------------------------------------------------- tint
        public static int Tint(Renderer[] renderers, Color tint)
        {
            int changed = 0;
            foreach (Renderer r in renderers)
            {
                foreach (Material m in r.materials)
                {
                    string p = ColorPropertyOf(m);
                    if (p == null) continue;
                    if (!_originalTint.ContainsKey(m)) _originalTint[m] = m.GetColor(p);
                    Color o = _originalTint[m];
                    m.SetColor(p, new Color(o.r * tint.r, o.g * tint.g, o.b * tint.b, o.a));
                    changed++;
                }
            }
            return changed;
        }

        //-------------------------------------------------------------- hue shift
        public static int Shift(Renderer[] renderers, HueShift shift)
        {
            int changed = 0;
            foreach (Renderer r in renderers)
            {
                foreach (Material m in r.materials)
                {
                    if (!_originalTexture.ContainsKey(m))
                    {
                        if (m.mainTexture == null) continue;
                        _originalTexture[m] = m.mainTexture;
                    }
                    Texture original = _originalTexture[m];
                    if (_madeTexture.TryGetValue(m, out Texture2D old) && old != null) UnityEngine.Object.Destroy(old);
                    _madeTexture.Remove(m);
                    if (shift.IsNeutral)
                    {
                        m.mainTexture = original;
                    }
                    else
                    {
                        Texture2D made = Shifted(original, shift);
                        if (made == null) continue;
                        _madeTexture[m] = made;
                        m.mainTexture = made;
                    }
                    changed++;
                }
            }
            return changed;
        }

        //Textures in the game's bundles can't be read directly: they are copied through the GPU first
        private static Texture2D Shifted(Texture source, HueShift shift)
        {
            int w = source.width, h = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                Color32[] px = tex.GetPixels32();
                float hueTurn = shift.Hue / 360f;
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    Color.RGBToHSV(c, out float hh, out float s, out float v);
                    //Skin: warm hue, little colour, not dark
                    if (shift.KeepSkin && (hh < 0.12f || hh > 0.95f) && s > 0.12f && s < 0.6f && v > 0.35f) continue;
                    //Greys and blacks have no hue to turn, only brightness
                    if (s > 0.08f) hh = (hh + hueTurn + 1f) % 1f;
                    s = Mathf.Clamp01(s * shift.Saturation);
                    v = Mathf.Clamp01(v * shift.Brightness);
                    Color n = Color.HSVToRGB(hh, s, v);
                    px[i] = new Color32((byte)(n.r * 255f), (byte)(n.g * 255f), (byte)(n.b * 255f), c.a);
                }
                tex.SetPixels32(px);
                tex.Apply(true);
                tex.name = source.name + "_recolored";
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogError("[DevToolsProbe] Recolour of " + source.name + " failed: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }

    internal struct HueShift
    {
        public float Hue, Saturation, Brightness;
        public bool KeepSkin;

        public static HueShift Neutral { get { return new HueShift { Hue = 0f, Saturation = 1f, Brightness = 1f, KeepSkin = true }; } }
        public bool IsNeutral { get { return Mathf.Abs(Hue) < 0.5f && Mathf.Abs(Saturation - 1f) < 0.01f && Mathf.Abs(Brightness - 1f) < 0.01f; } }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0.00},{2:0.00},{3}", Hue, Saturation, Brightness, KeepSkin ? 1 : 0);
        }

        public static HueShift Parse(string s)
        {
            HueShift h = Neutral;
            string[] p = (s ?? "").Split(',');
            if (p.Length >= 4)
            {
                float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out h.Hue);
                float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out h.Saturation);
                float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out h.Brightness);
                h.KeepSkin = p[3] == "1";
            }
            return h;
        }
    }
}
