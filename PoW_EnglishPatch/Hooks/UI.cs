using System.Reflection;
using System.Text;

using HarmonyLib;

using Heluo.Data;
using Heluo.Manager;
using Heluo.Platform;
using Heluo.UI;

using UnityEngine;
using UnityEngine.UI;

namespace EnglishPatch.Hooks
{
    internal static class TextUtil
    {
        private static Font _Font;

        //Font for the English texts, chosen in the config file (see EnglishPatch.FontName). Looked up once, it is the same object every time.
        //A font that is not installed on this PC falls back to the Arial built into Unity.
        public static Font UIFont
        {
            get
            {
                if (_Font == null)
                {
                    _Font = LoadFont(EnglishPatch.FontName);
                }
                return _Font;
            }
        }

        private static Font _CombatFont;
        private static bool _CombatFontLoaded;

        //Font for the combat texts in the game's own font, chosen in the config file (see EnglishPatch.CombatFontName).
        //null when the setting is empty, those texts then keep the game's font.
        public static Font CombatFont
        {
            get
            {
                if (!_CombatFontLoaded)
                {
                    _CombatFontLoaded = true;
                    _CombatFont = string.IsNullOrEmpty(EnglishPatch.CombatFontName?.Trim()) ? null : LoadFont(EnglishPatch.CombatFontName);
                }
                return _CombatFont;
            }
        }

        private static Font _DialogueFont;
        private static bool _DialogueFontLoaded;

        //Font for the dialogue text in the talk box, chosen in the config file (see EnglishPatch.DialogueFontName).
        //The English font when the setting is empty.
        public static Font DialogueFont
        {
            get
            {
                if (!_DialogueFontLoaded)
                {
                    _DialogueFontLoaded = true;
                    _DialogueFont = string.IsNullOrEmpty(EnglishPatch.DialogueFontName?.Trim()) ? null : LoadFont(EnglishPatch.DialogueFontName);
                }
                return _DialogueFont ?? UIFont;
            }
        }

        //Dialogue texts of the talk box, registered by WGMessage_UpdateWidget_Hook
        public static readonly System.Collections.Generic.HashSet<WGText> DialogueTexts = new System.Collections.Generic.HashSet<WGText>();

        //Name Unity gives the game's own font (Kaiu, the Windows font DFKai-SB)
        private const string GameFont = "Kaiu";

        //Texts under root that are still written in the game's own font get the given font, the other fonts are left alone
        public static void ReplaceGameFont(Component root, Font font)
        {
            if (root == null || font == null)
            {
                return;
            }

            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.font != null && text.font.name == GameFont)
                {
                    text.font = font;
                }
            }
        }

        //The 新 ("new") badges under root, a text over a red ring, get the English "New" from StringTable (General_New)
        public static void TranslateNewBadges(Component root)
        {
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.text == null || text.text.Trim() != "新")
                {
                    continue;
                }

                text.text = Heluo.Game.Data.Get<StringTable>("General_New")?.Text ?? "New";
                text.font = UIFont;
                //The box is sized for one Chinese character. Wrapping or truncating would hide the word, so it may overflow
                //and we shrink the font ourselves until the word is as wide as the box
                text.resizeTextForBestFit = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                float width = text.rectTransform.rect.width;
                if (width > 0 && text.preferredWidth > width)
                {
                    text.fontSize = Mathf.Max(8, Mathf.FloorToInt(text.fontSize * width / text.preferredWidth));
                }
            }
        }

        private static Font LoadFont(string name)
        {
            Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (string.IsNullOrEmpty(name) || name.Trim().Equals("Arial", System.StringComparison.OrdinalIgnoreCase))
            {
                return arial;
            }

            name = name.Trim();
            string simplifiedName = Simplified(name);
            string[] installedFonts = Font.GetOSInstalledFontNames();

            //The exact name first, then the same name written with other spaces or capitals ("EdoSZ" for "Edo SZ")
            string match = null;
            foreach (string installed in installedFonts)
            {
                if (installed.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    match = installed;
                    break;
                }
            }
            if (match == null)
            {
                foreach (string installed in installedFonts)
                {
                    if (Simplified(installed) == simplifiedName)
                    {
                        match = installed;
                        break;
                    }
                }
            }

            if (match != null)
            {
                //Arial behind it for any character the chosen font does not have
                Font font = Font.CreateDynamicFontFromOSFont(new[] { match, "Arial" }, 20);
                if (font != null)
                {
                    Debug.Log("[EnglishPatch] Using the font " + match);
                    return font;
                }
            }

            //Helping to find the right name: the installed fonts that share the start of the name
            string start = simplifiedName.Substring(0, Mathf.Min(3, simplifiedName.Length));
            var similar = new System.Collections.Generic.List<string>();
            foreach (string installed in installedFonts)
            {
                if (start.Length > 0 && Simplified(installed).Contains(start))
                {
                    similar.Add(installed);
                }
            }
            if (similar.Count > 0)
            {
                Debug.LogWarning("[EnglishPatch] Installed fonts with a similar name: " + string.Join(", ", similar.ToArray()));
            }
            Debug.LogWarning("[EnglishPatch] The font '" + name + "' is not installed on this PC (or only for one Windows user), using Arial instead");
            return arial;
        }

        //Lower case, without spaces, dashes and underscores
        private static string Simplified(string fontName)
        {
            return fontName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
        }

        private static readonly AccessTools.FieldRef<WGText, Text> _WGTextText = AccessTools.FieldRefAccess<WGText, Text>("text");

        //The Text a WGText shows
        public static Text TextOf(WGText wgText)
        {
            return wgText == null ? null : _WGTextText(wgText);
        }

        /*
         * Drops what is kept for widgets that no longer exist (a battle screen is made again for every battle), checked
         * only once the dictionary has grown, so it costs nothing on most calls.
         */
        public static void Prune<TKey, TValue>(System.Collections.Generic.Dictionary<TKey, TValue> kept) where TKey : UnityEngine.Object
        {
            if (kept.Count < 64)
            {
                return;
            }
            var gone = new System.Collections.Generic.List<TKey>();
            foreach (TKey key in kept.Keys)
            {
                if (key == null)
                {
                    gone.Add(key);
                }
            }
            foreach (TKey key in gone)
            {
                kept.Remove(key);
            }
        }

        /*
         * Settings to measure a text as it is drawn, but keeping every line: a text set to cut lines that do not fit gives
         * nothing for a line a little taller than its area (Arial 20 in an area 20 high), so it could not be measured.
         */
        public static TextGenerationSettings Measuring(Text text, Vector2 size)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(size);
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            return settings;
        }

        private static readonly AccessTools.FieldRef<WGStringTable, string> _StringTableId = AccessTools.FieldRefAccess<WGStringTable, string>("id");

        //The StringTable id a label of the prefab shows (SecondaryInterface0107...)
        public static string StringId(WGStringTable label)
        {
            return label == null ? null : _StringTableId(label);
        }

        //Horizontal world position of the drawn text (not of its rect), using its one line width and alignment
        public static void VisibleX(Text text, out float left, out float right)
        {
            Rect rect = text.rectTransform.rect;
            float width = text.preferredWidth;
            float xMin;
            switch ((int)text.alignment % 3)
            {
                case 0: xMin = rect.xMin; break;
                case 1: xMin = rect.center.x - width / 2f; break;
                default: xMin = rect.xMax - width; break;
            }
            left = text.rectTransform.TransformPoint(new Vector3(xMin, 0f, 0f)).x;
            right = text.rectTransform.TransformPoint(new Vector3(xMin + width, 0f, 0f)).x;
        }

        //Font size each text had before ShrinkToWidth changed it, texts are reused for different strings
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalWidthFitSize = new System.Collections.Generic.Dictionary<Text, int>();

        //Keeps a text on one line and shrinks it (never below minSize) until it is not wider than its rect
        public static void ShrinkToWidth(Text text, int minSize, float margin = 0f)
        {
            if (text == null)
            {
                return;
            }

            if (!_OriginalWidthFitSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalWidthFitSize[text] = originalSize;
            }
            text.fontSize = originalSize;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            float width = text.rectTransform.rect.width - margin;
            while (text.preferredWidth > width && text.fontSize > minSize)
            {
                text.fontSize--;
            }
        }

        //Font size each text had before ShrinkToBox changed it, texts are reused for different strings
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalBoxFitSize = new System.Collections.Generic.Dictionary<Text, int>();

        //Wraps a text at spaces and shrinks it (never below minSize) until it fits its rect without breaking a word.
        //Unlike best fit it never hides the text, at the minimum size it may overflow a little.
        //verticalPadding is room kept free above and below the text.
        public static void ShrinkToBox(Text text, int minSize, float verticalPadding = 0f)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            if (!_OriginalBoxFitSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalBoxFitSize[text] = originalSize;
            }
            text.fontSize = originalSize;
            text.resizeTextForBestFit = false;
            text.text = text.text.Replace('\u00A0', ' ');
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            Rect rect = text.rectTransform.rect;
            string[] words = text.text.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            while (text.fontSize > minSize && (text.preferredHeight > rect.height - 2f * verticalPadding || WidestWord(text, words) > rect.width))
            {
                text.fontSize--;
            }
        }

        //Wraps a text at spaces and shrinks it (never below minSize) until it takes at most maxLines lines and no word is wider than its rect.
        //Shares the original sizes with ShrinkToBox, so RestoreBoxFit gives it back too.
        public static void ShrinkToLines(Text text, int maxLines, int minSize)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            if (!_OriginalBoxFitSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalBoxFitSize[text] = originalSize;
            }
            text.fontSize = originalSize;
            text.resizeTextForBestFit = false;
            text.text = text.text.Replace('\u00A0', ' ');
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            Rect rect = text.rectTransform.rect;
            string[] words = text.text.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            while (text.fontSize > minSize && (LineCount(text) > maxLines || WidestWord(text, words) > rect.width))
            {
                text.fontSize--;
            }
        }

        //Only used to count lines, one is enough for all texts. Made on first use, from the game's own thread.
        private static TextGenerator _LineCounter;

        private static int LineCount(Text text)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(text.rectTransform.rect.width, 100000f));
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            if (_LineCounter == null)
            {
                _LineCounter = new TextGenerator();
            }
            _LineCounter.Populate(text.text, settings);
            return _LineCounter.lineCount;
        }

        //The text broken at the space that makes its longest line the shortest
        public static string TwoLines(Text text, string content)
        {
            string best = content;
            float bestWidth = float.MaxValue;
            for (int i = content.IndexOf(' '); i >= 0; i = content.IndexOf(' ', i + 1))
            {
                string split = content.Substring(0, i) + "\n" + content.Substring(i + 1);
                float width = LinesWidth(text, split);
                if (width < bestWidth)
                {
                    best = split;
                    bestWidth = width;
                }
            }
            return best;
        }

        //Width of the widest line of a text (lines split at \n, not wrapped) at the text's current size
        public static float LinesWidth(Text text, string content)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            float widest = 0f;
            foreach (string line in content.Split('\n'))
            {
                widest = Mathf.Max(widest, text.cachedTextGeneratorForLayout.GetPreferredWidth(line, settings) / text.pixelsPerUnit);
            }
            return widest;
        }

        //Gives a text shrunk by ShrinkToBox its original size back, for reused texts that should not be fitted anymore
        public static void RestoreBoxFit(Text text)
        {
            if (text != null && _OriginalBoxFitSize.TryGetValue(text, out int originalSize))
            {
                text.fontSize = originalSize;
            }
        }

        private static float WidestWord(Text text, string[] words)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            float widest = 0f;
            foreach (string word in words)
            {
                widest = Mathf.Max(widest, text.cachedTextGeneratorForLayout.GetPreferredWidth(word, settings) / text.pixelsPerUnit);
            }
            return widest;
        }

        /*
         * Unity's Text has no justified alignment, so we do it ourselves: we let Unity break the lines at the rect width,
         * then widen the spaces of every line until it reaches the right edge. Extra space finer than one space is a space
         * with a smaller <size>, smaller sizes do not make the line taller. The last line of a paragraph stays left aligned.
         * The lines are joined with line breaks and the text no longer wraps by itself, so Unity keeps our lines as they are.
         * Color tags are kept, texts with other rich text tags (they change the width) or not aligned to the left are left alone.
         */
        public static string Justified(Text text, string content)
        {
            if (text == null || text.font == null || string.IsNullOrEmpty(content) || (int)text.alignment % 3 != 0)
            {
                return content;
            }

            //Color tags do not change the width of the text, so the lines are worked out without them and they are put back
            //where they were. Any other tag (size, bold...) changes the width, those texts are left alone.
            var tags = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, string>>();
            var plain = new StringBuilder();
            int from = 0;
            foreach (System.Text.RegularExpressions.Match tag in _ColorTag.Matches(content))
            {
                plain.Append(content, from, tag.Index - from);
                tags.Add(new System.Collections.Generic.KeyValuePair<int, string>(plain.Length, tag.Value));
                from = tag.Index + tag.Length;
            }
            plain.Append(content, from, content.Length - from);
            if (plain.ToString().IndexOf('<') >= 0)
            {
                return content;
            }
            string original = content;
            content = plain.ToString();

            float width = text.rectTransform.rect.width;
            if (width <= 0f)
            {
                return original;
            }

            content = content.Replace('\u00A0', ' ');
            TextGenerationSettings wrapped = text.GetGenerationSettings(new Vector2(width, 100000f));
            wrapped.horizontalOverflow = HorizontalWrapMode.Wrap;
            wrapped.verticalOverflow = VerticalWrapMode.Overflow;
            wrapped.resizeTextForBestFit = false;
            wrapped.richText = true;
            TextGenerationSettings oneLine = wrapped;
            oneLine.horizontalOverflow = HorizontalWrapMode.Overflow;

            TextGenerator generator = JustifyGenerator;
            generator.Populate(content, wrapped);
            //A copy, measuring the lines below fills the generator again
            UILineInfo[] lines = generator.GetLinesArray();

            float scale = text.pixelsPerUnit;
            float[] smallSpace = SpaceWidths(text, generator, oneLine);
            float spaceWidth = smallSpace[0];
            if (spaceWidth <= 0f)
            {
                return original;
            }

            var result = new StringBuilder();
            int nextTag = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int start = lines[i].startCharIdx;
                int end = i + 1 < lines.Length ? lines[i + 1].startCharIdx : content.Length;
                if (start >= content.Length)
                {
                    break;
                }
                end = Mathf.Min(Mathf.Max(start, end), content.Length);
                bool paragraphEnd = i + 1 >= lines.Length || end == start || content[end - 1] == '\n';

                //The line without the spaces and line break around it
                while (start < end && content[start] == ' ')
                {
                    start++;
                }
                while (end > start && (content[end - 1] == ' ' || content[end - 1] == '\n' || content[end - 1] == '\r'))
                {
                    end--;
                }
                string line = content.Substring(start, end - start);

                if (i > 0)
                {
                    result.Append('\n');
                }

                int gaps = 0;
                foreach (char c in line)
                {
                    if (c == ' ')
                    {
                        gaps++;
                    }
                }

                float extra = width - generator.GetPreferredWidth(line, oneLine) / scale - 0.5f;
                bool justify = !paragraphEnd && gaps > 0 && extra > 0f;

                //Spreading the extra width over the spaces, keeping track of the rounding so the line still ends at the edge
                float perGap = justify ? extra / gaps : 0f;
                float added = 0f;
                int gap = 0;
                for (int c = start; c < end; c++)
                {
                    //The color tags that were in front of this character
                    while (nextTag < tags.Count && tags[nextTag].Key <= c)
                    {
                        result.Append(tags[nextTag].Value);
                        nextTag++;
                    }

                    result.Append(content[c]);
                    if (!justify || content[c] != ' ')
                    {
                        continue;
                    }

                    gap++;
                    float missing = perGap * gap - added;
                    int spaces = Mathf.FloorToInt(missing / spaceWidth);
                    result.Append(' ', spaces);
                    added += spaces * spaceWidth;

                    //The rest is one smaller space, the size whose width comes closest
                    float rest = missing - spaces * spaceWidth;
                    int best = 0;
                    for (int size = 1; size < smallSpace.Length; size++)
                    {
                        if (Mathf.Abs(smallSpace[size] - rest) < Mathf.Abs(smallSpace[best] - rest))
                        {
                            best = size;
                        }
                    }
                    if (best > 0)
                    {
                        result.Append("<size=").Append(best).Append("> </size>");
                        added += smallSpace[best];
                    }
                }
            }
            for (; nextTag < tags.Count; nextTag++)
            {
                result.Append(tags[nextTag].Value);
            }

            //Nothing to justify (a single line, or only paragraph ends), the text stays as it was
            return lines.Length < 2 ? original : result.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex _ColorTag = new System.Text.RegularExpressions.Regex("<color=[^>]*>|</color>");

        //One generator for every justifying, made when first used
        private static TextGenerator _JustifyGenerator;
        private static TextGenerator JustifyGenerator => _JustifyGenerator ?? (_JustifyGenerator = new TextGenerator());

        /*
         * Width of a space at the text's size ([0]) and at every smaller size ([size], glyph widths are rounded to whole
         * pixels so they are measured, not assumed). The same for every text of that font, size, style and scale, so it is
         * measured once (the dialogue is justified on every line shown, this was about 20 measures each time).
         */
        private static readonly System.Collections.Generic.Dictionary<string, float[]> _SpaceWidths = new System.Collections.Generic.Dictionary<string, float[]>();

        private static float[] SpaceWidths(Text text, TextGenerator generator, TextGenerationSettings oneLine)
        {
            string key = text.font.GetInstanceID() + "/" + text.fontSize + "/" + (int)text.fontStyle + "/" + text.pixelsPerUnit;
            if (_SpaceWidths.TryGetValue(key, out float[] widths))
            {
                return widths;
            }
            float scale = text.pixelsPerUnit;
            float pair = generator.GetPreferredWidth("xx", oneLine);
            widths = new float[Mathf.Max(1, text.fontSize)];
            widths[0] = (generator.GetPreferredWidth("x x", oneLine) - pair) / scale;
            for (int size = 1; size < widths.Length; size++)
            {
                widths[size] = (generator.GetPreferredWidth("x<size=" + size + "> </size>x", oneLine) - pair) / scale;
            }
            _SpaceWidths[key] = widths;
            return widths;
        }

        //What each text was justified into and at which width: the same text set again is not worked out again
        private static readonly System.Collections.Generic.Dictionary<Text, JustifiedText> _Justified = new System.Collections.Generic.Dictionary<Text, JustifiedText>();

        private struct JustifiedText
        {
            public string Result;
            public float Width;
            public int Size;
        }

        //Justifies a text in place, see Justified
        public static void Justify(Text text)
        {
            if (text == null || text.font == null)
            {
                return;
            }

            float width = text.rectTransform.rect.width;
            if (_Justified.TryGetValue(text, out JustifiedText done) && done.Result == text.text && done.Width == width && done.Size == text.fontSize)
            {
                //Already our lines: they must not be wrapped again (a hook may have set the text to wrap meanwhile)
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                return;
            }

            string justified;
            try
            {
                justified = Justified(text, text.text);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[EnglishPatch] Could not justify '" + text.text + "': " + e);
                return;
            }
            if (justified != text.text)
            {
                text.supportRichText = true;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.text = justified;
                Prune(_Justified);
                _Justified[text] = new JustifiedText { Result = justified, Width = width, Size = text.fontSize };
            }
        }

        //Shrinks a wrapping text until it is not taller than its rect, never below minSize. It is not truncated, so a very long text may still overflow a little.
        public static void ShrinkToHeight(Text text, int minSize)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            text.text = text.text.Replace(' ', ' ');
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            float height = text.rectTransform.rect.height;
            while (text.preferredHeight > height && text.fontSize > minSize)
            {
                text.fontSize--;
            }
        }

        //The screen a widget belongs to: the closest UIForm above it, also when they are hidden
        public static UIForm OwningForm(Component component)
        {
            for (Transform t = component.transform; t != null; t = t.parent)
            {
                UIForm form = t.GetComponent<UIForm>();
                if (form != null)
                {
                    return form;
                }
            }
            return null;
        }

        //A width in the local units of a rect, converted to world units
        public static float WorldWidth(RectTransform rect, float localWidth)
        {
            return rect.TransformVector(new Vector3(localWidth, 0f, 0f)).x;
        }

        private static readonly System.Text.RegularExpressions.Regex _SizeTag = new System.Text.RegularExpressions.Regex(@"<size=(\d+)>");

        //Scales the <size=N> rich text tags of a text, so they shrink together with the font size
        public static string ScaleSizeTags(string text, float scale)
        {
            return _SizeTag.Replace(text, m => "<size=" + Mathf.Max(1, Mathf.RoundToInt(int.Parse(m.Groups[1].Value) * scale)) + ">");
        }

        //Shrinks a text to fit its rect and lets it wrap at spaces
        public static void FitToBox(Text text, int minSize)
        {
            text.text = text.text.Replace(' ', ' ');
            if (!text.resizeTextForBestFit)
            {
                text.resizeTextMaxSize = text.fontSize;
                text.resizeTextMinSize = minSize;
                text.resizeTextForBestFit = true;
            }
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        //Height of one line of text for its font size, Arial and most English fonts are about this tall
        private const float LineHeight = 1.25f;

        /*
         * Short texts stay on one line, longer ones wrap and may grow downwards.
         * An area less than one line high (sized for the game's font) set to cut lines that do not fit showed nothing at
         * all with Arial ("Turn", the skill key numbers), so such a text may go past its area.
         */
        public static void FitWrapping(Text text)
        {
            if (text.verticalOverflow == VerticalWrapMode.Truncate && text.rectTransform.rect.height < text.fontSize * LineHeight)
            {
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
            if (text.text.Length <= 20)
            {
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            else
            {
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }
    }

    [HarmonyPatch]
    public class WGText_setText_Hook
    {
        public static MethodBase TargetMethod()
        {
            return typeof(WGText).GetProperty("Text").GetSetMethod();
        }

        public static void Postfix(ref WGText __instance, ref Text ___text)
        {
            bool dialogue = TextUtil.DialogueTexts.Contains(__instance);
            ___text.font = dialogue ? TextUtil.DialogueFont : TextUtil.UIFont;
            ___text.fontSize = dialogue && EnglishPatch.DialogueFontSize > 0 ? EnglishPatch.DialogueFontSize : 20;
            TextUtil.FitWrapping(___text);
            //The dialogue lines are justified, both margins straight
            if (dialogue)
            {
                TextUtil.Justify(___text);
            }
        }
    }

    /*
     * The dialogue text of the talk box can have its own font and size (DialogueFont and DialogueFontSize in the config file).
     * The message is registered the first time it is shown, from then on WGText_setText_Hook gives it that font. The first
     * line was already set before it was registered, so it is styled here once more.
     */
    [HarmonyPatch(typeof(WGMessage), "UpdateWidget")]
    public class WGMessage_UpdateWidget_Hook
    {
        private static void Widen(RectTransform rect)
        {
            if (_Widened.TryGetValue(rect, out Vector2 size) && rect.sizeDelta == size)
            {
                return;
            }
            TextUtil.Prune(_Widened);
            /*
             * Left edge WiderLeft further left, right edge NarrowerRight further left. The game sets the area's position
             * itself, so moving it did not last: the area is made wider and its pivot (the point the position is for)
             * moved, so at the position the game gives, the edges land where wanted.
             */
            float wider = WiderLeft - NarrowerRight;
            float width = rect.rect.width;
            if (width + wider <= 0f)
            {
                return;
            }
            float pivot = (rect.pivot.x * width + WiderLeft) / (width + wider);
            rect.sizeDelta += new Vector2(wider, 0f);
            rect.pivot = new Vector2(pivot, rect.pivot.y);
            _Widened[rect] = rect.sizeDelta;
        }

        /*
         * The text started far right of the left end of the talk box, with a wide empty strip before it. The text area's
         * left edge moves WiderLeft to the left, so the lines start further left, and its right edge NarrowerRight.
         * Done relative to the area as it is, once: an area remembered while the box was still being set up made the
         * text area huge (lines no longer wrapped and ran off the screen). The size set here is kept, so a message that
         * finds that size again is not widened twice, and nothing adds up.
         */
        //From the game's text area (left edge about 613, right edge about 1311 at 1920x1080): left to the portrait,
        //right to just before the mountains drawn at the end of the box (a negative value moves the right edge right)
        private const float WiderLeft = 38f;
        private const float NarrowerRight = -26f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Widened = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        public static void Postfix(WGText ___message)
        {
            Text message = TextUtil.TextOf(___message);
            if (message != null)
            {
                Widen(message.rectTransform);
            }

            if (___message == null || !TextUtil.DialogueTexts.Add(___message))
            {
                return;
            }

            Text text = TextUtil.TextOf(___message);
            if (text != null)
            {
                text.font = TextUtil.DialogueFont;
                text.fontSize = EnglishPatch.DialogueFontSize > 0 ? EnglishPatch.DialogueFontSize : 20;
                TextUtil.FitWrapping(text);
                TextUtil.Justify(text);
            }
        }
    }

    /*
     * Quest results: rewards were "{item}x{count}" with nothing between them ("Lithospermum Angelica Oilx2"). They are written
     * "{item} 2 pcs" and "{item} 1 pc" instead. Money ("300 Money") has no count and stays as it is.
     */
    [HarmonyPatch(typeof(WGEvaluationReward), "UpdateWidget")]
    public class WGEvaluationReward_UpdateWidget_Hook
    {
        private static readonly System.Text.RegularExpressions.Regex _Count = new System.Text.RegularExpressions.Regex(@"\s*[xX×](\d+)$");

        public static void Postfix(WGText ___content, WGImage ___icon)
        {
            Text text = TextUtil.TextOf(___content);
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            text.text = _Count.Replace(text.text, m => " " + m.Groups[1].Value + (m.Groups[1].Value == "1" ? " pc" : " pcs"));

            //Long item names ran out of the reward box to the left, under the item icon
            TextUtil.ShrinkToWidth(text, MinSize);
            PlaceReward(text, ___icon);
        }

        /*
         * Rewards were centered while the criteria above them are left aligned. They are left aligned now and start where the
         * criteria texts start, never left of the item icon (with a small gap). A reward wider than the room from there to the
         * right edge of its box shrinks to fit. The rows are reused for every reward, so the position is always taken from the
         * one the game gave the text.
         */
        private static void PlaceReward(Text text, WGImage icon)
        {
            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
            text.alignment = TextAnchor.MiddleLeft;

            float boxLeft = rect.TransformPoint(new Vector3(rect.rect.xMin, 0f, 0f)).x;
            float boxRight = rect.TransformPoint(new Vector3(rect.rect.xMax, 0f, 0f)).x;

            //Where the criteria texts start, or the left of the box
            float start = boxLeft;
            Text criteria = CriteriaText(text);
            if (criteria != null)
            {
                TextUtil.VisibleX(criteria, out start, out _);
            }

            RectTransform iconRect = icon == null ? null : icon.transform as RectTransform;
            if (iconRect != null)
            {
                Vector3[] corners = new Vector3[4];
                iconRect.GetWorldCorners(corners);
                start = Mathf.Max(start, corners[2].x + rect.TransformVector(new Vector3(IconGap, 0f, 0f)).x);
            }

            float room = boxRight - start;
            while (rect.TransformVector(new Vector3(text.preferredWidth, 0f, 0f)).x > room && text.fontSize > MinSize)
            {
                text.fontSize--;
            }

            rect.anchoredPosition = origin + new Vector2(rect.InverseTransformVector(new Vector3(start - boxLeft, 0f, 0f)).x, 0f);
        }

        //Text of the first criteria line of the result screen the reward belongs to
        private static Text CriteriaText(Text reward)
        {
            UIEvaluation form = reward.GetComponentInParent<UIEvaluation>();
            WGEvaluationPoint[] points = form == null ? null : _Points(form);
            if (points == null)
            {
                return null;
            }

            foreach (WGEvaluationPoint point in points)
            {
                Text text = point == null ? null : TextUtil.TextOf(_Description(point));
                if (text != null)
                {
                    return text;
                }
            }
            return null;
        }

        private const int MinSize = 14;
        //Room left between the item icon and the text
        private const float IconGap = 8f;
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly AccessTools.FieldRef<UIEvaluation, WGEvaluationPoint[]> _Points = AccessTools.FieldRefAccess<UIEvaluation, WGEvaluationPoint[]>("evaluationPoint");
        private static readonly AccessTools.FieldRef<WGEvaluationPoint, WGText> _Description = AccessTools.FieldRefAccess<WGEvaluationPoint, WGText>("description");
    }

    /*
     * Some texts are written straight into a screen's prefab in Chinese, not taken from the text files, so no translation
     * reaches them. They are replaced when the screen is shown, and get the English font.
     */
    [HarmonyPatch(typeof(UIEvaluation), "Show")]
    public class UIEvaluation_Show_FixedTexts_Hook
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> _Texts = new System.Collections.Generic.Dictionary<string, string>
        {
            //Bottom of the quest result screen, same wording as SecondaryInterface2903
            { "按下任意鍵繼續遊戲", "Press Any Key to Continue" },
            { "按下任意键继续游戏", "Press Any Key to Continue" },
        };

        public static void Postfix(UIEvaluation __instance)
        {
            foreach (Text text in __instance.GetComponentsInChildren<Text>(true))
            {
                if (text.text != null && _Texts.TryGetValue(text.text.Trim(), out string english))
                {
                    text.text = english;
                    text.font = TextUtil.UIFont;
                    //The box is as wide as the Chinese text, the English one wrapped and its second line was cut off
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }
        }
    }

    [HarmonyPatch(typeof(UICommission), "Show")]
    public class UICommission_Show_Hook
    {
        public static void Postfix(ref UICommission __instance)
        {
            //Adjusting some elements to make the UI less scuffed (bc English texts are longer)
            RectTransform dynamicCanvas = (RectTransform)__instance.transform.Find("DynamicCanvas");
            RectTransform proclamation = (RectTransform)dynamicCanvas.transform.Find("Proclamation");
            Transform limit = (Transform)proclamation.transform.Find("Limit");

            //Updating the Credits Button Text
            RectTransform limit_Icon = (RectTransform)limit.Find("Limit_Icon");
            RectTransform limit_Title = (RectTransform)limit.Find("Limit_Title");
            RectTransform limit_Content = (RectTransform)limit.Find("Limit_Content");

            limit_Icon.localPosition = new Vector3(-295f, -384f, 0);
            limit_Title.localPosition = new Vector3(-170f, -384f, 0);
            limit_Content.localPosition = new Vector3(-11.37f, -385.82f, 0);
        }
    }

    [HarmonyPatch(typeof(WGStringTable), "UpdateString")]
    public class WGStringTable_UpdateString_Hook
    {
        //Labels whose English text is wider than the box behind them, shrunk to the width of their box.
        //With each label id the room to keep free at both ends together, for labels on rounded backgrounds.
        private static readonly System.Collections.Generic.Dictionary<string, float> _FitWidth = new System.Collections.Generic.Dictionary<string, float>
        {
            //Relationship screen: Friendship bar label, Send Gifts and Outings buttons
            { "General_Favorability", 0f }, { "SecondaryInterface0808", 0f }, { "SecondaryInterface0803", 0f }, { "SecondaryInterface0804", 0f },
            //Relationship screen: the small "Ability" tag in front of every level
            { "SecondaryInterface0809", 10f },
            //Refine Medicine: "Amount to Refine", kept on one line
            { "Refining_Count", 0f },
        };
        private const int MinTitleSize = 14;

        //Labels of _FitWidth whose translation has a size tag ("<size=26>Amount to Refine</size>"), it is removed so they can shrink
        private static readonly System.Collections.Generic.HashSet<string> _DropSizeTags = new System.Collections.Generic.HashSet<string>
        {
            "Refining_Count",
        };
        private static readonly System.Text.RegularExpressions.Regex _SizeTags = new System.Text.RegularExpressions.Regex(@"</?size(=\d+)?>");

        //Labels a little smaller than the size the game gives them, and by how much
        private static readonly System.Collections.Generic.Dictionary<string, int> _Smaller = new System.Collections.Generic.Dictionary<string, int>
        {
            //"Select Gear" on the Gear tab
            { "Equip_Select", 2 },
        };

        //"Medicine" in the tab bar of the character screen (Status, Skills, Gear, Medicine, Items) ran into the slanted edge
        //of the Items tab. It is a little smaller and moves a little closer to its icon (measured at 1920x1080).
        private const string MedicineId = "General_Medicine";
        private const int MedicineSmaller = 2;
        private const float MedicineShift = -4f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _MedicineOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        private static void FitMedicineTab(Text text, string id)
        {
            if (id != MedicineId || !(TextUtil.OwningForm(text) is UIFormTab))
            {
                return;
            }

            if (!_OriginalSize.TryGetValue(text, out int originalSize))
            {
                originalSize = text.fontSize;
                _OriginalSize[text] = originalSize;
            }
            text.fontSize = originalSize - MedicineSmaller;

            RectTransform rect = text.rectTransform;
            if (!_MedicineOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _MedicineOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(MedicineShift, 0f);
        }

        /*
         * "Martial Arts", "Internal Arts" and "Special Skill" (tab headings) are two short lines, the gap between them was
         * as wide as for a much bigger text. Their lines are drawn closer.
         */
        private const float TwoLineSpacing = 0.75f;
        private static readonly System.Text.RegularExpressions.Regex _Tags = new System.Text.RegularExpressions.Regex("<[^>]*>");
        private static readonly System.Text.RegularExpressions.Regex _Spaces = new System.Text.RegularExpressions.Regex(@"\s+");

        private static void TightTwoLines(Text text)
        {
            //Every label of the game comes through here: only the two headings go on to the regular expressions
            string raw = text.text;
            if (raw == null || (raw.IndexOf("Internal", System.StringComparison.OrdinalIgnoreCase) < 0 && raw.IndexOf("Special", System.StringComparison.OrdinalIgnoreCase) < 0
                && raw.IndexOf("Martial", System.StringComparison.OrdinalIgnoreCase) < 0))
            {
                return;
            }
            string plain = _Spaces.Replace(_Tags.Replace(raw, " "), " ").Trim();
            if (plain.Equals("Internal Arts", System.StringComparison.OrdinalIgnoreCase) || plain.Equals("Special Skill", System.StringComparison.OrdinalIgnoreCase)
                || plain.Equals("Martial Arts", System.StringComparison.OrdinalIgnoreCase))
            {
                text.lineSpacing = TwoLineSpacing;
            }
        }

        //Size the game gave each of those labels, they may be updated more than once
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Postfix(WGStringTable __instance, string ___id)
        {
            Text text = __instance.GetComponent<Text>();
            text.font = TextUtil.UIFont;
            TextUtil.FitWrapping(text);

            if (_FitWidth.TryGetValue(___id, out float margin))
            {
                //A size tag from the translation would keep the text at that size whatever font size we try
                if (_DropSizeTags.Contains(___id))
                {
                    text.text = _SizeTags.Replace(text.text, "");
                }
                TextUtil.ShrinkToWidth(text, MinTitleSize, margin);
            }

            WgBattleRound_UpdateRound_Hook.LabelChanged(text);
            TightTwoLines(text);
            SaveLoadTab.Fit(text, ___id);
            PawnshopTabs.Fit(text);
            FitMedicineTab(text, ___id);

            if (_Smaller.TryGetValue(___id, out int smaller))
            {
                if (!_OriginalSize.TryGetValue(text, out int originalSize))
                {
                    originalSize = text.fontSize;
                    _OriginalSize[text] = originalSize;
                }
                text.fontSize = originalSize - smaller;
            }
        }
    }

    [HarmonyPatch(typeof(UIBattleStatus), "UpdateView")]
    public class UIBattleStatus_UpdateView_Hook
    {
        public static void Postfix(ref UIBattleStatus __instance)
        {
            __instance.FinalCounterAttack.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalCrit.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalDodge.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalHit.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
            __instance.FinalParry.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;

            //The bio broke in the middle of words (the game turns the spaces into non-breaking spaces) and ran over the top of
            //its box. It wraps at spaces again and shrinks until it fits the box. The text area (200 high) is a little taller
            //than the light box drawn behind it (about 190), so some room is kept free above and below, fonts with tall lines
            //(Yu Gothic) otherwise stuck out of the box.
            TextUtil.ShrinkToBox(TextUtil.TextOf(__instance.Introduction), BioMinSize, BioPadding);

            //The inner art and gear names under their icons took 3 lines and ran into the icon ("Martial Code of the Fierce
            //Marquis"), or were cut at the right ("Cordierite Handwraps"). They shrink until they take at most 2 lines.
            foreach (WGPropsIntroduction slot in new[] { __instance.Mantra, __instance.Weapon, __instance.Cloth, __instance.Jewelry })
            {
                if (slot != null)
                {
                    TextUtil.ShrinkToLines(TextUtil.TextOf(_PropsName(slot)), NameLines, NameMinSize);
                }
            }
        }

        private const int BioMinSize = 12;
        private const float BioPadding = 8f;
        private const int NameLines = 2;
        private const int NameMinSize = 12;

        private static readonly AccessTools.FieldRef<WGPropsIntroduction, WGText> _PropsName = AccessTools.FieldRefAccess<WGPropsIntroduction, WGText>("propsName");
    }

    /*
     * The label under a buff or debuff icon in combat sits on a dark tab (OverlayIcon), which reads poorly behind the English
     * labels. Its images are hidden (HideBuffLabelBackground in the config file), the round icon and the label text stay.
     * Without the tab the small label stood straight on the battlefield and looked washed out on light ground, so it gets a
     * thin dark outline instead. Done whenever an icon is set up or its label changes, the icons are reused for every buff.
     */
    [HarmonyPatch]
    public class WGBufferIcon_LabelBackground_Hook
    {
        private static readonly AccessTools.FieldRef<WGBufferIcon, GameObject> _Tab = AccessTools.FieldRefAccess<WGBufferIcon, GameObject>("OverlayIcon");
        private static readonly AccessTools.FieldRef<WGBufferIcon, Image> _Icon = AccessTools.FieldRefAccess<WGBufferIcon, Image>("Icon");
        private static readonly AccessTools.FieldRef<WGBufferIcon, Text> _Label = AccessTools.FieldRefAccess<WGBufferIcon, Text>("Overlay");

        private static readonly Color OutlineColor = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Vector2 OutlineDistance = new Vector2(1f, -1f);

        //The outline is in the label's own units, a label drawn bigger and shrunk needs it bigger to look the same
        internal static Vector2 OutlineFor(Text label)
        {
            float scale = label.rectTransform.localScale.x;
            return scale > 0f ? OutlineDistance / scale : OutlineDistance;
        }

        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WGBufferIcon), "SetBufferOverlay");
            yield return AccessTools.Method(typeof(WGBufferIcon), "SetIcon");
        }

        public static void Postfix(WGBufferIcon __instance)
        {
            GameObject tab = _Tab(__instance);
            if (!EnglishPatch.HideBuffLabelBackground || tab == null)
            {
                return;
            }

            Image icon = _Icon(__instance);
            foreach (Image image in tab.GetComponentsInChildren<Image>(true))
            {
                if (image != icon)
                {
                    image.enabled = false;
                }
            }

            Text label = _Label(__instance);
            if (label != null && label.GetComponent<Outline>() == null)
            {
                Outline outline = label.gameObject.AddComponent<Outline>();
                outline.effectColor = OutlineColor;
                outline.effectDistance = OutlineFor(label);
            }
        }
    }

    /*
     * Watches a buff label drawn twice as big and shrunk by half (WGBufferIcon_SetBufferOverlay_Hook.Sharpen): if an
     * animation sets its scale, the halving is gone and it would be shown twice as big, so it goes back to the game's way.
     * Only compares the scale, once per frame.
     */
    public class SharpLabelGuard : MonoBehaviour
    {
        private Vector3 _Scale;

        public void Watch(Vector3 scale)
        {
            _Scale = scale;
        }

        private void LateUpdate()
        {
            if (transform.localScale == _Scale)
            {
                return;
            }
            Text label = GetComponent<Text>();
            if (label != null)
            {
                WGBufferIcon_SetBufferOverlay_Hook.Unsharpen(label);
            }
            Destroy(this);
        }
    }

    /*
     * The label under a stacking buff icon in combat is "{name}\n{level}" ("Confused" in orange, "Lv 1" below). Its box is as
     * wide as the icon, so the name broke in the middle ("Conf / used") and pushed the level out of the box.
     * Each line stays whole now, and the label shrinks until its widest line fits the space between two icons (a little
     * less, so labels of icons next to each other never touch).
     */
    [HarmonyPatch(typeof(WGBufferIcon), "SetBufferOverlay")]
    public class WGBufferIcon_SetBufferOverlay_Hook
    {
        private const int MinSize = 10;
        //Part of the distance between two icons a label may take
        private const float RoomShare = 0.95f;

        private static readonly AccessTools.FieldRef<WGBufferIcon, Text> _Overlay = AccessTools.FieldRefAccess<WGBufferIcon, Text>("Overlay");
        private static readonly AccessTools.FieldRef<WGBufferGroup, System.Collections.Generic.List<WGBufferIcon>> _Icons =
            AccessTools.FieldRefAccess<WGBufferGroup, System.Collections.Generic.List<WGBufferIcon>>("bufferItems");

        //Size each label has in the prefab, the icons are reused for every buff
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Postfix(WGBufferIcon __instance, int overlay)
        {
            Text label = _Overlay(__instance);
            if (overlay == 0 || label == null || string.IsNullOrEmpty(label.text))
            {
                return;
            }

            if (!_OriginalSize.TryGetValue(label, out int size))
            {
                size = label.fontSize;
                _OriginalSize[label] = size;
            }
            int sharp = Sharpen(label);
            label.fontSize = size * sharp;
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = label.text.Replace('\u00A0', ' ');
            if (sharp != 1)
            {
                label.text = TextUtil.ScaleSizeTags(label.text, sharp);
            }

            float room = Mathf.Abs(label.rectTransform.InverseTransformVector(new Vector3(WorldRoom(__instance), 0f, 0f)).x);
            while (label.fontSize > MinSize * sharp && TextUtil.LinesWidth(label, label.text) > room)
            {
                label.fontSize--;
            }

            CloseToIcon(__instance, label);
        }

        /*
         * The label sat far below the round icon, a big gap was left between them. Its letters now start IconGap below
         * the bottom of the icon (measured from the top of the capitals of its first line).
         */
        private const float IconGap = 3f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        private static void CloseToIcon(WGBufferIcon buff, Text label)
        {
            RectTransform rect = label.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                TextUtil.Prune(_Origin);
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;

            Image icon = buff.Icon;
            if (icon == null || rect.parent == null || !SkillBarNames.LettersSpan(label, out float top, out float bottom))
            {
                return;
            }
            Vector3[] corners = new Vector3[4];
            icon.rectTransform.GetWorldCorners(corners);
            float gap = Mathf.Abs(rect.TransformVector(new Vector3(0f, IconGap, 0f)).y);
            float up = corners[0].y - gap - top;
            //Only closer: a label already close to the icon (or over it) stays where it is
            if (up > 0f)
            {
                rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, up, 0f)).y);
            }
        }

        /*
         * Hovering a buff zooms its icon and the label with it. Text is drawn for its own size, so zoomed it was blurry.
         * The label is drawn twice as big and shrunk back by half: the same size on screen, and sharp when zoomed.
         * Done once per label (its area doubled, its scale halved), returns how much bigger its font is drawn.
         */
        private const int Sharpness = 2;

        private static readonly System.Collections.Generic.HashSet<Text> _Sharpened = new System.Collections.Generic.HashSet<Text>();

        //Labels whose scale something else sets (an animation), they keep the game's way
        private static readonly System.Collections.Generic.HashSet<Text> _NotSharpened = new System.Collections.Generic.HashSet<Text>();

        internal static int Sharpen(Text label)
        {
            if (_Sharpened.Contains(label))
            {
                return Sharpness;
            }
            RectTransform rect = label.rectTransform;
            //Only an area of fixed size can simply be doubled
            if (_NotSharpened.Contains(label) || rect.anchorMin != rect.anchorMax)
            {
                return 1;
            }
            rect.sizeDelta *= Sharpness;
            rect.localScale /= Sharpness;
            _Sharpened.Add(label);
            SetOutline(label);
            label.gameObject.AddComponent<SharpLabelGuard>().Watch(rect.localScale);
            return Sharpness;
        }

        //Back to the game's way, when an animation sets the label's scale itself (it would be shown twice as big)
        internal static void Unsharpen(Text label)
        {
            if (!_Sharpened.Remove(label))
            {
                return;
            }
            _NotSharpened.Add(label);
            label.rectTransform.sizeDelta /= Sharpness;
            label.fontSize = Mathf.Max(1, label.fontSize / Sharpness);
            label.text = TextUtil.ScaleSizeTags(label.text, 1f / Sharpness);
            SetOutline(label);
        }

        private static void SetOutline(Text label)
        {
            Outline outline = label.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectDistance = WGBufferIcon_LabelBackground_Hook.OutlineFor(label);
            }
        }

        //The icon row each icon belongs to, found once (the icons stay in their row)
        private static readonly System.Collections.Generic.Dictionary<WGBufferIcon, WGBufferGroup> _Group = new System.Collections.Generic.Dictionary<WGBufferIcon, WGBufferGroup>();

        //The distance between two icons of the group, or the icon's own width when the group has a single icon
        private static float WorldRoom(WGBufferIcon icon)
        {
            if (!_Group.TryGetValue(icon, out WGBufferGroup group))
            {
                group = icon.GetComponentInParent<WGBufferGroup>();
                _Group[icon] = group;
            }
            System.Collections.Generic.List<WGBufferIcon> icons = group != null ? _Icons(group) : null;
            if (icons != null && icons.Count >= 2 && icons[0] != null && icons[1] != null)
            {
                return Mathf.Abs(icons[1].transform.position.x - icons[0].transform.position.x) * RoomShare;
            }
            RectTransform rect = icon.Icon != null ? icon.Icon.rectTransform : (RectTransform)icon.transform;
            return Mathf.Abs(rect.TransformVector(new Vector3(rect.rect.width, 0f, 0f)).x);
        }
    }

    //The skill row of the battle status window (a character's status in combat): the same as the skill bar
    [HarmonyPatch(typeof(UIBattleStatus), "set_all_button")]
    public class UIBattleStatus_SetAllButton_Hook
    {
        public static void Postfix(UIBattleStatus __instance)
        {
            UIBattleUnitMenu_SetAllButton_Hook.FitNames(__instance.Skill_Buttons, __instance.Special_Skill_Button);
        }
    }

    /*
     * The skill bar at the bottom of the battle screen: long skill names ran into the names next to them or were cut.
     * Each name gets the room between two skills (a little less, so neighbours keep a gap), goes on two lines if one is
     * too wide, and only shrinks if two lines are still too wide.
     */
    [HarmonyPatch(typeof(UIBattleUnitMenu), "set_all_button")]
    public class UIBattleUnitMenu_SetAllButton_Hook
    {
        private const int MinSize = 12;
        //Part of the distance between two skills a name may take
        private const float RoomShare = 0.92f;

        private static readonly AccessTools.FieldRef<WGSkillBtn, Text> _SkillName = AccessTools.FieldRefAccess<WGSkillBtn, Text>("skill_name");

        //Size each name has in the prefab, the buttons are reused for every unit
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Postfix(WGSkillBtn[] ___skill_buttons, WGSkillBtn ___special_skill_button)
        {
            FitNames(___skill_buttons, ___special_skill_button);
        }

        //Also used for the skill row of the battle status window (UIBattleStatus_SetAllButton_Hook)
        public static void FitNames(WGSkillBtn[] buttons, WGSkillBtn special)
        {
            if (buttons == null || buttons.Length < 2 || buttons[0] == null || buttons[1] == null)
            {
                return;
            }

            float room = Mathf.Abs(buttons[1].transform.position.x - buttons[0].transform.position.x) * RoomShare;
            foreach (WGSkillBtn button in buttons)
            {
                Fit(button, room);
            }
            Fit(special, room);
        }

        private static void Fit(WGSkillBtn button, float worldRoom)
        {
            Text name = button == null ? null : _SkillName(button);
            if (name == null || string.IsNullOrEmpty(name.text))
            {
                return;
            }

            if (!_OriginalSize.TryGetValue(name, out int size))
            {
                size = name.fontSize;
                _OriginalSize[name] = size;
            }
            name.fontSize = size;
            name.resizeTextForBestFit = false;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.verticalOverflow = VerticalWrapMode.Overflow;

            float room = Mathf.Abs(name.rectTransform.InverseTransformVector(new Vector3(worldRoom, 0f, 0f)).x);
            string oneLine = name.text.Replace('\n', ' ').Replace('\u00A0', ' ').Trim();
            name.text = TextUtil.LinesWidth(name, oneLine) <= room ? oneLine : TextUtil.TwoLines(name, oneLine);
            while (name.fontSize > MinSize && TextUtil.LinesWidth(name, name.text) > room)
            {
                name.fontSize--;
            }
        }
    }

    [HarmonyPatch(typeof(UIQuest), "SetTrackedQuest")]
    public class UIQuest_SetTrackedQuest_Hook
    {
        public static void Postfix(ref UIQuest __instance, ref WGText ___questName, ref WGText ___description)
        {
            ___questName.GetComponent<Text>().fontSize = 18;
            ___description.GetComponent<Text>().fontSize = 16;
            //Longer quest texts still ran out of the box at 16, those shrink until they fit
            TextUtil.ShrinkToHeight(___description.GetComponent<Text>(), 12);
        }
    }

    [HarmonyPatch(typeof(WGBattleInfo), "UpdateInfo")]
    public class WGBattleInfo_UpdateInfo_Hook
    {
        public static void Postfix(Text ___Name)
        {
            ___Name.font = TextUtil.UIFont;
            ___Name.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }

    /*
     * WGPaoMaDengText is a marquee: text taller (or wider) than its box scrolls through after 2 seconds.
     * For the skill effect and the course description we shrink the font until the text fits the box, so it can be read at once.
     * Text that does not even fit at the minimum size still scrolls like before. Other marquees (like the inner art effect) stay as they are.
     */
    [HarmonyPatch(typeof(WGPaoMaDengText), "AddMessage")]
    public class WGPaoMaDengText_AddMessage_Hook
    {
        private const int MinFontSize = 16;

        //Marquees that shrink to fit, registered by the widgets owning them (see below)
        public static readonly System.Collections.Generic.HashSet<WGPaoMaDengText> FitToBox = new System.Collections.Generic.HashSet<WGPaoMaDengText>();

        //Marquees whose text is also justified
        public static readonly System.Collections.Generic.HashSet<WGPaoMaDengText> Justify = new System.Collections.Generic.HashSet<WGPaoMaDengText>();

        //Font size the prefab gave each marquee text, before we started shrinking it
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalFontSize = new System.Collections.Generic.Dictionary<Text, int>();

        public static void Prefix(WGPaoMaDengText __instance, ref string msg, Text ___pmdText, Image ___mask)
        {
            //The original measures the text right after setting it, so the English font has to be set before that
            ___pmdText.font = TextUtil.UIFont;

            if (msg == null || !FitToBox.Contains(__instance) || __instance.direction != WGPaoMaDengText.Direction.Vertical || ___mask == null)
            {
                return;
            }

            //StringTool.Replace turned the spaces into non-breaking spaces, which made English break in the middle of words
            msg = msg.Replace(' ', ' ');

            if (!_OriginalFontSize.TryGetValue(___pmdText, out int originalSize))
            {
                originalSize = ___pmdText.fontSize;
                _OriginalFontSize[___pmdText] = originalSize;
            }

            //A justified text was set to not wrap, measuring has to wrap again
            ___pmdText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float maxHeight = ___mask.rectTransform.rect.height;
            int size = originalSize;
            string fitted = msg;
            ___pmdText.fontSize = size;
            ___pmdText.text = fitted;
            while (___pmdText.preferredHeight > maxHeight && size > MinFontSize)
            {
                size--;
                fitted = TextUtil.ScaleSizeTags(msg, (float)size / originalSize);
                ___pmdText.fontSize = size;
                ___pmdText.text = fitted;
            }
            msg = fitted;

            if (Justify.Contains(__instance))
            {
                string justified = TextUtil.Justified(___pmdText, msg);
                if (justified != msg)
                {
                    ___pmdText.supportRichText = true;
                    ___pmdText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    msg = justified;
                }
            }
        }
    }

    /*
     * The item Info box (and Effect box) on the character screen:
     * Gear tab: the Info box reaches down to the bottom of the Effect box next to it, the texts shrink (down to 14) if they still do not fit.
     * Items tab (Gift, Books, Quest): nothing is below the Info box, every item gets the same taller box (see InfoBox.Grow).
     * Medicine tab: the Effect part moves down and gives up some height, the Info box takes that room (see InfoBox.Split).
     * WGPropsIntroduction is also used by popups (gear selection, gifts, battle items), the shop, forge, elixirs and battle status,
     * there only the Info text is justified.
     */
    [HarmonyPatch(typeof(WGPropsIntroduction), "UpdateWidget")]
    public class WGPropsIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;
        //The "Info" and "Effect" titles are 25 in the prefab
        private const int MedicineTitleSize = 20;
        //What the Effect box gives up for Info on the Medicine tab: it keeps room for three lines of size 20
        private const float MedicineEffectShrink = 22f;
        //Four more lines of size 20 (23 each) than the two the box was made for, plus the gap kept below the last line
        private const float ItemsExtraHeight = 100f;

        public static void Postfix(WGPropsIntroduction __instance, WGText ___description, WGText ___effectDescription, WGText ___conditionDescription)
        {
            Text info = TextUtil.TextOf(___description);
            Text effect = TextUtil.TextOf(___effectDescription);
            Text condition = TextUtil.TextOf(___conditionDescription);

            //"Suitable for:" got a space on both sides of the colon, on the Gear tab, in the gear selection and in the combat popup
            bool gearPanel = IsGearPanel(__instance);
            bool gearPopup = IsPanelOf<UIInventoryWindow>(__instance);
            bool battlePopup = IsPanelOf<UIBattleInventoryWindow>(__instance);
            if (gearPanel || gearPopup || battlePopup)
            {
                SpaceSuitableFor(condition);
            }

            //The character screen tabs, the gear selection and the combat item popup each get their own layout below.
            //The other places using this widget (gifts, shop, forge, elixirs...) are left as they are.
            if (gearPanel)
            {
                //The Info box always reaches down to the bottom of the Effect box next to it
                InfoBox.SideMargins(info);
                InfoBox.Grow(info, float.MaxValue, MinSize);
                TextUtil.ShrinkToHeight(effect, MinSize);
            }
            else if (IsPanelOf<UIInventory>(__instance) && effect == null)
            {
                //Items tab: every item gets the same box, with room for six lines, only longer texts make it bigger
                InfoBox.SideMargins(info);
                InfoBox.Grow(info, ItemsExtraHeight, MinSize);
            }
            else if (IsPanelOf<UIMedicine>(__instance) && effect != null)
            {
                //Medicine tab: the "Info" and "Effect" titles are a little smaller, and the Effect part below moves down
                //(and loses its empty space) to make room for Info
                SetTitleSize(info, MedicineTitleSize);
                SetTitleSize(effect, MedicineTitleSize);
                InfoBox.Split(info, effect, MedicineEffectShrink, MinSize);
            }
            else if (gearPopup)
            {
                //Gear selection popup: a long Info grows its band and pushes the parts below down
                InfoBox.GearPopup(info, condition, effect, MinSize);
                PopupTitles.SetSize(TextUtil.OwningForm(__instance));
            }
            else if (battlePopup)
            {
                //Item and gear popup in combat: Info is left aligned so it can be justified, Criteria and Effect stay centered,
                //and the Criteria and Effect parts move down to make room for a long Info (see BattlePopup)
                if (info != null)
                {
                    info.alignment = (TextAnchor)((int)info.alignment / 3 * 3);
                }
                BattlePopup.Layout(__instance, info, condition, effect, MinSize);
            }
            else if (TextUtil.OwningForm(__instance) is UIForm shop && (shop is UIPawnshop || shop is UIAlchemy))
            {
                //Refine Medicine: the "Effect" heading sat right under the Info text, it moves down to the middle between
                //the Info text and the effect
                if (shop is UIAlchemy)
                {
                    MoveTitleDown(effect, AlchemyEffectTitleDown);
                }

                //Pawn shop and Refine Medicine: the effect ("Restore 500 Qi") started at the left edge of the middle panel,
                //under the centered "Effect" heading. It is centered under that heading now.
                CenterUnderTitle(effect);
            }

            //The item Info is justified everywhere
            TextUtil.Justify(info);
        }

        private static readonly AccessTools.FieldRef<UIEquip, WGPropsIntroduction> _Weapon = AccessTools.FieldRefAccess<UIEquip, WGPropsIntroduction>("weapon");
        private static readonly AccessTools.FieldRef<UIEquip, WGPropsIntroduction> _Armor = AccessTools.FieldRefAccess<UIEquip, WGPropsIntroduction>("armor");
        private static readonly AccessTools.FieldRef<UIEquip, WGPropsIntroduction> _Accessories = AccessTools.FieldRefAccess<UIEquip, WGPropsIntroduction>("accessories");
        private static readonly AccessTools.FieldRef<UIFormInventory, WGPropsIntroduction> _PropsIntroduction = AccessTools.FieldRefAccess<UIFormInventory, WGPropsIntroduction>("propsIntroduction");

        //One of the weapon, armor and accessories panels of the Gear tab
        private static bool IsGearPanel(WGPropsIntroduction widget)
        {
            UIEquip equip = TextUtil.OwningForm(widget) as UIEquip;
            return equip != null && (_Weapon(equip) == widget || _Armor(equip) == widget || _Accessories(equip) == widget);
        }

        //The item panel of that inventory screen itself (Items tab: UIInventory, Medicine tab: UIMedicine, gear selection: UIInventoryWindow)
        private static bool IsPanelOf<T>(WGPropsIntroduction widget) where T : UIFormInventory
        {
            T form = TextUtil.OwningForm(widget) as T;
            return form != null && _PropsIntroduction(form) == widget;
        }

        //How far the "Effect" heading of Refine Medicine moves down (measured at 1920x1080)
        private const float AlchemyEffectTitleDown = 20f;

        //Where each moved heading sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _TitleOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //Moves the heading of a text down (the text itself stays)
        private static void MoveTitleDown(Text text, float down)
        {
            RectTransform title = Heading(text)?.rectTransform;
            if (title == null)
            {
                return;
            }
            if (!_TitleOrigin.TryGetValue(title, out Vector2 origin))
            {
                origin = title.anchoredPosition;
                _TitleOrigin[title] = origin;
            }
            title.anchoredPosition = origin - new Vector2(0f, down);
        }

        //Where each centered effect text sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _EffectOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //String ids of the "Effect" heading
        private static readonly string[] _EffectTitleIds = { "General_Effect", "SecondaryInterface1406" };

        //The heading found for each text, looked up once
        private static readonly System.Collections.Generic.Dictionary<Text, Text> _Heading = new System.Collections.Generic.Dictionary<Text, Text>();

        //The "Effect" heading of an effect text: the label of its screen that shows "Effect" (by its string id or its text)
        //whose middle is closest above the middle of the effect text (on the pawn shop and Refine Medicine it is a sibling
        //named Text, the game gives it no fixed name)
        private static Text Heading(Text text)
        {
            if (text == null)
            {
                return null;
            }
            //Kept once found. Not found yet (the heading may not have its English text yet) or destroyed with its screen
            //(it then compares equal to null): looked up again.
            if (_Heading.TryGetValue(text, out Text heading) && heading != null)
            {
                return heading;
            }

            heading = null;
            UIForm form = TextUtil.OwningForm(text);
            if (form != null)
            {
                var words = new System.Collections.Generic.HashSet<string>();
                foreach (string id in _EffectTitleIds)
                {
                    string word = Heluo.Game.Data?.Get<StringTable>(id)?.Text;
                    if (!string.IsNullOrEmpty(word))
                    {
                        words.Add(word.Trim());
                    }
                }

                //Centers are compared: the text box can be much taller than its text, its top may be above the heading
                float center = text.rectTransform.TransformPoint(text.rectTransform.rect.center).y;
                float best = float.MaxValue;
                foreach (Text label in form.GetComponentsInChildren<Text>(true))
                {
                    if (label == text)
                    {
                        continue;
                    }
                    WGStringTable table = label.GetComponent<WGStringTable>();
                    bool isEffect = (table != null && System.Array.IndexOf(_EffectTitleIds, TextUtil.StringId(table)) >= 0)
                        || (label.text != null && words.Contains(label.text.Trim()));
                    float above = label.rectTransform.TransformPoint(label.rectTransform.rect.center).y - center;
                    if (isEffect && above > 0f && above < best)
                    {
                        best = above;
                        heading = label;
                    }
                }
            }

            _Heading[text] = heading;
            return heading;
        }

        //Centers a text and lines it up under its heading (just centered if there is none)
        private static void CenterUnderTitle(Text text)
        {
            if (text == null)
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            if (!_EffectOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _EffectOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin;
            text.alignment = (TextAnchor)((int)text.alignment / 3 * 3 + 1);

            Text title = Heading(text);
            if (title == null || string.IsNullOrEmpty(title.text))
            {
                return;
            }
            TextUtil.VisibleX(title, out float titleLeft, out float titleRight);
            float textCenter = rect.TransformPoint(rect.rect.center).x;
            rect.position += new Vector3((titleLeft + titleRight) / 2f - textCenter, 0f, 0f);
        }

        //"Suitable for:" is SecondaryInterface0502, the game puts the character names right after it, separated by the
        //Chinese comma 、. The names become "Suitable for : Zhao Zilong, Xiao Qianling". Only that first line is changed,
        //the game may add another condition on the next line.
        private static void SpaceSuitableFor(Text condition)
        {
            string label = Heluo.Game.Data?.Get<StringTable>("SecondaryInterface0502")?.Text;
            if (condition == null || string.IsNullOrEmpty(label) || condition.text == null || !condition.text.StartsWith(label, System.StringComparison.Ordinal))
            {
                return;
            }
            string spaced = label.TrimEnd().TrimEnd(':', '：').TrimEnd() + " : ";
            string rest = condition.text.Substring(label.Length).TrimStart();
            int lineEnd = rest.IndexOf('\n');
            string names = lineEnd < 0 ? rest : rest.Substring(0, lineEnd);
            string after = lineEnd < 0 ? "" : rest.Substring(lineEnd);
            condition.text = spaced + names.Replace("、", ", ") + after;
        }

        //The title ("Info", "Effect") is the Title child of the box the text sits in
        private static void SetTitleSize(Text text, int size)
        {
            Transform title = text != null && text.transform.parent != null ? text.transform.parent.Find("Title") : null;
            Text titleText = title != null ? title.GetComponent<Text>() : null;
            if (titleText != null)
            {
                titleText.fontSize = size;
            }
        }
    }

    /*
     * The item Info box is a background image with the text stretched inside it, sized for two lines of Chinese.
     * The box keeps its top and grows down: by at least minGrow (so it does not change size from item to item),
     * and more when the text needs it, but never past the bottom of the panel it sits in.
     * A text that still does not fit shrinks (never below minSize). The text always starts at the top of the box.
     */
    public static class InfoBox
    {
        //Size and position each box has in the prefab, so we always change it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        public static void Grow(Text text, float minGrow, int minSize)
        {
            RectTransform box = text != null ? text.transform.parent as RectTransform : null;
            RectTransform panel = box != null ? box.parent as RectTransform : null;
            if (panel == null || box.GetComponent<Image>() == null)
            {
                return;
            }

            Restore(box);
            if (string.IsNullOrEmpty(text.text))
            {
                return;
            }

            TopAlign(text);
            NormalSpaces(text);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            float missing = text.preferredHeight - text.rectTransform.rect.height;
            float wanted = Mathf.Max(missing, minGrow);
            if (wanted <= 0f)
            {
                return;
            }

            float room = Mathf.Max(0f, Bottom(box, panel) - panel.rect.yMin);
            float grow = Mathf.Min(wanted, room);
            MoveEdges(box, 0f, grow);

            if (missing > grow)
            {
                TextUtil.ShrinkToHeight(text, minSize);
            }
        }

        /*
         * Two boxes above each other (Info and Effect on the Medicine tab), laid out the same for every item:
         * the lower box moves down to the bottom of the panel and gives up belowShrink of its height, the upper box
         * grows down into all of that room. Both texts start at the top of their box and shrink only when they do not fit.
         */
        public static void Split(Text text, Text below, float belowShrink, int minSize)
        {
            RectTransform box = text != null ? text.transform.parent as RectTransform : null;
            RectTransform panel = box != null ? box.parent as RectTransform : null;
            RectTransform belowBox = below != null ? below.transform.parent as RectTransform : null;
            if (panel == null || belowBox == null || belowBox.parent != panel)
            {
                return;
            }

            Restore(box);
            Restore(belowBox);

            float drop = Mathf.Max(0f, Bottom(belowBox, panel) - panel.rect.yMin);
            float grow = drop + belowShrink;
            MoveEdges(box, 0f, grow);
            MoveEdges(belowBox, grow, drop);

            foreach (Text t in new[] { text, below })
            {
                TopAlign(t);
                NormalSpaces(t);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (t.preferredHeight > t.rectTransform.rect.height)
                {
                    TextUtil.ShrinkToHeight(t, minSize);
                }
            }
        }

        /*
         * The game turns the spaces into non-breaking spaces (StringTool.Replace). Measured like that, the text breaks in the
         * middle of words and looks a line shorter than it is once the justifying turns the spaces back, so the last line
         * ended up below the box. The text is measured with normal spaces instead.
         */
        private static void NormalSpaces(Text text)
        {
            if (text != null && text.text != null && text.text.IndexOf('\u00A0') >= 0)
            {
                text.text = text.text.Replace('\u00A0', ' ');
            }
        }

        //Space between the box edges and the text, as in the Effect boxes and on the Medicine tab
        private const float LeftMargin = 25f;
        private const float RightMargin = 20f;

        //The Info text on the Gear and Items tabs sat 5 further from the left edge than every other box text
        public static void SideMargins(Text text)
        {
            if (text == null)
            {
                return;
            }
            RectTransform rect = text.rectTransform;
            rect.offsetMin = new Vector2(LeftMargin, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-RightMargin, rect.offsetMax.y);
        }

        //Extra gap between the box edges and the text, at the top and the same at the bottom. The prefab leaves 9, which looked cramped.
        private const float Padding = 8f;

        //Top and bottom edge each text area has in the prefab
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _OriginalEdges = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //The text starts at the top of its box, with the same small gap above the first line and below the last one
        private static void TopAlign(Text text)
        {
            text.alignment = (TextAnchor)((int)text.alignment % 3);
            RectTransform rect = text.rectTransform;
            if (!_OriginalEdges.TryGetValue(rect, out Vector2 edges))
            {
                edges = new Vector2(rect.offsetMax.y, rect.offsetMin.y);
                _OriginalEdges[rect] = edges;
            }
            rect.offsetMax = new Vector2(rect.offsetMax.x, edges.x - Padding);
            rect.offsetMin = new Vector2(rect.offsetMin.x, edges.y + Padding);
        }

        /*
         * The gear selection popup: the headings and the light bands behind the texts are part of the popup's art and
         * cannot move. The Info text area (75 high, 3 lines) sits inside its band, an Info text with more lines ran over
         * the "Criteria" heading. Such a text may now use the band down to just above that heading, and shrinks only
         * as much as it needs to fit. Texts of up to 3 lines are not changed. Only done when the popup has exactly the
         * Description, ConditionDescription and EffectDescription parts.
         */
        //How much further down the Info band goes than the text area, before the "Criteria" heading starts
        private const float PopupBandBelowText = 5f;

        public static void GearPopup(Text info, Text condition, Text effect, int minSize)
        {
            RectTransform infoHeader = info != null ? info.transform.parent as RectTransform : null;
            RectTransform conditionHeader = condition != null ? condition.transform.parent as RectTransform : null;
            RectTransform effectHeader = effect != null ? effect.transform.parent as RectTransform : null;
            if (infoHeader == null || conditionHeader == null || effectHeader == null
                || infoHeader.name != "Description" || conditionHeader.name != "ConditionDescription" || effectHeader.name != "EffectDescription"
                || infoHeader.parent != conditionHeader.parent || infoHeader.parent != effectHeader.parent)
            {
                return;
            }

            Restore(info.rectTransform);
            if (string.IsNullOrEmpty(info.text))
            {
                return;
            }

            NormalSpaces(info);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (info.preferredHeight <= info.rectTransform.rect.height)
            {
                return;
            }

            MoveEdges(info.rectTransform, 0f, PopupBandBelowText);
            TextUtil.ShrinkToHeight(info, minSize);
        }

        private static void Restore(RectTransform box)
        {
            if (box == null)
            {
                return;
            }
            if (!_Original.TryGetValue(box, out Vector2[] original))
            {
                original = new[] { box.sizeDelta, box.anchoredPosition };
                _Original[box] = original;
            }
            box.sizeDelta = original[0];
            box.anchoredPosition = original[1];
        }

        //Bottom edge of a box, in the panel's space
        private static float Bottom(RectTransform box, RectTransform panel)
        {
            Vector3[] corners = new Vector3[4];
            box.GetLocalCorners(corners);
            return panel.InverseTransformPoint(box.TransformPoint(corners[0])).y;
        }

        //Moves the top edge of a box down by topDown and its bottom edge down by bottomDown, whatever its pivot
        private static void MoveEdges(RectTransform box, float topDown, float bottomDown)
        {
            box.sizeDelta += new Vector2(0f, bottomDown - topDown);
            box.anchoredPosition += new Vector2(0f, -bottomDown + box.pivot.y * (bottomDown - topDown));
        }
    }

    //The skill effect box (reading screen and martial arts form) shrinks to fit
    [HarmonyPatch(typeof(WGSkillsIntroduction), "UpdateWidget")]
    public class WGSkillsIntroduction_UpdateWidget_Hook
    {
        //The skill effect shrinks to fit its box and is justified, like the course description
        public static void Prefix(WGPaoMaDengText ___effectDescription)
        {
            if (___effectDescription != null)
            {
                WGPaoMaDengText_AddMessage_Hook.FitToBox.Add(___effectDescription);
                WGPaoMaDengText_AddMessage_Hook.Justify.Add(___effectDescription);
            }
        }

        //The book description under the title is justified
        public static void Postfix(WGSkillsIntroduction __instance, WGText ___description, WGText ___name, WGText ___requestMP,
            WGText ___hurt, WGText ___range, WGText ___area, WGText ___cD, WGPaoMaDengText ___effectDescription)
        {
            TextUtil.Justify(TextUtil.TextOf(___description));

            if (TextUtil.OwningForm(__instance) is UIMartialArtsWindow)
            {
                RaiseStats(__instance, new[] { ___hurt, ___range, ___area, ___cD },
                    new Component[] { ___description, ___name, ___requestMP, ___effectDescription });
            }
        }

        /*
         * Skill selection popup: the Damage, Attack Distance, Attack Area and Cooldown Time rows sat low in their box
         * (about 22 above them and 13 below). The rows, names and numbers, move up a little. Only done when the part
         * holding the four numbers holds nothing else of the panel.
         */
        private const float StatsRaise = 6f;

        //Where each moved row is in the prefab, and the padding of a layout group holding the rows
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _OriginalRow = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<LayoutGroup, RectOffset> _OriginalPadding = new System.Collections.Generic.Dictionary<LayoutGroup, RectOffset>();

        private static void RaiseStats(WGSkillsIntroduction widget, WGText[] values, Component[] others)
        {
            Transform[] texts = System.Array.ConvertAll(values, v => TextUtil.TextOf(v) != null ? TextUtil.TextOf(v).transform : null);
            if (System.Array.IndexOf(texts, null) >= 0)
            {
                return;
            }

            //The smallest part of the panel holding all four numbers
            Transform rows = texts[0].parent;
            while (rows != null && !System.Array.TrueForAll(texts, t => t.IsChildOf(rows)))
            {
                rows = rows.parent;
            }
            //Holding only the numbers, it is the numbers column: the labels next to them are one level up
            if (rows != null && rows.GetComponentsInChildren<Text>(true).Length <= texts.Length)
            {
                rows = rows.parent;
            }
            if (rows == null || rows == widget.transform || !rows.IsChildOf(widget.transform))
            {
                return;
            }
            foreach (Component other in others)
            {
                if (other != null && other.transform.IsChildOf(rows))
                {
                    return;
                }
            }

            LayoutGroup layout = rows.GetComponent<LayoutGroup>();
            if (layout != null)
            {
                //The layout places the rows, so it gets less room on top and more below instead
                if (!_OriginalPadding.TryGetValue(layout, out RectOffset padding))
                {
                    padding = new RectOffset(layout.padding.left, layout.padding.right, layout.padding.top, layout.padding.bottom);
                    _OriginalPadding[layout] = padding;
                }
                layout.padding = new RectOffset(padding.left, padding.right, padding.top - (int)StatsRaise, padding.bottom + (int)StatsRaise);
                return;
            }

            foreach (Transform child in rows)
            {
                RectTransform row = child as RectTransform;
                if (row == null || child.GetComponentInChildren<Text>(true) == null)
                {
                    continue;
                }
                if (!_OriginalRow.TryGetValue(row, out Vector2 position))
                {
                    position = row.anchoredPosition;
                    _OriginalRow[row] = position;
                }
                row.anchoredPosition = position + new Vector2(0f, StatsRaise);
            }
        }
    }

    //The inner art book description is justified like the martial arts one
    [HarmonyPatch(typeof(WGMantraIntroduction), "UpdateWidget")]
    public class WGMantraIntroduction_UpdateWidget_Hook
    {
        public static void Postfix(WGText ___description)
        {
            TextUtil.Justify(TextUtil.TextOf(___description));
        }
    }

    //The course description on the elective screen shrinks to fit, and the learnable skill rows get room for their English names
    [HarmonyPatch(typeof(WGCourseIntroduction), "UpdateWidget")]
    public class WGCourseIntroduction_UpdateWidget_Hook
    {
        private const float Gap = 8f;
        private const int MinNameSize = 12;
        //The prefab label is bigger than the skill names (20), 18 keeps it a label next to them
        private const int LabelSize = 18;

        //Where each "Qi Usage" label sits in the prefab (inside its parent, so moving or scrolling the screen does not change it), so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _LabelOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        public static void Prefix(WGPaoMaDengText ___description)
        {
            if (___description != null)
            {
                WGPaoMaDengText_AddMessage_Hook.FitToBox.Add(___description);
                WGPaoMaDengText_AddMessage_Hook.Justify.Add(___description);
            }
        }

        //Each skill row is: skill name, "Qi Usage" label, Qi number. Long English names ran into the label.
        //We move the label right up to the number, then shrink the name if it still does not end before the label.
        public static void Postfix(WGCourseIntroduction __instance, GameObject[] ___skillGo, WGText[] ___skills, WGText[] ___mps)
        {
            CenterDescriptionTitle(__instance);

            if (___skillGo == null || ___skills == null || ___mps == null)
            {
                return;
            }

            for (int i = 0; i < ___skillGo.Length && i < ___skills.Length && i < ___mps.Length; i++)
            {
                if (___skillGo[i] == null || !___skillGo[i].activeSelf)
                {
                    continue;
                }

                Text name = TextUtil.TextOf(___skills[i]);
                Text mp = TextUtil.TextOf(___mps[i]);
                if (name == null || mp == null)
                {
                    continue;
                }

                Text label = null;
                foreach (Text t in ___skillGo[i].GetComponentsInChildren<Text>(true))
                {
                    if (t != name && t != mp && !string.IsNullOrEmpty(t.text))
                    {
                        label = t;
                        break;
                    }
                }

                FitRow(name, label, mp);
            }
        }

        //The "Description" title ran into the diamond at the right end of its band. It gets the size the translation gives the
        //"Skills Learned" title above it (20.5), and is centered on that title, which sits in the middle of the same band.
        private const string DescriptionTitleId = "SecondaryInterface0107";
        private const string SkillsTitleId = "SecondaryInterface0106";
        private const int DescriptionTitleSize = 20;

        //Where the "Description" title sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _TitleOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //The "Description" and "Skills Learned" titles of each course widget, found once
        private static readonly System.Collections.Generic.Dictionary<WGCourseIntroduction, Text[]> _Titles = new System.Collections.Generic.Dictionary<WGCourseIntroduction, Text[]>();

        private static void CenterDescriptionTitle(WGCourseIntroduction widget)
        {
            if (!_Titles.TryGetValue(widget, out Text[] titles))
            {
                UIForm form = TextUtil.OwningForm(widget);
                if (form == null)
                {
                    return;
                }
                titles = new Text[2];
                foreach (WGStringTable label in form.GetComponentsInChildren<WGStringTable>(true))
                {
                    string id = TextUtil.StringId(label);
                    if (id == DescriptionTitleId)
                    {
                        titles[0] = label.GetComponent<Text>();
                    }
                    else if (id == SkillsTitleId)
                    {
                        titles[1] = label.GetComponent<Text>();
                    }
                }
                _Titles[widget] = titles;
            }

            Text title = titles[0];
            Text skills = titles[1];
            if (title == null || string.IsNullOrEmpty(title.text))
            {
                return;
            }

            title.fontSize = DescriptionTitleSize;

            RectTransform rect = title.rectTransform;
            if (!_TitleOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _TitleOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin;

            if (skills == null || string.IsNullOrEmpty(skills.text))
            {
                return;
            }
            TextUtil.VisibleX(title, out float titleLeft, out float titleRight);
            TextUtil.VisibleX(skills, out float skillsLeft, out float skillsRight);
            rect.position += new Vector3((skillsLeft + skillsRight - titleLeft - titleRight) / 2f, 0f, 0f);
        }

        private static void FitRow(Text name, Text label, Text mp)
        {
            TextUtil.VisibleX(mp, out float mpLeft, out _);
            float rightLimit = mpLeft;

            if (label != null)
            {
                RectTransform labelRect = label.rectTransform;
                if (!_LabelOrigin.TryGetValue(labelRect, out Vector2 origin))
                {
                    origin = labelRect.anchoredPosition;
                    _LabelOrigin[labelRect] = origin;
                }
                labelRect.anchoredPosition = origin;
                label.fontSize = Mathf.Min(label.fontSize, LabelSize);

                TextUtil.VisibleX(label, out float labelLeft, out float labelRight);
                float shift = mpLeft - TextUtil.WorldWidth(labelRect, Gap) - labelRight;
                if (shift > 0)
                {
                    labelRect.position += new Vector3(shift, 0f, 0f);
                    labelLeft += shift;
                }
                rightLimit = labelLeft;
            }

            //One line, shrunk until it ends a gap before the label (or the number)
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            TextUtil.VisibleX(name, out float nameLeft, out float nameRight);
            float available = rightLimit - TextUtil.WorldWidth(name.rectTransform, Gap) - nameLeft;
            float width = nameRight - nameLeft;
            if (available > 0 && width > available)
            {
                name.fontSize = Mathf.Max(MinNameSize, Mathf.FloorToInt(name.fontSize * available / width));
            }
        }
    }

    [HarmonyPatch(typeof(WGNurturanceBtn), "UpdateButton")]
    public class WGNurturanceBtn_UpdateButton_Hook
    {
        public static void Postfix(WGNurturanceBtn __instance)
        {
            Text text = __instance.Btn_Name;
            text.font = TextUtil.UIFont;
            //Every label of the life menu (main row and all its pages) sits in the middle of its banner
            text.alignment = TextAnchor.MiddleCenter;
            ShrinkSkillLevel(__instance.SkillLevelText);
            Text level = __instance.SkillLevelText;
            if (level != null && level.isActiveAndEnabled && !string.IsNullOrEmpty(level.text))
            {
                Replacer.Run(__instance, () => CenterSkillLevel(level));
            }

            //Only Return, Back/Next (previous and next page), Socialize and the character names inside Socialize are fitted to their box, the other buttons keep the default size
            Nurturance nurturance = __instance.tree?.Value;
            bool fit = nurturance != null
                && (nurturance.Fuction == NurturanceFunction.Back || nurturance.Fuction == NurturanceFunction.Previous || nurturance.Fuction == NurturanceFunction.Next
                    || nurturance.Fuction == NurturanceFunction.Firend || nurturance.Id == "Nurturance_05");

            //Martial arts and inner arts tabs: long names took up to 4 lines, they shrink until they take at most 2
            if (nurturance != null && (nurturance.Fuction == NurturanceFunction.Skill || nurturance.Fuction == NurturanceFunction.Mantra))
            {
                TextUtil.ShrinkToLines(text, SkillLines, SkillMinSize);
                RaiseLabel(text);
                return;
            }

            if (!fit)
            {
                TextUtil.RestoreBoxFit(text);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                RaiseLabel(text);
                return;
            }

            //Return, Back and Next have a tall narrow box made for Chinese written top to bottom, English is stacked letter by letter there
            Rect rect = text.rectTransform.rect;
            bool stacked = rect.height > rect.width * 1.5f;
            if (stacked && !string.IsNullOrEmpty(text.text))
            {
                text.text = string.Join("\n", System.Array.ConvertAll(text.text.Replace(" ", "").ToCharArray(), c => c.ToString()));
            }

            TextUtil.ShrinkToBox(text, MinSize);
            if (!stacked)
            {
                RaiseLabel(text);
            }
        }

        /*
         * Unity centers a line by the whole height of the font, room for descenders (g, p, y) included. The labels hardly have any,
         * so they sat low in their banner (with Arial about 7 pixels above the capitals but 4 below the letters at 1080p, more with
         * Bahnschrift). How low depends on the font, so it is measured: the label moves until the band from the baseline to the
         * top of a capital H, laid out by Unity in this label's font and size, is in the middle of the label box. If that cannot
         * be measured, it is raised by a tenth of its size (right for Arial). The buttons are updated more than once, so the
         * position is always taken from the one the game gave the label.
         */
        private static void RaiseLabel(Text text)
        {
            RectTransform rect = text.rectTransform;
            if (!_LabelOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _LabelOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(0f, CapsCenterOffset(text) ?? text.fontSize * LabelRaise);
        }

        /*
         * How far the letters of a one line text are below the middle of its box, null when Unity lays out nothing.
         * The labels are mostly lower case, so what looks centered is between the capitals (baseline to top of H) and the
         * lower case letters (baseline to top of x): the middle of both bands is put in the middle of the box. That is where
         * the Arial labels already looked right, and other fonts get the same placement from their own letters.
         */
        private static float? CapsCenterOffset(Text text)
        {
            Rect box = text.rectTransform.rect;
            TextGenerationSettings settings = text.GetGenerationSettings(box.size);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.textAnchor = TextAnchor.MiddleCenter;
            settings.richText = false;

            float? caps = GlyphMiddle("H", settings, text.pixelsPerUnit);
            float? lower = GlyphMiddle("x", settings, text.pixelsPerUnit);
            if (caps == null || lower == null)
            {
                return null;
            }

            //The vertices are relative to the pivot of the box
            return box.center.y - (caps.Value + lower.Value) / 2f;
        }

        //Middle height of one letter laid out with these settings, in the units of the box
        private static float? GlyphMiddle(string letter, TextGenerationSettings settings, float pixelsPerUnit)
        {
            _CapsGenerator.Populate(letter, settings);
            System.Collections.Generic.IList<UIVertex> verts = _CapsGenerator.verts;
            if (verts.Count < 4 || pixelsPerUnit <= 0f)
            {
                return null;
            }

            float bottom = float.MaxValue, top = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                bottom = Mathf.Min(bottom, verts[i].position.y);
                top = Mathf.Max(top, verts[i].position.y);
            }
            return (bottom + top) / 2f / pixelsPerUnit;
        }

        private const float LabelRaise = 0.1f;
        private static readonly TextGenerator _CapsGenerator = new TextGenerator();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _LabelOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //The skill level next to the martial arts icons ("10") was sized for the game's font, its digits are a lot bigger in the
        //English font the life screen now uses (see UIForm_Show_LifeFont_Hook). Buttons are updated more than once, so the size
        //is always taken from the one the game gave the label.
        private static void ShrinkSkillLevel(Text level)
        {
            if (level == null)
            {
                return;
            }

            if (!_LevelSize.TryGetValue(level, out int size))
            {
                size = level.fontSize;
                _LevelSize[level] = size;
            }
            level.fontSize = Mathf.RoundToInt(size * SkillLevelScale);
        }

        /*
         * The skill level ("10", "1") sat off the middle of the small diamond behind it: its drawn digits are put in the
         * middle of what the diamond draws, both ways (see UIBattle_SkillHotKeys_Hook, the same for the combat key numbers).
         */
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _LevelOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        private static void CenterSkillLevel(Text level)
        {
            if (level == null || !level.isActiveAndEnabled || string.IsNullOrEmpty(level.text))
            {
                return;
            }
            level.verticalOverflow = VerticalWrapMode.Overflow;
            level.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform rect = level.rectTransform;
            if (!_LevelOrigin.TryGetValue(rect, out Vector2 origin))
            {
                TextUtil.Prune(_LevelOrigin);
                origin = rect.anchoredPosition;
                _LevelOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin;

            Image badge = UIBattle_SkillHotKeys_Hook.BadgeOf(level);
            Vector3? ink = UIBattle_SkillHotKeys_Hook.InkCenter(level);
            if (badge == null || ink == null || rect.parent == null)
            {
                return;
            }
            Vector3 shift = rect.parent.InverseTransformVector(UIBattle_SkillHotKeys_Hook.DrawnMiddle(badge) - ink.Value);
            //Within the diamond, a bigger move is a measure gone wrong
            Vector2 size = badge.rectTransform.rect.size;
            if (Mathf.Abs(shift.x) <= size.x && Mathf.Abs(shift.y) <= size.y)
            {
                rect.anchoredPosition = origin + new Vector2(shift.x, shift.y);
            }
        }

        private const float SkillLevelScale = 0.8f;
        private static readonly System.Collections.Generic.Dictionary<Text, int> _LevelSize = new System.Collections.Generic.Dictionary<Text, int>();

        private const int MinSize = 10;
        private const int SkillLines = 2;
        private const int SkillMinSize = 12;
    }

    [HarmonyPatch(typeof(UIRegistration), "NameCheck")]
    public class UIRegistration_NameCheck_Hook
    {
        public static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamPlatform), "CheckWordLegality")]
    public class SteamPlatform_CheckWordLegality_Hook
    {
        public static bool Prefix(string word, ref bool __result)
        {
            __result = true;
            return false;
        }
    }

    /*
     * The skill and inner art selection popup (Skills tab): the name area of each row was sized for a few Chinese
     * characters, English names wrapped into two or three lines and ran over the "Lv" below them. The name area is now
     * as wide as "Long Howl from Heavenly Gate" plus a little, its left end stays where it is, longer names take two lines.
     * The small scroll in front of "Lv" is drawn a little smaller, so it does not touch names of two lines.
     * Every other list with this button (gear selection, the Medicine tab, combat) gets the same wider names
     * ("Bloodstained Iron Claw" on one line), never past the "New" badge some lists show on the right, nor past the
     * right edge of the row itself.
     */
    [HarmonyPatch(typeof(WGScrollBtn), "UpdateWidget")]
    public class WGScrollBtn_UpdateWidget_Hook
    {
        private const float NameWidth = 290f;
        //The small icon in front of "Lv" or the count (a scroll, a hand) is drawn smaller, on every list
        private const float LevelIconScale = 0.65f;

        //Size each scroll has in the prefab, and size and place of what sits inside it ("Lv 10" is a child of the scroll)
        private static readonly System.Collections.Generic.Dictionary<Transform, Vector3> _OriginalIconScale = new System.Collections.Generic.Dictionary<Transform, Vector3>();
        private static readonly System.Collections.Generic.Dictionary<Transform, Vector3[]> _OriginalChild = new System.Collections.Generic.Dictionary<Transform, Vector3[]>();

        //Width and position each name area has in the prefab
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        //Room kept between a name and the "New" badge
        private const float SignGap = 6f;

        public static void Postfix(WGScrollBtn __instance, WGText ___itemName, WGImage ___remarkIcon, GameObject ___sign)
        {
            Text name = TextUtil.TextOf(___itemName);
            if (name == null)
            {
                return;
            }

            //The small icon in front of the level or the count (the "Lv" scroll of skills, the count of items), on every list
            Transform levelIcon = ___remarkIcon != null ? ___remarkIcon.transform : null;
            if (levelIcon != null)
            {
                if (!_OriginalIconScale.TryGetValue(levelIcon, out Vector3 scale))
                {
                    scale = levelIcon.localScale;
                    _OriginalIconScale[levelIcon] = scale;
                }
                levelIcon.localScale = scale * LevelIconScale;

                //What sits inside the scroll gets the opposite, so "Lv 10" keeps its size and place
                foreach (Transform child in levelIcon)
                {
                    if (!_OriginalChild.TryGetValue(child, out Vector3[] inside))
                    {
                        inside = new[] { child.localScale, child.localPosition };
                        _OriginalChild[child] = inside;
                    }
                    child.localScale = inside[0] / LevelIconScale;
                    child.localPosition = inside[1] / LevelIconScale;
                }
            }

            RectTransform rect = name.rectTransform;
            if (!_Original.TryGetValue(rect, out Vector2[] original))
            {
                original = new[] { rect.sizeDelta, rect.anchoredPosition };
                _Original[rect] = original;
            }
            rect.sizeDelta = original[0];
            rect.anchoredPosition = original[1];

            float grow = NameWidth - rect.rect.width;
            //Not past the right edge of the row
            if (__instance.transform is RectTransform row && rect.parent != null)
            {
                Vector3[] edges = new Vector3[4];
                row.GetWorldCorners(edges);
                float rowRight = Mathf.Max(edges[0].x, edges[2].x);
                rect.GetWorldCorners(edges);
                float nameRight = Mathf.Max(edges[0].x, edges[2].x);
                float room = Mathf.Abs(rect.parent.InverseTransformVector(new Vector3(rowRight - nameRight, 0f, 0f)).x) * Mathf.Sign(rowRight - nameRight) - SignGap;
                grow = Mathf.Min(grow, room / Mathf.Max(0.0001f, rect.localScale.x));
            }
            //Not past the "New" badge
            if (___sign != null && ___sign.activeInHierarchy && ___sign.transform is RectTransform sign && rect.parent != null)
            {
                Vector3[] corners = new Vector3[4];
                sign.GetWorldCorners(corners);
                float signLeft = corners[0].x;
                rect.GetWorldCorners(corners);
                float room = rect.parent.InverseTransformVector(new Vector3(signLeft - corners[2].x, 0f, 0f)).x - SignGap;
                grow = Mathf.Min(grow, room / Mathf.Max(0.0001f, rect.localScale.x));
            }
            if (grow > 0f)
            {
                rect.sizeDelta += new Vector2(grow, 0f);
                rect.anchoredPosition += new Vector2(grow * rect.pivot.x, 0f);
            }

            //Never more than two lines (long item names took three and ran into the count below): shrinks if needed
            TextUtil.ShrinkToLines(name, MaxLines, MinNameSize);
        }

        private const int MaxLines = 2;
        private const int MinNameSize = 12;
    }

    [HarmonyPatch(typeof(WGBookBtn), "UpdateWidget")]
    public class WGBookBtn_UpdateWidget_Hook
    {
        public static void Postfix(WGBookBtn __instance, ref WGText ___itemName, System.Collections.Generic.List<WGImage> ___difficultIcons)
        {
            Widen(__instance, TextUtil.TextOf(___itemName));
            //Long book names took 3 lines and ran into "Remaining Chapters", they shrink until they take at most 2
            TextUtil.ShrinkToLines(TextUtil.TextOf(___itemName), NameLines, NameMinSize);
            KeepVolumeTogether(TextUtil.TextOf(___itemName));
            AlignChapters(__instance, TextUtil.TextOf(___itemName), ___difficultIcons);

            //A button shown for the first time is not laid out yet ("Remaining Chapters" may not even have its English text),
            //so the move above is off and it jumped into place only on the next update. It is done again right before the
            //screen is drawn, after Unity has laid everything out.
            _Pending[__instance] = ___difficultIcons;
            if (!_Subscribed)
            {
                Canvas.willRenderCanvases += AlignPending;
                _Subscribed = true;
            }
        }

        /*
         * The name area ended well before the right edge of the row, names wrapped early ("Palm of Thirty Six / Heavenly
         * Spirits: Vol II"). It now reaches the row's right edge (less RowGap), its left edge stays.
         */
        private const float RowGap = 12f;
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _NameOriginal = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        private static void Widen(WGBookBtn button, Text name)
        {
            if (name == null || !(button.transform is RectTransform row) || name.rectTransform.parent == null)
            {
                return;
            }
            RectTransform rect = name.rectTransform;
            if (!_NameOriginal.TryGetValue(rect, out Vector2[] original))
            {
                TextUtil.Prune(_NameOriginal);
                original = new[] { rect.sizeDelta, rect.anchoredPosition };
                _NameOriginal[rect] = original;
            }
            rect.sizeDelta = original[0];
            rect.anchoredPosition = original[1];

            //Both right edges in the space of the name's parent
            Transform space = rect.parent;
            Vector3[] corners = new Vector3[4];
            row.GetWorldCorners(corners);
            float rowRight = Mathf.Max(space.InverseTransformPoint(corners[0]).x, space.InverseTransformPoint(corners[2]).x);
            rect.GetWorldCorners(corners);
            float nameRight = Mathf.Max(space.InverseTransformPoint(corners[0]).x, space.InverseTransformPoint(corners[2]).x);
            float grow = (rowRight - RowGap - nameRight) / Mathf.Max(0.0001f, Mathf.Abs(rect.localScale.x));
            //Only wider, and not more than the name area itself again (a row found far bigger is not the name's row)
            if (grow <= 0f || grow > rect.rect.width)
            {
                return;
            }
            rect.sizeDelta += new Vector2(grow, 0f);
            rect.anchoredPosition += new Vector2(grow * rect.pivot.x * Mathf.Abs(rect.localScale.x), 0f);
        }

        //"Vol" and its number on the same line ("Hundred Forms of Sands : Vol / I" broke between them)
        private static readonly System.Text.RegularExpressions.Regex _Volume = new System.Text.RegularExpressions.Regex(@"\bVol ([IVX]+)\b");

        private static void KeepVolumeTogether(Text name)
        {
            if (name != null && name.text != null && name.text.IndexOf("Vol ", System.StringComparison.Ordinal) >= 0)
            {
                name.text = _Volume.Replace(name.text, "Vol\u00A0$1");
            }
        }

        private static readonly System.Collections.Generic.Dictionary<WGBookBtn, System.Collections.Generic.List<WGImage>> _Pending = new System.Collections.Generic.Dictionary<WGBookBtn, System.Collections.Generic.List<WGImage>>();
        private static bool _Subscribed;
        private static readonly AccessTools.FieldRef<WGBookBtn, WGText> _ItemName = AccessTools.FieldRefAccess<WGBookBtn, WGText>("itemName");

        //Runs after Unity's own layout (it registered first), once for each button updated since the last frame
        private static void AlignPending()
        {
            if (_Pending.Count == 0)
            {
                return;
            }

            foreach (var pending in _Pending)
            {
                //A button destroyed in the meantime compares equal to null
                if (pending.Key != null)
                {
                    AlignChapters(pending.Key, TextUtil.TextOf(_ItemName(pending.Key)), pending.Value);
                }
            }
            _Pending.Clear();
        }

        private const int NameLines = 2;
        private const int NameMinSize = 14;

        /*
         * "Remaining Chapters" and the chapter icons after it started under the book cover, and the icons were tall enough to
         * reach into the book name above. They move right so "Remaining Chapters" starts where the book name starts,
         * and the icons get smaller.
         */
        private const string ChaptersId = "SecondaryInterface2207";
        private const float IconScale = 0.75f;

        //Position and scale each of them has in the prefab, so we always start from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<Transform, Vector3> _Scale = new System.Collections.Generic.Dictionary<Transform, Vector3>();

        //The "Remaining Chapters" label of each button, found once (the list reuses its buttons)
        private static readonly System.Collections.Generic.Dictionary<WGBookBtn, Text> _Label = new System.Collections.Generic.Dictionary<WGBookBtn, Text>();

        private static void AlignChapters(WGBookBtn button, Text name, System.Collections.Generic.List<WGImage> icons)
        {
            if (!_Label.TryGetValue(button, out Text label))
            {
                foreach (WGStringTable table in button.GetComponentsInChildren<WGStringTable>(true))
                {
                    if (TextUtil.StringId(table) == ChaptersId)
                    {
                        label = table.GetComponent<Text>();
                        break;
                    }
                }
                _Label[button] = label;
            }
            if (name == null || label == null || string.IsNullOrEmpty(name.text))
            {
                return;
            }

            var parts = new System.Collections.Generic.List<Transform> { label.transform };
            if (icons != null)
            {
                foreach (WGImage icon in icons)
                {
                    if (icon == null)
                    {
                        continue;
                    }
                    if (!_Scale.TryGetValue(icon.transform, out Vector3 scale))
                    {
                        scale = icon.transform.localScale;
                        _Scale[icon.transform] = scale;
                    }
                    icon.transform.localScale = scale * IconScale;
                    parts.Add(icon.transform);
                }
            }

            //What gets moved: each part, or the layout group holding it (a layout group puts its children back), never something inside another one that moves
            var movers = new System.Collections.Generic.List<RectTransform>();
            foreach (Transform part in parts)
            {
                Transform mover = part.parent != null && part.parent.GetComponent<LayoutGroup>() != null ? part.parent : part;
                if (mover is RectTransform rect && !movers.Contains(rect))
                {
                    movers.Add(rect);
                }
            }
            movers.RemoveAll(m => movers.Exists(other => other != m && m.IsChildOf(other)));

            foreach (RectTransform mover in movers)
            {
                if (!_Origin.TryGetValue(mover, out Vector2 origin))
                {
                    origin = mover.anchoredPosition;
                    _Origin[mover] = origin;
                }
                mover.anchoredPosition = origin;
            }

            TextUtil.VisibleX(name, out float nameLeft, out _);
            TextUtil.VisibleX(label, out float labelLeft, out _);
            float shift = nameLeft - labelLeft;
            if (shift <= 0f)
            {
                return;
            }
            foreach (RectTransform mover in movers)
            {
                mover.position += new Vector3(shift, 0f, 0f);
            }
        }
    }

    [HarmonyPatch(typeof(WGTraitBtn), "UpdateWidget")]
    public class WGTraitBtn_UpdateWidget_Hook
    {
        public static void Postfix(ref WGText ___itemName)
        {
            Text text = ___itemName.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(-120f, -20f);
        }
    }

    [HarmonyPatch(typeof(WGTitleValue), "TitleFormat")]
    public class WGTitleValue_TitleFormat_Hook
    {
        public static void Postfix(ref Text ___Title)
        {
            ___Title.font = TextUtil.UIFont;
            ___Title.fontSize = 24;
            TrainingTipArrow.Raise(___Title);
        }
    }

    /*
     * Combat screens (every screen whose class starts with UIBattle: the battle screen with the victory and failure conditions,
     * the skill bar, the battle status, the stance list, the item and gear popup, the result): their texts written in the game's
     * own font (Kaiu, which is the Windows font DFKai-SB) get the CombatFont from the config file, if one is set.
     * Done every time such a screen is shown, items added to its lists are copies of texts already changed.
     */
    [HarmonyPatch(typeof(UIForm), "Show")]
    public class UIForm_Show_CombatFont_Hook
    {
        public static void Postfix(UIForm __instance)
        {
            if (__instance == null || !__instance.GetType().Name.StartsWith("UIBattle", System.StringComparison.Ordinal))
            {
                return;
            }

            TextUtil.ReplaceGameFont(__instance, TextUtil.CombatFont);
        }
    }

    //The turn number of the battle screen kept the game's font, it gets the CombatFont directly every time the turn changes.
    //Turn 1 is shown before the first turn change, so also when the battle sets up its victory conditions.
    [HarmonyPatch]
    public class WgBattleRound_UpdateRound_Hook
    {
        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WgBattleRound), "UpdateRound");
            yield return AccessTools.Method(typeof(WgBattleRound), "UpdateCondition");
        }

        public static void Postfix(WgBattleRound __instance, Text ___text_current, Text ___text_win_lose, GameObject ___tipObj)
        {
            TextUtil.Prune(_Origin);
            TextUtil.Prune(_AreaOrigin);
            TextUtil.Prune(_Label);
            TextUtil.Prune(_Logo);
            TextUtil.Prune(_Band);

            Font font = TextUtil.CombatFont;
            if (font != null && ___text_current != null)
            {
                ___text_current.font = font;
            }

            Schedule(__instance, ___text_current, ___text_win_lose, ___tipObj);
        }

        private static void Schedule(WgBattleRound round, Text current, Text winLose, GameObject tip)
        {
            Replacer.Run(round, () =>
            {
                if (current != null)
                {
                    TurnFirst(round, current, winLose, tip);
                }
                if (winLose != null)
                {
                    CenterConditions(winLose);
                }
            });
        }

        private static readonly AccessTools.FieldRef<WgBattleRound, Text> _Current = AccessTools.FieldRefAccess<WgBattleRound, Text>("text_current");
        private static readonly AccessTools.FieldRef<WgBattleRound, Text> _WinLose = AccessTools.FieldRefAccess<WgBattleRound, Text>("text_win_lose");
        private static readonly AccessTools.FieldRef<WgBattleRound, GameObject> _Tip = AccessTools.FieldRefAccess<WgBattleRound, GameObject>("tipObj");

        //The round label turns from 回合 into "Turn" after the round is set up: placed again then (WGStringTable_UpdateString_Hook)
        internal static void LabelChanged(Text label)
        {
            //Every label of the game goes through here, only the round label is looked at further
            string written = label.text == null ? "" : label.text.Trim();
            if (!written.Equals("Turn", System.StringComparison.OrdinalIgnoreCase) && written != "回合")
            {
                return;
            }
            WgBattleRound round = label.GetComponentInParent<WgBattleRound>();
            if (round != null)
            {
                _Label.Remove(round);
                Schedule(round, _Current(round), _WinLose(round), _Tip(round));
            }
        }

        /*
         * The victory and failure conditions: a long text came close to the bottom of the dark area behind it. The area
         * grows downwards when the text needs it, keeping at least ConditionGap above and below the letters, and the
         * text is put in its middle so both gaps are the same.
         */
        private const float ConditionGap = 12f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _AreaOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        private static void CenterConditions(Text text)
        {
            if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text))
            {
                return;
            }
            RectTransform rect = text.rectTransform;
            Reset(rect);
            RectTransform area = WGTalentItem_UpdateWidget_Hook.BoxOf(text);
            if (area == rect || area.IsChildOf(rect))
            {
                return;
            }

            if (!_AreaOrigin.TryGetValue(area, out Vector2[] origin))
            {
                origin = new[] { area.sizeDelta, area.anchoredPosition };
                _AreaOrigin[area] = origin;
            }
            area.sizeDelta = origin[0];
            area.anchoredPosition = origin[1];

            Rect? ink = Ink(text);
            if (ink == null)
            {
                return;
            }
            Vector3[] corners = new Vector3[4];
            area.GetWorldCorners(corners);
            float gap = Mathf.Abs(area.TransformVector(new Vector3(0f, ConditionGap, 0f)).y);
            float missing = ink.Value.height + 2f * gap - (corners[1].y - corners[0].y);
            //A measure gone wrong would make a huge area: only a few lines more are ever needed
            if (missing > (corners[1].y - corners[0].y) * 2f)
            {
                return;
            }
            if (missing > 0f && area.parent != null && area.GetComponentInParent<LayoutGroup>() == null)
            {
                //Taller by what is missing, with the top edge where it was
                float local = Mathf.Abs(area.parent.InverseTransformVector(new Vector3(0f, missing, 0f)).y) / Mathf.Max(0.0001f, area.localScale.y);
                area.sizeDelta += new Vector2(0f, local);
                area.anchoredPosition -= new Vector2(0f, local * (1f - area.pivot.y) * area.localScale.y);
            }

            float? shift = WGTalentItem_UpdateWidget_Hook.ShiftToCenter(text, area);
            if (shift != null && Mathf.Abs(shift.Value) < area.rect.height)
            {
                rect.anchoredPosition += new Vector2(0f, shift.Value);
            }
        }

        /*
         * The round shows "1  Turn", the number before a bigger "Turn". It now reads "Turn 1": the label starts where the
         * number started, the number follows it after a space, and sits on the same baseline.
         */
        private const float SpaceWidth = 0.3f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        //Made when first used, not when the hook is set up
        private static TextGenerator _Gen;
        private static TextGenerator _Generator => _Gen ?? (_Gen = new TextGenerator());

        //"Turn" and the crossed swords of each round widget, looked for once
        private static readonly System.Collections.Generic.Dictionary<WgBattleRound, Text> _Label = new System.Collections.Generic.Dictionary<WgBattleRound, Text>();
        private static readonly System.Collections.Generic.Dictionary<WgBattleRound, Image> _Logo = new System.Collections.Generic.Dictionary<WgBattleRound, Image>();
        private static readonly System.Collections.Generic.Dictionary<WgBattleRound, RectTransform> _Band = new System.Collections.Generic.Dictionary<WgBattleRound, RectTransform>();

        private static void TurnFirst(WgBattleRound round, Text number, Text winLose, GameObject tip)
        {
            if (!_Label.TryGetValue(round, out Text label) || label == null || !label.isActiveAndEnabled)
            {
                label = LabelOf(round, number, winLose, tip);
                _Label[round] = label;
            }
            if (label == null || number.rectTransform.parent == null || label.rectTransform.parent == null)
            {
                Warn(round, "no label found next to the number");
                return;
            }
            if (LayoutLog.Enabled) LayoutLog.Once("turn label " + round.GetInstanceID(), "Turn label: " + LayoutLog.Describe(label) + " | number: " + LayoutLog.Describe(number));

            //"Turn" in the game's font is a little taller than its area (sized for 回合), it was cut and not shown
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            //The number as big as "Turn", allowed past its area which is sized for the smaller number
            number.resizeTextForBestFit = false;
            number.fontSize = label.fontSize;
            number.horizontalOverflow = HorizontalWrapMode.Overflow;
            number.verticalOverflow = VerticalWrapMode.Overflow;

            LayoutGroup layout = number.transform.parent.GetComponent<LayoutGroup>();
            if (layout != null && label.transform.parent == number.transform.parent)
            {
                //A layout places them side by side, putting the label first is enough
                if (label.transform.GetSiblingIndex() > number.transform.GetSiblingIndex())
                {
                    label.transform.SetSiblingIndex(number.transform.GetSiblingIndex());
                }
                if (LayoutLog.Enabled) LayoutLog.Once("turn layout " + round.GetInstanceID(), "Turn: placed by the layout " + layout.GetType().Name + ", only swapped");
                return;
            }

            Reset(number.rectTransform);
            Reset(label.rectTransform);
            Rect? numberInk = Ink(number);
            Rect? labelInk = Ink(label);
            float? numberBase = Baseline(number);
            float? labelBase = Baseline(label);
            if (numberInk == null || labelInk == null || numberBase == null || labelBase == null)
            {
                Warn(round, "nothing drawn when measured (label " + (labelInk != null) + ", number " + (numberInk != null) + ")");
                return;
            }

            float left = Mathf.Min(numberInk.Value.xMin, labelInk.Value.xMin);
            float space = Mathf.Abs(label.rectTransform.TransformVector(new Vector3(label.fontSize * SpaceWidth, 0f, 0f)).x);
            //"Turn 1" as a whole in the middle under the crossed swords above it, when they are found
            float width = labelInk.Value.width + space + numberInk.Value.width;
            if (!_Logo.TryGetValue(round, out Image logo) || logo == null || !logo.isActiveAndEnabled)
            {
                logo = LogoAbove(round, label, labelInk.Value, width);
                _Logo[round] = logo;
            }
            if (logo != null)
            {
                Vector3[] corners = new Vector3[4];
                logo.rectTransform.GetWorldCorners(corners);
                left = (corners[0].x + corners[2].x) / 2f - width / 2f;
            }

            //Top to bottom in the middle of the dark band behind it: the letters of "Turn", capitals' top to baseline
            float up = 0f;
            if (!_Band.TryGetValue(round, out RectTransform band) || band == null || !band.gameObject.activeInHierarchy)
            {
                band = WGTalentItem_UpdateWidget_Hook.BoxOf(label);
                Vector3[] edges = new Vector3[4];
                band.GetWorldCorners(edges);
                //The label's own area or a big background is not the band
                if (band == label.rectTransform || edges[1].y - edges[0].y > labelInk.Value.height * 3f)
                {
                    band = null;
                }
                _Band[round] = band;
            }
            float? letters = SkillBarNames.LettersMiddle(label);
            if (band != null && letters != null)
            {
                up = SkillBarNames.Middle(band) - letters.Value;
            }

            //A measure gone wrong (a font the measuring does not suit) would throw the texts far away: then the round is
            //left as the game shows it. The real moves are a few letters wide at most.
            float limit = labelInk.Value.height * 6f;
            float baseline = labelBase.Value - numberBase.Value;
            if (Mathf.Abs(up) > labelInk.Value.height)
            {
                up = 0f;
            }
            if (Mathf.Abs(baseline) > labelInk.Value.height)
            {
                baseline = 0f;
            }
            if (LayoutLog.Enabled) LayoutLog.Once("turn moves " + round.GetInstanceID() + " " + number.text, "Turn measures: label drawn " + LayoutLog.R(labelInk.Value) + ", number drawn "
                + LayoutLog.R(numberInk.Value) + ", baselines " + labelBase.Value.ToString("F1") + " / " + numberBase.Value.ToString("F1")
                + ", logo " + (logo != null ? logo.name : "none") + ", band " + (band != null ? band.name : "none") + ", up " + up.ToString("F1")
                + ", label moves " + (left - labelInk.Value.xMin).ToString("F1") + ", number moves " + (left + labelInk.Value.width + space - numberInk.Value.xMin).ToString("F1")
                + ", limit " + limit.ToString("F1"));
            if (labelInk.Value.width <= 0f || Mathf.Abs(left - labelInk.Value.xMin) > limit
                || Mathf.Abs(left + labelInk.Value.width + space - numberInk.Value.xMin) > limit)
            {
                Warn(round, "the moves measured were too big (" + label.text + ")");
                return;
            }

            Vector3 labelMove = new Vector3(left - labelInk.Value.xMin, up, 0f);
            Vector3 numberMove = new Vector3(left + labelInk.Value.width + space - numberInk.Value.xMin, baseline + up, 0f);
            //One inside the other moves along with it
            if (label.transform.IsChildOf(number.transform))
            {
                labelMove -= numberMove;
            }
            else if (number.transform.IsChildOf(label.transform))
            {
                numberMove -= labelMove;
            }
            Move(label, labelMove);
            Move(number, numberMove);
        }

        /*
         * The crossed swords: the closest small image above "Turn", on the round or next to it, not further to the side than
         * the width of "Turn 1". Small is up to about two times the height of "Turn" each way, the backgrounds are bigger.
         */
        private static Image LogoAbove(WgBattleRound round, Text label, Rect ink, float width)
        {
            float size = ink.height * 2.5f;
            float middle = ink.xMin + width / 2f;
            Vector3[] corners = new Vector3[4];
            Image best = null;
            float bestDistance = float.MaxValue;
            Transform root = round.transform.parent != null ? round.transform.parent : round.transform;
            foreach (Image image in root.GetComponentsInChildren<Image>())
            {
                if (label.transform.IsChildOf(image.transform))
                {
                    continue;
                }
                image.rectTransform.GetWorldCorners(corners);
                float w = corners[2].x - corners[0].x, h = corners[1].y - corners[0].y;
                float x = (corners[0].x + corners[2].x) / 2f;
                if (w > size || h > size || w <= 0f || corners[0].y < ink.yMax || Mathf.Abs(x - middle) > width)
                {
                    continue;
                }
                float distance = corners[0].y - ink.yMax;
                if (distance < bestDistance)
                {
                    best = image;
                    bestDistance = distance;
                }
            }
            return best;
        }

        //The "Turn" text: the closest other text of the round on the same height as the number, outside the conditions
        //Written once per round widget to the log (F9) when the round is left as the game shows it
        private static readonly System.Collections.Generic.HashSet<WgBattleRound> _Warned = new System.Collections.Generic.HashSet<WgBattleRound>();

        private static void Warn(WgBattleRound round, string why)
        {
            if (_Warned.Add(round))
            {
                Debug.LogWarning("[EnglishPatch] Turn number not moved: " + why);
                if (LayoutLog.Enabled) LayoutLog.Once("turn warn " + round.GetInstanceID(), "Turn not moved: " + why);
            }
        }

        //The text written "Turn" (the translation of the round label), else the closest text on the number's height
        private static Text LabelOf(WgBattleRound round, Text number, Text winLose, GameObject tip)
        {
            foreach (Text other in round.GetComponentsInChildren<Text>())
            {
                string written = other.text == null ? "" : other.text.Trim();
                if (other != number && (written.Equals("Turn", System.StringComparison.OrdinalIgnoreCase) || written == "回合"))
                {
                    return other;
                }
            }

            Vector3[] own = new Vector3[4];
            number.rectTransform.GetWorldCorners(own);
            Vector3[] corners = new Vector3[4];
            Text best = null;
            float bestDistance = float.MaxValue;
            foreach (Text other in round.GetComponentsInChildren<Text>())
            {
                if (other == number || other == winLose || (tip != null && other.transform.IsChildOf(tip.transform))
                    || string.IsNullOrEmpty(other.text) || other.text.Trim().Length < 2)
                {
                    continue;
                }
                other.rectTransform.GetWorldCorners(corners);
                if (corners[1].y <= own[0].y || corners[0].y >= own[1].y)
                {
                    continue;
                }
                float distance = Mathf.Abs((corners[0].x + corners[2].x) / 2f - (own[0].x + own[2].x) / 2f);
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static void Reset(RectTransform rect)
        {
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
        }

        private static void Move(Text text, Vector3 world)
        {
            RectTransform rect = text.rectTransform;
            Vector3 local = rect.parent.InverseTransformVector(world);
            rect.anchoredPosition += new Vector2(local.x, local.y);
        }

        //World box of the drawn letters
        private static Rect? Ink(Text text)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (UIVertex vert in verts)
            {
                min = Vector2.Min(min, vert.position);
                max = Vector2.Max(max, vert.position);
            }
            Vector3 a = rect.TransformPoint(min / text.pixelsPerUnit);
            Vector3 b = rect.TransformPoint(max / text.pixelsPerUnit);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        //World height of the line the text is written on: the bottom of an "H" laid out like it
        private static float? Baseline(Text text)
        {
            RectTransform rect = text.rectTransform;
            TextGenerationSettings settings = TextUtil.Measuring(text, rect.rect.size);
            settings.richText = false;
            _Generator.Populate("H", settings);
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count < 4 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            float bottom = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                bottom = Mathf.Min(bottom, verts[i].position.y);
            }
            return rect.TransformPoint(new Vector3(0f, bottom / text.pixelsPerUnit, 0f)).y;
        }
    }

    /*
     * The bonus tip on the main life screen ("Workout bonus!", the herbal bath buffs left, the effects enhanced): the game
     * sizes the dark box behind it for Chinese (520 wide), the English lines ran past its right edge and below it, and broke
     * inside words (the game's non-breaking spaces). The text wraps at spaces, ends Padding inside the box's right edge,
     * and shrinks (its size tags with it) until its last line ends Padding above the box's bottom.
     */
    [HarmonyPatch(typeof(UINurturance), "SetAddEffectBack")]
    public class UINurturance_SetAddEffectBack_Hook
    {
        private const float Padding = 12f;
        private const float MinScale = 0.6f;
        //The text starts this much further left in the box
        private const float ShiftLeft = 15f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _OffsetMax = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Position = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        public static void Postfix(RectTransform ___AddEffectBack, Text ___AddEffectTip)
        {
            Text text = ___AddEffectTip;
            if (text == null || ___AddEffectBack == null || string.IsNullOrEmpty(text.text) || !___AddEffectBack.gameObject.activeSelf)
            {
                return;
            }

            //From the prefab's area every time, the tip is shown again and again
            RectTransform rect = text.rectTransform;
            if (!_OffsetMax.TryGetValue(rect, out Vector2 offsetMax))
            {
                offsetMax = rect.offsetMax;
                _OffsetMax[rect] = offsetMax;
            }
            rect.offsetMax = offsetMax;
            if (!_Position.TryGetValue(rect, out Vector2 position))
            {
                position = rect.anchoredPosition;
                _Position[rect] = position;
            }
            rect.anchoredPosition = position - new Vector2(ShiftLeft, 0f);
            offsetMax = rect.offsetMax;

            string original = text.text.Replace('\u00A0', ' ');
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = false;

            Vector3[] box = new Vector3[4];
            ___AddEffectBack.GetWorldCorners(box);
            Vector3[] own = new Vector3[4];
            rect.GetWorldCorners(own);
            float pad = Mathf.Abs(rect.TransformVector(new Vector3(Padding, 0f, 0f)).x);

            //Right edge inside the box
            float over = own[2].x - (box[2].x - pad);
            if (over > 0f && rect.parent != null)
            {
                float local = Mathf.Abs(rect.parent.InverseTransformVector(new Vector3(over, 0f, 0f)).x);
                rect.offsetMax = new Vector2(offsetMax.x - local, offsetMax.y);
            }

            //Down to the box's bottom, from where the text starts (its top, the tip is aligned to the top)
            rect.GetWorldCorners(own);
            float room = own[1].y - (box[0].y + pad);
            if (!_Size.TryGetValue(text, out int size))
            {
                size = text.fontSize;
                _Size[text] = size;
            }
            for (float scale = 1f; ; scale -= 0.05f)
            {
                text.text = scale < 1f ? TextUtil.ScaleSizeTags(original, scale) : original;
                text.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * scale));
                float height = Mathf.Abs(rect.TransformVector(new Vector3(0f, text.preferredHeight, 0f)).y);
                if (height <= room || scale - 0.05f < MinScale)
                {
                    break;
                }
            }
        }

        private static readonly System.Collections.Generic.Dictionary<Text, int> _Size = new System.Collections.Generic.Dictionary<Text, int>();
    }

    /*
     * Screens that still had texts in the game's own font get the English font from the config file (Font, Arial by default)
     * like the rest of the English texts. Done every time the screen is shown.
     *  - UINurturance, the main life screen: energy text, "Martial arts training and reading bonus!", the stats total
     *  - UIDate, the date next to it: "Year 2 Month 6", "Mid"
     *  - UIRelationship, a character in Socialize: the relationship levels and their abilities, the hobbies
     *    (also done when another character is picked, see UIRelationship_UpdateRelationship_Hook)
     */
    [HarmonyPatch(typeof(UIForm), "Show")]
    public class UIForm_Show_LifeFont_Hook
    {
        private static readonly System.Collections.Generic.HashSet<string> _Forms = new System.Collections.Generic.HashSet<string>
        {
            "UINurturance", "UIDate", "UIRelationship"
        };

        public static void Postfix(UIForm __instance)
        {
            if (__instance == null || !_Forms.Contains(__instance.GetType().Name))
            {
                return;
            }

            TextUtil.ReplaceGameFont(__instance, TextUtil.UIFont);
        }
    }

    /*
     * The "Info", "Criteria" and "Effect" titles of the gear selection popup and of the item and gear popup in combat:
     * "Criteria" (24-25 in the prefab) ran into the diamonds at both ends of its banner. All three get the same smaller size.
     * These popups have two copies of the panel (StaticCanvas/PropsBG draws the visible titles, DynamicCanvas/PropsIntroduction
     * has hidden ones), both are changed. Done once per popup, nothing sets the size back.
     */
    internal static class PopupTitles
    {
        private const int Size = 19;

        //String ids of the "Info", "Criteria" and "Effect" titles
        private static readonly System.Collections.Generic.HashSet<string> _Ids = new System.Collections.Generic.HashSet<string>
        {
            "General_Introduction", "General_Condition", "General_PassCondition", "General_Effect"
        };

        private static readonly string[] _Paths =
        {
            "StaticCanvas/PropsBG/Description/Title", "StaticCanvas/PropsBG/ConditionDescription/Title", "StaticCanvas/PropsBG/EffectDescription/Title",
            "DynamicCanvas/PropsIntroduction/Description/Title", "DynamicCanvas/PropsIntroduction/ConditionDescription/Title", "DynamicCanvas/PropsIntroduction/EffectDescription/Title",
        };

        private static readonly System.Collections.Generic.HashSet<UIForm> _Done = new System.Collections.Generic.HashSet<UIForm>();

        public static void SetSize(UIForm popup)
        {
            if (popup == null || !_Done.Add(popup))
            {
                return;
            }

            foreach (WGStringTable label in popup.GetComponentsInChildren<WGStringTable>(true))
            {
                Text text = label.GetComponent<Text>();
                if (text != null && _Ids.Contains(TextUtil.StringId(label)))
                {
                    text.fontSize = Size;
                }
            }
            foreach (string path in _Paths)
            {
                Text title = popup.transform.Find(path)?.GetComponent<Text>();
                if (title != null)
                {
                    title.fontSize = Size;
                }
            }
        }
    }

    /*
     * The item and gear popup in combat (UIBattleInventoryWindow). It has two copies of the panel: StaticCanvas/PropsBG draws
     * the paper, the "Info", "Criteria" and "Effect" banners and their titles, DynamicCanvas/PropsIntroduction draws the same
     * banners again with the texts in them. The banners are separate objects (not part of the paper picture), so they can move.
     * The Info text area is 75 high and its 3rd line already touched the "Criteria" banner, a 4th line ran over it.
     * The Criteria and Effect parts (both copies of the banners, with their texts) move down as far as the Info text needs,
     * the Effect text keeps its bottom edge. They move at most as far as the Effect text still fits, a longer Info shrinks.
     */
    internal static class BattlePopup
    {
        //Gap kept between the Info text and the "Criteria" banner below it, and between the "Effect" banner and its text
        private const float Gap = 8f;

        //Position and size each part has in the prefab, the popup is updated for every item
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        public static void Layout(WGPropsIntroduction popup, Text info, Text condition, Text effect, int minSize)
        {
            RectTransform panel = popup.transform as RectTransform;
            RectTransform conditionBanner = condition != null ? condition.transform.parent as RectTransform : null;
            RectTransform effectBanner = effect != null ? effect.transform.parent as RectTransform : null;
            if (panel == null || info == null || conditionBanner == null || effectBanner == null
                || conditionBanner.name != "ConditionDescription" || effectBanner.name != "EffectDescription"
                || conditionBanner.parent != panel || effectBanner.parent != panel)
            {
                return;
            }

            Transform window = TextUtil.OwningForm(popup)?.transform;
            RectTransform staticConditionBanner = window?.Find("StaticCanvas/PropsBG/ConditionDescription") as RectTransform;
            RectTransform staticEffectBanner = window?.Find("StaticCanvas/PropsBG/EffectDescription") as RectTransform;
            PopupTitles.SetSize(TextUtil.OwningForm(popup));

            foreach (RectTransform rect in new[] { info.rectTransform, conditionBanner, effectBanner, effect.rectTransform, staticConditionBanner, staticEffectBanner })
            {
                Restore(rect);
            }

            foreach (Text text in new[] { info, effect })
            {
                text.text = text.text?.Replace('\u00A0', ' ');
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }

            //How far down Criteria and Effect have to go for the whole Info text, and how far they can go
            float infoTop = Top(info.rectTransform, panel);
            float conditionTop = Top(conditionBanner, panel);
            float wanted = conditionTop - (infoTop - info.preferredHeight - Gap);
            float room = Bottom(effectBanner, panel) - Gap - Bottom(effect.rectTransform, panel) - effect.preferredHeight;
            float shift = Mathf.Clamp(wanted, 0f, Mathf.Max(0f, room));

            if (shift > 0f)
            {
                foreach (RectTransform banner in new[] { conditionBanner, effectBanner, staticConditionBanner, staticEffectBanner })
                {
                    if (banner != null)
                    {
                        banner.anchoredPosition -= new Vector2(0f, shift);
                    }
                }
                //The Effect text moved down with its banner, its bottom edge goes back up to where it was
                MoveBottom(effect.rectTransform, -shift);
            }

            //Info may use the room down to the gap above the (moved) "Criteria" banner
            float infoBottom = conditionTop - shift + Gap;
            MoveBottom(info.rectTransform, Bottom(info.rectTransform, panel) - infoBottom);
            if (info.preferredHeight > info.rectTransform.rect.height)
            {
                TextUtil.ShrinkToHeight(info, minSize);
            }
            if (effect.preferredHeight > effect.rectTransform.rect.height)
            {
                TextUtil.ShrinkToHeight(effect, minSize);
            }
        }

        private static void Restore(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }
            if (!_Original.TryGetValue(rect, out Vector2[] original))
            {
                original = new[] { rect.anchoredPosition, rect.sizeDelta };
                _Original[rect] = original;
            }
            rect.anchoredPosition = original[0];
            rect.sizeDelta = original[1];
        }

        //Moves the bottom edge of a rect down by down (up when negative), the top edge stays, whatever its pivot
        private static void MoveBottom(RectTransform rect, float down)
        {
            rect.sizeDelta += new Vector2(0f, down);
            rect.anchoredPosition -= new Vector2(0f, down * (1f - rect.pivot.y));
        }

        //Top and bottom edge of a rect, in the panel's space
        private static float Top(RectTransform rect, RectTransform panel)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return panel.InverseTransformPoint(corners[1]).y;
        }

        private static float Bottom(RectTransform rect, RectTransform panel)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return panel.InverseTransformPoint(corners[0]).y;
        }
    }

    /*
     * The tabs of the pawn shop (Weapon, Armor, Accessories, Medicine, Gift): the names were centered on the tab, so they ran
     * over their icon and into the slanted edge of the next tab. Each tab button (184 wide) holds an Icon (43 x 43) with the
     * name inside it (TabBtn_Weapon/Icon/Text). The icons get smaller (their left edge stays), and each name is centered in
     * the room between its icon and the slanted edge, shrinking only if it does not fit. Done when a name gets its English
     * text (WGStringTable.UpdateString), the shop is not laid out with English names before that.
     */
    internal static class PawnshopTabs
    {
        private const float IconScale = 0.8f;
        private const int MinSize = 12;
        //Gap between the icon and the name, in pixels at 1920x1080
        private const float Gap = 4f;

        //Name -> where its room ends, from the left edge of its tab button, before the slanted edge (pixels at 1920x1080,
        //measured on the pawn shop at the bottom of the letters, where the edge is furthest left; Weapon a little less)
        private static readonly System.Collections.Generic.Dictionary<string, float> _RoomRight = new System.Collections.Generic.Dictionary<string, float>
        {
            { "Weapon", 154f }, { "Armor", 149f }, { "Accessories", 156f }, { "Medicine", 153f }, { "Gift", 149f },
        };

        //Name -> (font sizes added after fitting, pixels moved to the left). The bag and bottle icons are drawn narrower than
        //their box, so these two names can come a little closer to them and be a little bigger.
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> _Adjust = new System.Collections.Generic.Dictionary<string, Vector2>
        {
            { "Accessories", new Vector2(2f, 4f) }, { "Medicine", new Vector2(2f, 4f) },
        };

        //Size, position and alignment each name has in the prefab
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<Text, TextAnchor> _Alignment = new System.Collections.Generic.Dictionary<Text, TextAnchor>();

        public static void Fit(Text text)
        {
            if (text == null || text.text == null || !_RoomRight.TryGetValue(text.text.Trim(), out float roomRight) || !(TextUtil.OwningForm(text) is UIPawnshop))
            {
                return;
            }
            RectTransform button = text.GetComponentInParent<WGTabBtn>()?.transform as RectTransform;
            Image icon = text.transform.parent != null ? text.transform.parent.GetComponent<Image>() : null;
            if (button == null || icon == null || icon.transform == button)
            {
                return;
            }

            //The icon first: the name sits inside it and moves with it
            IconSizer.Shrink(icon.rectTransform, IconScale);

            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
                _OriginalSize[text] = text.fontSize;
                _Alignment[text] = text.alignment;
            }
            rect.anchoredPosition = origin;
            text.fontSize = _OriginalSize[text];
            text.alignment = (TextAnchor)((int)_Alignment[text] / 3 * 3 + 1);
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            float pixel = button.lossyScale.x;
            Vector3[] corners = new Vector3[4];
            button.GetWorldCorners(corners);
            float right = corners[0].x + roomRight * pixel;
            icon.rectTransform.GetWorldCorners(corners);
            float left = corners[2].x + Gap * pixel;
            if (right <= left)
            {
                return;
            }

            float room = (right - left) / rect.lossyScale.x;
            while (text.fontSize > MinSize && TextUtil.LinesWidth(text, text.text) > room)
            {
                text.fontSize--;
            }
            float move = 0f;
            if (_Adjust.TryGetValue(text.text.Trim(), out Vector2 adjust))
            {
                text.fontSize = Mathf.Min(_OriginalSize[text], text.fontSize + (int)adjust.x);
                move = adjust.y * pixel;
            }
            float center = rect.TransformPoint(rect.rect.center).x;
            rect.position += new Vector3((left + right) / 2f - move - center, 0f, 0f);
        }
    }

    /*
     * Keeps a picture smaller than the game makes it, with its left edge where it was. Its size is changed, not its scale, so
     * what sits inside it (a tab name) keeps its own size. Applied again right before each frame is drawn, so an animation
     * that sets the picture's size or position does not undo it: a value the animation set becomes the new base, a value we
     * set ourselves is left alone (so nothing shrinks twice).
     */
    internal static class IconSizer
    {
        private class Entry
        {
            public float Scale;
            public Vector2 BaseSize;
            public Vector2 BasePosition;
            public Vector2 SetSize;
            public Vector2 SetPosition;
        }

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Entry> _Entries = new System.Collections.Generic.Dictionary<RectTransform, Entry>();
        private static bool _Subscribed;

        public static void Shrink(RectTransform icon, float scale)
        {
            if (!_Entries.TryGetValue(icon, out Entry entry))
            {
                entry = new Entry { Scale = scale, BaseSize = icon.sizeDelta, BasePosition = icon.anchoredPosition, SetSize = new Vector2(float.NaN, float.NaN) };
                _Entries[icon] = entry;
            }
            Apply(icon, entry);
            if (!_Subscribed)
            {
                Canvas.willRenderCanvases += ApplyAll;
                _Subscribed = true;
            }
        }

        private static void Apply(RectTransform icon, Entry entry)
        {
            if (icon.sizeDelta != entry.SetSize)
            {
                entry.BaseSize = icon.sizeDelta;
            }
            if (icon.anchoredPosition != entry.SetPosition)
            {
                entry.BasePosition = icon.anchoredPosition;
            }
            entry.SetSize = entry.BaseSize * entry.Scale;
            //The size changes around the pivot, the left edge goes back to where it was
            entry.SetPosition = entry.BasePosition - new Vector2(icon.pivot.x * (entry.BaseSize.x - entry.SetSize.x), 0f);
            if (icon.sizeDelta != entry.SetSize)
            {
                icon.sizeDelta = entry.SetSize;
            }
            if (icon.anchoredPosition != entry.SetPosition)
            {
                icon.anchoredPosition = entry.SetPosition;
            }
        }

        private static readonly System.Collections.Generic.List<RectTransform> _Gone = new System.Collections.Generic.List<RectTransform>();

        private static void ApplyAll()
        {
            foreach (var pair in _Entries)
            {
                //An icon destroyed with its screen compares equal to null
                if (pair.Key == null)
                {
                    _Gone.Add(pair.Key);
                }
                else if (pair.Key.gameObject.activeInHierarchy)
                {
                    Apply(pair.Key, pair.Value);
                }
            }
            foreach (RectTransform gone in _Gone)
            {
                _Entries.Remove(gone);
            }
            _Gone.Clear();
        }
    }

    /*
     * Refine Medicine: the master's speech bubble broke in the middle of words ("It / 's a hassle", the game turns the spaces
     * into non-breaking spaces). It wraps at spaces again and shrinks if it would not fit the bubble.
     * The text area was nearer the bubble's left side: it is placed with the same gap on both sides, and the lines are
     * justified, so both margins are straight.
     */
    [HarmonyPatch(typeof(WGElixirIntroduction), "UpdateWidget", new[] { typeof(ElixirIntroductionInfo) })]
    public class WGElixirIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;

        //The text area of the prefab (left and right edges), the bubble is shown again and again
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2[]> _Original = new System.Collections.Generic.Dictionary<RectTransform, Vector2[]>();

        public static void Postfix(WGElixirIntroduction __instance, WGText ___talk)
        {
            LowerEffectHeading(__instance);
            Text text = TextUtil.TextOf(___talk);
            if (text == null)
            {
                return;
            }
            SpanBubble(text);
            TextUtil.ShrinkToBox(text, MinSize);
            //Justified lines need the text aligned to the left (same height in the bubble as before)
            text.alignment = (TextAnchor)((int)text.alignment / 3 * 3);
            TextUtil.Justify(text);
        }

        /*
         * The bubble's edges on a 1920x1080 screen (left 1283, right 1708), the text kept Gap inside both. Measured on the
         * screen rather than from the game's text area, whose edges changed from one message to the next. The area gets
         * that width and its pivot is moved so its edges land there at the position the game gives it.
         */
        private const float BubbleLeft = 1283f, BubbleRight = 1708f, Gap = 32f, ReferenceWidth = 1920f;

        //The "Effect" heading sat close under the info text, it moves down EffectLower (from its place in the prefab)
        private const float EffectLower = 8f;
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _HeadingOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static Text _Effect;

        private static void LowerEffectHeading(WGElixirIntroduction widget)
        {
            if (_Effect == null)
            {
                foreach (Text label in widget.GetComponentsInChildren<Text>(true))
                {
                    if (label.text != null && label.text.Trim() == "Effect")
                    {
                        _Effect = label;
                        break;
                    }
                }
                if (_Effect == null)
                {
                    return;
                }
            }
            RectTransform rect = _Effect.rectTransform;
            if (!_HeadingOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _HeadingOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin - new Vector2(0f, EffectLower);
        }

        private static void SpanBubble(Text text)
        {
            RectTransform rect = text.rectTransform;
            if (!_Original.TryGetValue(rect, out Vector2[] original))
            {
                original = new[] { rect.sizeDelta, rect.pivot };
                _Original[rect] = original;
            }
            rect.sizeDelta = original[0];
            rect.pivot = original[1];

            Canvas canvas = text.canvas != null ? text.canvas.rootCanvas : null;
            Transform space = rect.parent;
            if (canvas == null || space == null)
            {
                return;
            }
            //Screen points of the wanted edges, as world positions along the canvas
            Vector3[] screen = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(screen);
            float Along(float x) => Mathf.Lerp(screen[0].x, screen[2].x, x / ReferenceWidth);
            float left = space.InverseTransformPoint(new Vector3(Along(BubbleLeft + Gap), rect.position.y, rect.position.z)).x;
            float right = space.InverseTransformPoint(new Vector3(Along(BubbleRight - Gap), rect.position.y, rect.position.z)).x;
            if (right <= left)
            {
                return;
            }

            //Where the pivot is in the parent, then a width and pivot that put the edges at left and right from there
            float scale = Mathf.Abs(rect.localScale.x) > 0.0001f ? Mathf.Abs(rect.localScale.x) : 1f;
            float pivotX = space.InverseTransformPoint(rect.position).x;
            float width = (right - left) / scale;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x + width - rect.rect.width, rect.sizeDelta.y);
            rect.pivot = new Vector2((pivotX - left) / (right - left), rect.pivot.y);
        }
    }

    /*
     * The tabs of the save/load screen (Local storage, Cloud Save, Auto Save, Combat Autosave): the names are centered on the
     * tab, so they ran over the icon on the left and into the slanted edge of the next tab. Each name moves right, next to its
     * icon, and is fitted to the room between the icon and the slanted edge: it goes on two lines if one is too wide, and only
     * shrinks if two lines are still too wide. Room and move are measured at 1920x1080, the first tab is shaped differently.
     */
    internal static class SaveLoadTab
    {
        private const int MinSize = 14;

        //Tab name id -> (move to the right, room between the icon and the slanted edge)
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> _Tabs = new System.Collections.Generic.Dictionary<string, Vector2>
        {
            { "SecondaryInterface2117", new Vector2(3f, 115f) },  //Local storage
            { "SecondaryInterface2103", new Vector2(35f, 145f) }, //Cloud Save
            { "SecondaryInterface2104", new Vector2(35f, 145f) }, //Auto Save
            { "SecondaryInterface2118", new Vector2(35f, 145f) }, //Combat Autosave
        };

        //Size and position each name has in the prefab
        private static readonly System.Collections.Generic.Dictionary<Text, int> _OriginalSize = new System.Collections.Generic.Dictionary<Text, int>();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        public static void Fit(Text text, string id)
        {
            if (text == null || string.IsNullOrEmpty(text.text) || !_Tabs.TryGetValue(id, out Vector2 tab) || !(TextUtil.OwningForm(text) is UISaveLoad))
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(tab.x, 0f);

            if (!_OriginalSize.TryGetValue(text, out int size))
            {
                size = text.fontSize;
                _OriginalSize[text] = size;
            }
            text.fontSize = size;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.MiddleCenter;

            string oneLine = text.text.Replace('\n', ' ').Replace('\u00A0', ' ').Trim();
            text.text = TextUtil.LinesWidth(text, oneLine) <= tab.y ? oneLine : TextUtil.TwoLines(text, oneLine);
            while (text.fontSize > MinSize && TextUtil.LinesWidth(text, text.text) > tab.y)
            {
                text.fontSize--;
            }

            UIForm form = TextUtil.OwningForm(text);
            Replacer.Run(form, () => SameGap(form));
        }

        /*
         * The names are centered in their room, so a short name ended far from its icon ("Cloud Save", "Auto Save").
         * Each name is moved so the gap between its icon and its drawn letters is the gap "Local storage" has.
         */
        private const string LocalStorage = "SecondaryInterface2117";

        private static readonly AccessTools.FieldRef<WGStringTable, string> _Id = AccessTools.FieldRefAccess<WGStringTable, string>("id");

        private static void SameGap(UIForm form)
        {
            if (form == null || !form.isActiveAndEnabled)
            {
                return;
            }
            Text local = null;
            var others = new System.Collections.Generic.List<Text>();
            foreach (WGStringTable label in form.GetComponentsInChildren<WGStringTable>())
            {
                string id = _Id(label);
                Text text = label.GetComponent<Text>();
                if (id == null || text == null || !_Tabs.TryGetValue(id, out Vector2 tab) || !_Origin.TryGetValue(text.rectTransform, out Vector2 origin))
                {
                    continue;
                }
                //From where Fit put it, so placing again does not add up
                text.rectTransform.anchoredPosition = origin + new Vector2(tab.x, 0f);
                if (id == LocalStorage)
                {
                    local = text;
                }
                else
                {
                    others.Add(text);
                }
            }

            float? gap = local != null ? GapToIcon(local) : null;
            if (gap == null)
            {
                return;
            }
            foreach (Text text in others)
            {
                float? own = GapToIcon(text);
                RectTransform rect = text.rectTransform;
                //A gap that cannot be right (no icon found, or one far away) leaves the name where Fit put it
                if (own == null || rect.parent == null || Mathf.Abs(own.Value - gap.Value) > text.fontSize * 4f)
                {
                    continue;
                }
                rect.anchoredPosition += new Vector2(rect.parent.InverseTransformVector(new Vector3(gap.Value - own.Value, 0f, 0f)).x, 0f);
            }
        }

        private static readonly TextGenerator _Generator = new TextGenerator();

        //World distance from the right edge of the tab's icon to the left of the name's drawn letters
        private static float? GapToIcon(Text text)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            if (_Generator.vertexCount == 0 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            float inkLeft = float.MaxValue;
            foreach (UIVertex vert in _Generator.verts)
            {
                inkLeft = Mathf.Min(inkLeft, vert.position.x);
            }
            inkLeft = rect.TransformPoint(new Vector3(inkLeft / text.pixelsPerUnit, 0f, 0f)).x;

            Image icon = IconOf(text, inkLeft);
            if (icon == null)
            {
                return null;
            }
            Vector3[] corners = new Vector3[4];
            icon.rectTransform.GetWorldCorners(corners);
            return inkLeft - corners[2].x;
        }

        //The small image left of the name on the same height: among the name's neighbours and its parent's neighbours
        private static Image IconOf(Text text, float inkLeft)
        {
            Vector3[] own = new Vector3[4];
            text.rectTransform.GetWorldCorners(own);
            float middle = (own[0].y + own[1].y) / 2f;
            float size = Mathf.Abs(text.rectTransform.TransformVector(new Vector3(0f, text.fontSize, 0f)).y) * 4f;
            Vector3[] corners = new Vector3[4];
            Image best = null;
            float bestRight = float.MinValue;
            Transform parent = text.transform.parent;
            for (int level = 0; level < 2 && parent != null; level++, parent = parent.parent)
            {
                foreach (Image image in parent.GetComponentsInChildren<Image>())
                {
                    image.rectTransform.GetWorldCorners(corners);
                    float width = corners[2].x - corners[0].x, height = corners[1].y - corners[0].y;
                    float centerX = (corners[0].x + corners[2].x) / 2f;
                    if (width > size || height > size || corners[0].y > middle || corners[1].y < middle || centerX >= inkLeft)
                    {
                        continue;
                    }
                    if (corners[2].x > bestRight)
                    {
                        best = image;
                        bestRight = corners[2].x;
                    }
                }
                if (best != null)
                {
                    return best;
                }
            }
            return null;
        }
    }

    /*
     * The training tip (hovering a skill or inner art in the training menu) shows "Lv. 1 → Lv. 2" and "Damage 214→230".
     * Arial draws → small and low, next to the numbers it looked offset. A bigger arrow sits in the middle of the line.
     */
    internal static class TrainingTipArrow
    {
        private const float Scale = 1.4f;

        public static void Raise(Text text)
        {
            if (text == null || string.IsNullOrEmpty(text.text) || text.text.IndexOf('→') < 0 || text.text.Contains("→</size>")
                || !(TextUtil.OwningForm(text) is UINurturance))
            {
                return;
            }

            //"Lv. 1 → Lv. 2" has a space on both sides of the arrow, "Damage 214→230" had none, both get one
            text.supportRichText = true;
            text.text = text.text.Replace(" → ", "→").Replace("→", " <size=" + Mathf.RoundToInt(text.fontSize * Scale) + ">→</size> ");
        }
    }

    [HarmonyPatch(typeof(WGTitleValue), "ValueFromat")]
    public class WGTitleValue_ValueFromat_Hook
    {
        public static void Postfix(ref Text ___Value)
        {
            ___Value.font = TextUtil.UIFont;
            ___Value.fontSize = 20;
            TrainingTipArrow.Raise(___Value);
        }
    }

    [HarmonyPatch(typeof(UIRelationship), "UpdateRelationship")]
    public class UIRelationship_UpdateRelationship_Hook
    {
        public static void Postfix(UIRelationship __instance, ref Text ___community_name, ref Text ___community_introduction, WGFavorability[] ___community_fav_level)
        {
            //Before fitting the abilities, so they are measured in the font they are shown in
            TextUtil.ReplaceGameFont(__instance, TextUtil.UIFont);
            RelationshipAbilities.Fit(___community_fav_level);

            ___community_name.font = TextUtil.UIFont;
            ___community_introduction.font = TextUtil.UIFont;

            //The name box is sized for 2-4 Chinese characters, "Nan Feihuang" wrapped at the space and only "Nan" was left. We keep it on one line and shrink it to fit.
            TextUtil.ShrinkToWidth(___community_name, 16);

            //The game turns every space into a non-breaking space (StringTool.Replace), so English lines broke in the middle of words. Turning them back allows wrapping at spaces.
            ___community_introduction.text = ___community_introduction.text.Replace(' ', ' ');

            //Longer bios got cut off at the bottom of the Info box, so we let the text shrink until it fits with a little room
            //above and below, then justify it. It sits in the middle of the box, so the room is the same at the top and bottom.
            TextUtil.ShrinkToBox(___community_introduction, 12, BioPadding);
            ___community_introduction.alignment = TextAnchor.MiddleLeft;
            TextUtil.Justify(___community_introduction);

            //The text sits closer to the right edge of the Info box than to the left one, moved left so both gaps are about the same
            RectTransform rect = ___community_introduction.rectTransform;
            if (!_BioOrigin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _BioOrigin[rect] = origin;
            }
            rect.anchoredPosition = origin + new Vector2(-BioShift, 0f);
        }

        private const float BioShift = 8f;
        private const float BioPadding = 4f;

        //Where the bio sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _BioOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
    }

    //Hobby labels on the relationship screen (Calligraphy, Alcohol...), the pills are sized for 2 Chinese characters
    [HarmonyPatch(typeof(WGFavItem), "SetFavItem")]
    public class WGFavItem_SetFavItem_Hook
    {
        public static void Postfix(Text ___item_name)
        {
            if (___item_name == null || !___item_name.gameObject.activeSelf || string.IsNullOrEmpty(___item_name.text))
            {
                return;
            }

            TextUtil.ShrinkToWidth(___item_name, 12);
        }
    }

    /*
     * Status tab traits: the description shrinks (down to 15) to fit its box, and is justified. It sat near the top of the
     * dark box behind it, with much less room below than above, so it is also centered in that box: the drawn letters of the
     * whole text, as Unity lays them out, have the same room above and below.
     */
    [HarmonyPatch(typeof(WGTalentItem), "UpdateWidget")]
    public class WGTalentItem_UpdateWidget_Hook
    {
        private const int MinSize = 15;
        //Room kept above and below the text inside the box
        private const float BoxPadding = 8f;

        //Where each description is in the prefab, the items are reused for every character
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly TextGenerator _Generator = new TextGenerator();

        public static void Postfix(WGTalentItem __instance)
        {
            System.Array talents = Traverse.Create(__instance).Field("talents").GetValue() as System.Array;
            if (talents == null)
            {
                return;
            }

            //The texts of each trait, read once here and not on every placing
            Text[] names = new Text[talents.Length];
            Text[] descriptions = new Text[talents.Length];
            for (int i = 0; i < talents.Length; i++)
            {
                Traverse talent = Traverse.Create(talents.GetValue(i));
                names[i] = TextUtil.TextOf(talent.Field("name").GetValue<WGText>());
                descriptions[i] = TextUtil.TextOf(talent.Field("description").GetValue<WGText>());
            }

            for (int i = 0; i < talents.Length; i++)
            {
                FitName(names[i]);

                Text text = descriptions[i];
                if (text == null || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                RectTransform rect = text.rectTransform;
                ResetPosition(text);
                RectTransform box = BoxOf(text);
                TextUtil.ShrinkToHeight(text, MinSize);
                float room = WorldHeight(box) - 2f * Mathf.Abs(box.TransformVector(new Vector3(0f, BoxPadding, 0f)).y);
                while (Mathf.Abs(rect.TransformVector(new Vector3(0f, text.preferredHeight, 0f)).y) > room && text.fontSize > MinSize)
                {
                    text.fontSize--;
                }

                //Middle of its own rect first, then the drawn letters are centered in the box
                text.alignment = TextAnchor.MiddleLeft;
                TextUtil.Justify(text);
            }

            //The tab slides in and its layout is only finished in the next frames, until then the places measured can be off
            Replacer.Run(__instance, () => Place(names, descriptions));
        }

        //Puts the names and descriptions where they belong, from where the game put them (can be done again at any time)
        private static void Place(Text[] names, Text[] descriptions)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Text text = descriptions[i];
                Text name = names[i];
                if (name != null && name.isActiveAndEnabled && !string.IsNullOrEmpty(name.text))
                {
                    ResetPosition(name);
                    PlaceName(name, text);
                }
                if (text != null && text.isActiveAndEnabled && !string.IsNullOrEmpty(text.text))
                {
                    ResetPosition(text);
                    CenterInBox(text, BoxOf(text));
                }
            }
        }

        //Back where the prefab has it, the items are reused for every character
        private static void ResetPosition(Text text)
        {
            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
        }

        /*
         * The trait name ("Gifted") sits after the big "Traits :" title, in a smaller font, and was lower than it.
         * It stays where it starts across, and is moved up or down so its letters have the same middle as the title's
         * (middle of the capitals and lower case bands, see WGNurturanceBtn_UpdateButton_Hook).
         */
        private static readonly System.Collections.Generic.Dictionary<Text, TextAnchor> _Alignment = new System.Collections.Generic.Dictionary<Text, TextAnchor>();

        private static void FitName(Text name)
        {
            if (name == null || string.IsNullOrEmpty(name.text))
            {
                return;
            }

            if (!_Alignment.TryGetValue(name, out TextAnchor alignment))
            {
                alignment = name.alignment;
                _Alignment[name] = alignment;
            }
            //Same side across as in the prefab, middle top to bottom
            switch ((int)alignment % 3)
            {
                case 0: name.alignment = TextAnchor.MiddleLeft; break;
                case 1: name.alignment = TextAnchor.MiddleCenter; break;
                default: name.alignment = TextAnchor.MiddleRight; break;
            }
            TextUtil.ShrinkToWidth(name, 12);
        }

        private static void PlaceName(Text name, Text description)
        {
            RectTransform rect = name.rectTransform;
            Text title = TitleOf(name, description);
            //A group placing both would move the title along with the name
            if (title == null || (LayoutOf(name) != null && LayoutOf(name) == LayoutOf(title)))
            {
                return;
            }
            ResetLayout(name);
            //On the same line as "Traits": the baselines lined up (the name is smaller, so their middles differ)
            if (rect.parent == null || !SkillBarNames.LettersSpan(name, out float _, out float nameBase)
                || !SkillBarNames.LettersSpan(title, out float _, out float titleBase))
            {
                return;
            }
            float shift = titleBase - nameBase;
            //A move bigger than the name itself is a measure gone wrong
            if (Mathf.Abs(shift) > Mathf.Abs(rect.TransformVector(new Vector3(0f, rect.rect.height, 0f)).y))
            {
                return;
            }
            Shift(name, rect.parent.InverseTransformVector(new Vector3(0f, shift, 0f)).y);
        }

        //The title found for each name, the items are reused so it is looked for once
        private static readonly System.Collections.Generic.Dictionary<Text, Text> _Titles = new System.Collections.Generic.Dictionary<Text, Text>();

        //The "Traits" title on the same row as the name: the biggest other text nearby that overlaps it top to bottom
        private static Text TitleOf(Text name, Text description)
        {
            if (_Titles.TryGetValue(name, out Text cached) && cached != null && cached.isActiveAndEnabled)
            {
                return cached;
            }
            Text found = FindTitle(name, description);
            if (found != null)
            {
                _Titles[name] = found;
            }
            return found;
        }

        private static Text FindTitle(Text name, Text description)
        {
            Vector3[] own = new Vector3[4];
            name.rectTransform.GetWorldCorners(own);
            Vector3[] corners = new Vector3[4];
            Text best = null;
            Transform root = name.transform.parent;
            if (root != null && root.parent != null && root.GetComponent<UIForm>() == null)
            {
                root = root.parent;
            }
            if (root == null)
            {
                return null;
            }
            foreach (Text other in root.GetComponentsInChildren<Text>())
            {
                if (other == name || other == description || string.IsNullOrEmpty(other.text) || other.text.Trim().Length < 2)
                {
                    continue;
                }
                other.rectTransform.GetWorldCorners(corners);
                if (corners[1].y <= own[0].y || corners[0].y >= own[1].y)
                {
                    continue;
                }
                if (best == null || other.fontSize > best.fontSize)
                {
                    best = other;
                }
            }
            return best;
        }

        /*
         * The dark box behind a text: the smallest image that spans the text across and holds its middle, among its parents
         * and their other children (the box can be an image drawn behind the text, not only one of its parents).
         * The text's own rect when there is none.
         */
        internal static RectTransform BoxOf(Text text)
        {
            Vector3[] own = new Vector3[4];
            text.rectTransform.GetWorldCorners(own);
            float middle = (own[0].y + own[1].y) / 2f;
            Vector3[] corners = new Vector3[4];
            RectTransform best = null;
            float bestArea = float.MaxValue;
            int levels = 0;
            for (Transform t = text.transform.parent; t != null && levels < 4 && t.GetComponent<UIForm>() == null; t = t.parent, levels++)
            {
                Consider(t);
                foreach (Transform child in t)
                {
                    if (child != text.transform && child.gameObject.activeInHierarchy)
                    {
                        Consider(child);
                    }
                }
            }
            return best ?? text.rectTransform;

            void Consider(Transform t)
            {
                RectTransform rect = t as RectTransform;
                Image image = t.GetComponent<Image>();
                if (rect == null || image == null || !image.enabled)
                {
                    return;
                }
                rect.GetWorldCorners(corners);
                float area = (corners[2].x - corners[0].x) * (corners[1].y - corners[0].y);
                if (corners[0].x <= own[0].x + 1f && corners[2].x >= own[2].x - 1f && corners[0].y < middle && corners[1].y > middle && area < bestArea)
                {
                    best = rect;
                    bestArea = area;
                }
            }
        }

        private static float WorldHeight(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[1].y - corners[0].y;
        }

        /*
         * Moves the text so the top of its first line and the bottom of its last line are as far from the box edges.
         * When a layout group places the text, a moved text is put back by the next layout pass, so the shift goes into the
         * group's padding instead (more at the top and less at the bottom moves it down by the same amount, whatever the
         * child alignment). The padding of each group is always taken from the prefab, the items are reused.
         */
        private static void CenterInBox(Text text, RectTransform box)
        {
            ResetLayout(text);
            float? shift = ShiftToCenter(text, box);
            if (shift != null)
            {
                Shift(text, shift.Value);
            }
        }

        //The layout group that places the text, if any
        private static LayoutGroup LayoutOf(Text text)
        {
            RectTransform rect = text.rectTransform;
            LayoutElement element = text.GetComponent<LayoutElement>();
            if (rect.parent == null || (element != null && element.ignoreLayout))
            {
                return null;
            }
            return rect.parent.GetComponent<LayoutGroup>();
        }

        //Puts back the prefab padding of the group placing the text, so it is measured where the game puts it
        private static void ResetLayout(Text text)
        {
            LayoutGroup layout = LayoutOf(text);
            if (layout == null)
            {
                return;
            }
            if (!_Padding.TryGetValue(layout, out RectOffset original))
            {
                original = new RectOffset(layout.padding.left, layout.padding.right, layout.padding.top, layout.padding.bottom);
                _Padding[layout] = original;
            }
            layout.padding = new RectOffset(original.left, original.right, original.top, original.bottom);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)text.rectTransform.parent);
        }

        //Moves the text up by this much (units of its parent), through the padding of its layout group if it has one
        private static void Shift(Text text, float shift)
        {
            LayoutGroup layout = LayoutOf(text);
            if (layout != null)
            {
                ResetLayout(text);
                int units = Mathf.RoundToInt(shift);
                RectOffset original = _Padding[layout];
                layout.padding = new RectOffset(original.left, original.right, original.top - units, original.bottom + units);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)text.rectTransform.parent);
            }
            else
            {
                text.rectTransform.anchoredPosition += new Vector2(0f, shift);
            }
        }

        private static readonly System.Collections.Generic.Dictionary<LayoutGroup, RectOffset> _Padding = new System.Collections.Generic.Dictionary<LayoutGroup, RectOffset>();

        //How far up the text has to move (in the units of its parent) so its drawn letters are in the middle of the box
        internal static float? ShiftToCenter(Text text, RectTransform box)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0 || text.pixelsPerUnit <= 0f || rect.parent == null)
            {
                return null;
            }

            float bottom = float.MaxValue, top = float.MinValue;
            foreach (UIVertex vert in verts)
            {
                bottom = Mathf.Min(bottom, vert.position.y);
                top = Mathf.Max(top, vert.position.y);
            }
            float inkMiddle = rect.TransformPoint(new Vector3(0f, (bottom + top) / 2f / text.pixelsPerUnit, 0f)).y;

            Vector3[] corners = new Vector3[4];
            box.GetWorldCorners(corners);
            float boxMiddle = (corners[0].y + corners[1].y) / 2f;

            return rect.parent.InverseTransformVector(new Vector3(0f, boxMiddle - inkMiddle, 0f)).y;
        }
    }

    /*
     * Does a placing again every frame for a moment after a widget is updated: screens slide in and finish their layout in
     * the next frames, until then the places measured can be off. The placing must give the same result when done again.
     */
    public class Replacer : MonoBehaviour
    {
        //Longest time a placing is repeated, for screens that slide in slowly
        private const float Duration = 1f;
        //Frames always placed after an update, the layout of the screen settles in those
        private const int SettleFrames = 3;
        //Frames without the widget moving or changing size after which the placing stops
        private const int StillFrames = 3;

        private System.Action _Place;
        private float _Until;
        private int _Frames;
        private int _Still;
        private Matrix4x4 _LastMatrix;
        private Vector2 _LastSize;

        public static void Run(Component widget, System.Action place)
        {
            if (widget == null || place == null)
            {
                return;
            }
            Replacer replacer = widget.GetComponent<Replacer>();
            if (replacer == null)
            {
                replacer = widget.gameObject.AddComponent<Replacer>();
            }
            replacer._Place = place;
            replacer._Until = Time.unscaledTime + Duration;
            replacer._Frames = 0;
            replacer._Still = 0;
            replacer.enabled = true;
            //Placed once right away too, so the first frame drawn is already right when the layout is ready
            replacer.Place();
        }

        /*
         * Placed on the first frames after the update, then only on frames where the widget moved or changed size
         * (sliding in, the screen size changing), and stopped once it has been still for a few frames.
         */
        private void LateUpdate()
        {
            if (_Place == null || Time.unscaledTime > _Until)
            {
                enabled = false;
                return;
            }

            Matrix4x4 matrix = transform.localToWorldMatrix;
            RectTransform rect = transform as RectTransform;
            Vector2 size = rect != null ? rect.rect.size : Vector2.zero;
            bool moved = matrix != _LastMatrix || size != _LastSize;
            _LastMatrix = matrix;
            _LastSize = size;

            _Frames++;
            if (_Frames > SettleFrames && !moved)
            {
                if (++_Still >= StillFrames)
                {
                    enabled = false;
                }
                return;
            }
            _Still = 0;
            Place();
        }

        private void Place()
        {
            try
            {
                _Place();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[EnglishPatch] Could not place " + name + ": " + e.Message);
                _Place = null;
                enabled = false;
            }
        }
    }

    /*
     * BepInEx/EnglishPatch_Layout.log: how texts were measured and moved (Turn label, key numbers), one line per text,
     * to find what goes wrong on another PC or with another font. Started empty every time the game starts.
     * Only when LayoutLog is on in the config file, the lines are not even put together otherwise.
     */
    internal static class LayoutLog
    {
        public static bool Enabled => EnglishPatch.LayoutLog;

        private static readonly string _Path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "EnglishPatch_Layout.log");
        private static readonly System.Collections.Generic.HashSet<string> _Written = new System.Collections.Generic.HashSet<string>();
        private static bool _Started;

        public static void Once(string key, string line)
        {
            if (!_Written.Add(key))
            {
                return;
            }
            try
            {
                if (!_Started)
                {
                    _Started = true;
                    System.IO.File.WriteAllText(_Path, "English Patch layout log, screen " + Screen.width + "x" + Screen.height + "\n");
                }
                System.IO.File.AppendAllText(_Path, System.DateTime.Now.ToString("HH:mm:ss ") + line + "\n");
            }
            catch (System.Exception)
            {
                //The log is only a help, a file that cannot be written is left alone
            }
        }

        public static string Describe(Text text)
        {
            if (text == null)
            {
                return "none";
            }
            RectTransform rect = text.rectTransform;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return "'" + text.text.Replace("\n", "\\n") + "' (" + text.name + ") font " + (text.font != null ? text.font.name : "none") + " size " + text.fontSize
                + " align " + text.alignment + " color " + text.color + " active " + text.isActiveAndEnabled
                + " area " + LayoutLog.V(rect.rect.size) + " at " + LayoutLog.V((corners[0] + corners[2]) / 2f) + " scale " + LayoutLog.V(rect.lossyScale);
        }

        public static string V(Vector2 v)
        {
            return "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ")";
        }

        public static string V(Vector3 v)
        {
            return "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ")";
        }

        public static string R(Rect r)
        {
            return "x " + r.xMin.ToString("F1") + ".." + r.xMax.ToString("F1") + " y " + r.yMin.ToString("F1") + ".." + r.yMax.ToString("F1");
        }
    }

    /*
     * Combat skill bar: the names under the buttons (skill names, Gear, Medicine, Standby, Party Standby) are put on one
     * line across the bar: the middle between the top of the capitals of the first line and the baseline of the last
     * line, so a one line name and a two line name have the same middle, and letters going below the line do not count.
     * The line is the average middle of the name areas.
     */
    /*
     * Names under icons that take one or two lines (battle status: inner art, gear and skills): a one line name sat at the
     * top of the room for two. Each name is put in the middle of its own area, measured from the top of the capitals of
     * its first line to the baseline of its last line, so one and two line names share the same middle.
     */
    internal static class LetterCentering
    {
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        public static void Center(Text text)
        {
            if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text))
            {
                return;
            }
            RectTransform rect = text.rectTransform;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                TextUtil.Prune(_Origin);
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;

            float? middle = SkillBarNames.LettersMiddle(text);
            if (middle == null || rect.parent == null)
            {
                return;
            }
            float shift = SkillBarNames.Middle(rect) - middle.Value;
            rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, shift, 0f)).y);
        }
    }

    //The battle status window: inner art, gear and skill names in the middle of their areas (see LetterCentering)
    [HarmonyPatch]
    public class UIBattleStatus_CenterNames_Hook
    {
        private static readonly AccessTools.FieldRef<WGPropsIntroduction, WGText> _PropsName = AccessTools.FieldRefAccess<WGPropsIntroduction, WGText>("propsName");
        private static readonly AccessTools.FieldRef<WGSkillBtn, Text> _SkillName = AccessTools.FieldRefAccess<WGSkillBtn, Text>("skill_name");

        //After the hooks that size the names (UIBattleStatus_UpdateView_Hook, UIBattleStatus_SetAllButton_Hook)
        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIBattleStatus), "UpdateView");
            yield return AccessTools.Method(typeof(UIBattleStatus), "set_all_button");
        }

        [HarmonyPriority(Priority.Low)]
        public static void Postfix(UIBattleStatus __instance)
        {
            var names = new System.Collections.Generic.List<Text>();
            foreach (WGPropsIntroduction slot in new[] { __instance.Mantra, __instance.Weapon, __instance.Cloth, __instance.Jewelry })
            {
                if (slot != null)
                {
                    names.Add(TextUtil.TextOf(_PropsName(slot)));
                }
            }
            foreach (WGSkillBtn button in __instance.Skill_Buttons ?? new WGSkillBtn[0])
            {
                names.Add(button == null ? null : _SkillName(button));
            }
            names.Add(__instance.Special_Skill_Button == null ? null : _SkillName(__instance.Special_Skill_Button));

            Text[] texts = names.ToArray();
            Replacer.Run(__instance, () =>
            {
                foreach (Text text in texts)
                {
                    LetterCentering.Center(text);
                }
            });
        }
    }

    /*
     * Puts a text's first line on the same line as a label next to it (their baselines lined up), always from where the
     * game put the text, so placing again does not add up. A move bigger than the label is a measure gone wrong.
     */
    internal static class SameLine
    {
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        //Remembers where the game puts the text, the first time (the game does not move these texts, a later snapshot
        //would include our own move), the placing always starts from there
        public static void Keep(Text text)
        {
            if (text != null && !_Origin.ContainsKey(text.rectTransform))
            {
                TextUtil.Prune(_Origin);
                _Origin[text.rectTransform] = text.rectTransform.anchoredPosition;
            }
        }

        public static void Align(Text text, Text label, float limitScale = 1.5f)
        {
            if (text == null || label == null || !text.isActiveAndEnabled || !label.isActiveAndEnabled
                || string.IsNullOrEmpty(text.text) || string.IsNullOrEmpty(label.text))
            {
                return;
            }
            RectTransform rect = text.rectTransform;
            if (_Origin.TryGetValue(rect, out Vector2 origin))
            {
                rect.anchoredPosition = origin;
            }
            if (rect.parent == null || !SkillBarNames.FirstBaseline(text, out float textBase) || !SkillBarNames.FirstBaseline(label, out float labelBase))
            {
                return;
            }
            float move = labelBase - textBase;
            float limit = Mathf.Abs(label.rectTransform.TransformVector(new Vector3(0f, label.fontSize * limitScale, 0f)).y);
            if (Mathf.Abs(move) > limit)
            {
                return;
            }
            rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, move, 0f)).y);
        }

        //The shown text under root reading the given words (tags and spacing left out), null when there is none
        public static Text Find(Transform root, string words)
        {
            string wanted = Plain(words);
            if (root == null || wanted.Length == 0)
            {
                return null;
            }
            foreach (Text text in root.GetComponentsInChildren<Text>())
            {
                if (Plain(text.text).Equals(wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }
            return null;
        }

        private static readonly System.Text.RegularExpressions.Regex _Tags = new System.Text.RegularExpressions.Regex("<[^>]*>");

        private static string Plain(string text)
        {
            return text == null ? "" : _Tags.Replace(text, "").Replace('\u00A0', ' ').Trim();
        }

        //The label left of a text: the closest text on its left whose area overlaps it top to bottom, under root
        public static Text LabelLeftOf(Text text, Transform root)
        {
            if (text == null || root == null)
            {
                return null;
            }
            Vector3[] own = new Vector3[4];
            text.rectTransform.GetWorldCorners(own);
            Vector3[] corners = new Vector3[4];
            Text best = null;
            float bestRight = float.MinValue;
            foreach (Text other in root.GetComponentsInChildren<Text>())
            {
                if (other == text || string.IsNullOrEmpty(other.text))
                {
                    continue;
                }
                other.rectTransform.GetWorldCorners(corners);
                float centerX = (corners[0].x + corners[2].x) / 2f;
                if (centerX >= (own[0].x + own[2].x) / 2f || corners[1].y <= own[0].y || corners[0].y >= own[1].y)
                {
                    continue;
                }
                if (corners[2].x > bestRight)
                {
                    best = other;
                    bestRight = corners[2].x;
                }
            }
            return best;
        }
    }

    internal static class SkillBarNames
    {
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static TextGenerator _Gen;
        private static TextGenerator _Generator => _Gen ?? (_Gen = new TextGenerator());

        private static readonly AccessTools.FieldRef<UIBattleUnitMenu, WGSkillBtn[]> _Buttons = AccessTools.FieldRefAccess<UIBattleUnitMenu, WGSkillBtn[]>("skill_buttons");
        private static readonly AccessTools.FieldRef<WGSkillBtn, Text> _SkillName = AccessTools.FieldRefAccess<WGSkillBtn, Text>("skill_name");

        //A placing for the bar as it is now: the names are looked for on its first run, then only placed again
        public static System.Action Placer(UIBattleUnitMenu menu, Text[] hotKeys)
        {
            System.Collections.Generic.List<Text> names = null;
            return () =>
            {
                if (menu == null || !menu.isActiveAndEnabled)
                {
                    return;
                }
                if (names == null || names.Count == 0 || names.Exists(text => text == null))
                {
                    names = Find(menu, hotKeys);
                }
                Place(names);
            };
        }

        //The names of the bar: the texts at the height of the first skill name
        private static System.Collections.Generic.List<Text> Find(UIBattleUnitMenu menu, Text[] hotKeys)
        {
            TextUtil.Prune(_Origin);
            var names = new System.Collections.Generic.List<Text>();
            Text first = null;
            foreach (WGSkillBtn button in _Buttons(menu) ?? new WGSkillBtn[0])
            {
                Text name = button == null ? null : _SkillName(button);
                if (name != null && name.isActiveAndEnabled)
                {
                    first = name;
                    break;
                }
            }
            if (first == null)
            {
                return names;
            }

            foreach (Text text in menu.GetComponentsInChildren<Text>())
            {
                if (System.Array.IndexOf(hotKeys, text) < 0 && text.text != null && text.text.Trim().Length >= 2)
                {
                    //Only names moved before go back, the other texts are left as the game has them
                    if (_Origin.TryGetValue(text.rectTransform, out Vector2 origin))
                    {
                        text.rectTransform.anchoredPosition = origin;
                    }
                    names.Add(text);
                }
            }
            float band = Middle(first.rectTransform);
            float bandHeight = Mathf.Abs(first.rectTransform.TransformVector(new Vector3(0f, first.rectTransform.rect.height, 0f)).y);
            names.RemoveAll(text => Mathf.Abs(Middle(text.rectTransform) - band) > bandHeight / 2f);
            return names;
        }

        private static void Place(System.Collections.Generic.List<Text> names)
        {
            if (names.Count == 0)
            {
                return;
            }
            foreach (Text text in names)
            {
                Reset(text.rectTransform);
            }

            float line = 0f;
            foreach (Text text in names)
            {
                line += Middle(text.rectTransform);
            }
            line /= names.Count;

            foreach (Text text in names)
            {
                float? middle = LettersMiddle(text);
                RectTransform rect = text.rectTransform;
                if (middle == null || rect.parent == null)
                {
                    continue;
                }
                rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, line - middle.Value, 0f)).y);
            }
        }

        private static void Reset(RectTransform rect)
        {
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
        }

        //World height of the middle of a rect
        internal static float Middle(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0].y + corners[1].y) / 2f;
        }

        //World height of the middle between the capitals' top of the first line and the baseline of the last one
        internal static float? LettersMiddle(Text text)
        {
            if (!LettersSpan(text, out float top, out float bottom))
            {
                return null;
            }
            return (top + bottom) / 2f;
        }

        //World heights of the top of the capitals of the first line and of the baseline of the last line
        internal static bool LettersSpan(Text text, out float worldTop, out float worldBottom)
        {
            return Lines(text, out worldTop, out worldBottom, out float _);
        }

        //World height of the baseline of a text's first line (the one to line up with a label next to it)
        internal static bool FirstBaseline(Text text, out float baseline)
        {
            return Lines(text, out float _, out float _, out baseline);
        }

        private static readonly System.Text.RegularExpressions.Regex _LeadingSize = new System.Text.RegularExpressions.Regex(@"^\s*(?:<color=[^>]*>\s*)?<size=(\d+(?:\.\d+)?)>");

        private static bool Lines(Text text, out float worldTop, out float worldBottom, out float firstBaseline)
        {
            worldTop = worldBottom = firstBaseline = 0f;
            RectTransform rect = text.rectTransform;
            if (text.pixelsPerUnit <= 0f)
            {
                return false;
            }

            //Where an "H" starts and ends below the top of its line, in the size the text starts with ("<size=30>Validity")
            TextGenerationSettings settings = TextUtil.Measuring(text, rect.rect.size);
            System.Text.RegularExpressions.Match lead = _LeadingSize.Match(text.text ?? "");
            settings.richText = lead.Success;
            _Generator.Populate(lead.Success ? "<size=" + lead.Groups[1].Value + ">H</size>" : "H", settings);
            if (_Generator.verts.Count < 4 || _Generator.lineCount < 1)
            {
                return false;
            }
            float capTop = float.MinValue, baseline = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                capTop = Mathf.Max(capTop, _Generator.verts[i].position.y);
                baseline = Mathf.Min(baseline, _Generator.verts[i].position.y);
            }
            float lineTop = _Generator.lines[0].topY;
            float capBelowTop = lineTop - capTop, baseBelowTop = lineTop - baseline;

            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            int count = _Generator.lineCount;
            if (count < 1)
            {
                return false;
            }
            float top = _Generator.lines[0].topY - capBelowTop;
            float bottom = _Generator.lines[count - 1].topY - baseBelowTop;
            worldTop = rect.TransformPoint(new Vector3(0f, top / text.pixelsPerUnit, 0f)).y;
            worldBottom = rect.TransformPoint(new Vector3(0f, bottom / text.pixelsPerUnit, 0f)).y;
            firstBaseline = rect.TransformPoint(new Vector3(0f, (_Generator.lines[0].topY - baseBelowTop) / text.pixelsPerUnit, 0f)).y;
            return true;
        }
    }

    /*
     * Combat skill bar: the key numbers (1 to 9) under the buttons sat off the middle of the dark badge behind them.
     * Each one is put in the middle of its badge (its drawn digit, both ways), and the badge is lighter (more see-through), it was a heavy black spot.
     */
    /*
     * Tooltip texts (the skill Effect in combat, buffs, traits): the row is made exactly as high as the text, and a line
     * of text has room for tall letters above it but little below, so the text sat low: a big gap under the heading
     * above it and almost none at the bottom of the tooltip. The row now keeps a set gap above the first line's letters
     * and a bigger one below the last line's.
     */
    [HarmonyPatch(typeof(WGTip), "SetTipType")]
    public class WGTip_SetTipType_Hook
    {
        private const float TopGap = 4f;
        private const float BottomGap = 14f;

        /*
         * The element wheel tip ("Element changed to Metal..."): the text started far from the left edge of the tip, past
         * its dark border, and was left aligned. It starts closer to the border, using the room, and is justified.
         */
        private const float ElementTipWider = 42f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, float> _Left = new System.Collections.Generic.Dictionary<RectTransform, float>();

        /*
         * Heading rows of a tip ("Min criteria NOT reached, slower learning" in the training tip) are one line high, a longer
         * English heading wrapped into a second line over the next row, and broke inside a word (non-breaking spaces).
         * It stays on one line and shrinks (its size tags with it) until it fits the row, never below MinTitleScale.
         */
        private const float MinTitleScale = 0.6f;
        private const float TitlePadding = 8f;

        //Below this a heading is not shrunk to stay on one line, it goes on two justified lines instead
        private const float MinOneLineScale = 0.85f;
        private static readonly System.Text.RegularExpressions.Regex _AnySize = new System.Text.RegularExpressions.Regex("</?size[^>]*>");

        private static void OneLineTitle(WGTitleValue row)
        {
            Text title = row.Title;
            if (title == null || !title.gameObject.activeSelf || string.IsNullOrEmpty(title.text))
            {
                return;
            }
            string original = title.text.Replace('\u00A0', ' ');
            title.text = original;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            float room = title.rectTransform.rect.width - 2f * TitlePadding;
            if (room <= 0f || TextUtil.LinesWidth(title, original) <= room)
            {
                return;
            }
            //The row sets the size every time, so the shrinking starts again from it
            int size = title.fontSize;
            for (float scale = 0.95f; scale >= MinOneLineScale; scale -= 0.05f)
            {
                title.text = TextUtil.ScaleSizeTags(original, scale);
                title.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * scale));
                if (TextUtil.LinesWidth(title, title.text) <= room)
                {
                    return;
                }
            }

            /*
             * Too long for one line ("Unlock special outing event to level up your friendship when the friendship bar is
             * full." ran far out of the tip): two lines at most, shrinking only if needed, justified, and the row made as
             * high as the two lines.
             */
            string plain = _AnySize.Replace(original, "");
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.alignment = (TextAnchor)((int)title.alignment / 3 * 3);
            title.text = plain;
            for (float scale = 1f; scale >= MinTitleScale; scale -= 0.05f)
            {
                title.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * scale));
                _Generator.Populate(plain, TextUtil.Measuring(title, title.rectTransform.rect.size));
                if (_Generator.lineCount <= 2)
                {
                    break;
                }
            }
            TextUtil.Justify(title);
            row.SetHihgt(Mathf.CeilToInt(title.preferredHeight + TitlePadding));
        }

        /*
         * The training tip (main life screen): the level change ("Lv. 1 → Lv. 1"), the damage change and the rows with only a
         * heading ("Middle of Month Bonus") sat to one side of the tip while its headings are centered. Their drawn letters,
         * name and value together, are put in the middle of the tip. Rows with a name and a value ("Energy -12") stay.
         */
        //Where the game put each text of the training tip rows, and the type of each row, kept when the row is set
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _GamePosition = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<WGTitleValue, WGTip.TipType> _RowType = new System.Collections.Generic.Dictionary<WGTitleValue, WGTip.TipType>();

        //The Socialize tip keeps the size 20 the English texts have elsewhere
        private const int SocialSize = 20;
        private const int SocialMinSize = 14;
        private static readonly System.Collections.Generic.Dictionary<Text, string> _GameText = new System.Collections.Generic.Dictionary<Text, string>();

        //A Socialize tip row from the game's text: size 20, from the left margin, wrapped lines justified, row as high as them
        private static void SocialRow(WGTitleValue row)
        {
            Text title = row.Title;
            if (title == null || !_GameText.TryGetValue(title, out string game) || string.IsNullOrEmpty(game))
            {
                return;
            }
            //Placed again every frame for a moment: a row already made from this text is left as it is
            if (_SocialDone.TryGetValue(title, out string[] done) && done[0] == game && done[1] == title.text)
            {
                return;
            }
            title.fontSize = SocialSize;
            title.alignment = (TextAnchor)((int)title.alignment / 3 * 3);
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.text = _AnySize.Replace(game, "").Replace('\u00A0', ' ');
            //Two lines at most: the tip's background does not grow, a third line ran out of it. Smaller until it fits.
            _Generator.Populate(title.text, TextUtil.Measuring(title, title.rectTransform.rect.size));
            int lines = _Generator.lineCount;
            while (lines > 2 && title.fontSize > SocialMinSize)
            {
                title.fontSize--;
                _Generator.Populate(title.text, TextUtil.Measuring(title, title.rectTransform.rect.size));
                lines = _Generator.lineCount;
            }
            TextUtil.Justify(title);
            if (lines > 1)
            {
                row.SetHihgt(Mathf.CeilToInt(title.preferredHeight + TitlePadding));
            }
            if (row.Value != null)
            {
                row.Value.fontSize = SocialSize;
            }
            _SocialDone[title] = new[] { game, title.text };
        }

        private static readonly System.Collections.Generic.Dictionary<Text, string[]> _SocialDone = new System.Collections.Generic.Dictionary<Text, string[]>();

        /*
         * Places a whole tip of the life screen from where the game put its texts, the first and last rows staying where
         * they are. Training tip: each row centered (CenterTrainingRow), rows evenly spaced by the tops of their letters.
         * Socialize tip: a paragraph from one left margin (SocialRow), its first line centered, rows evenly spaced by the
         * gaps between their letters.
         */
        //A number, maybe signed, maybe inside rich text tags ("<color=...>8</color>")
        private static readonly System.Text.RegularExpressions.Regex _Number = new System.Text.RegularExpressions.Regex(@"^(\s*(?:<[^>]+>\s*)*)([+-])?\s*(\d+)(\s*(?:<[^>]+>\s*)*)$");
        private static readonly System.Text.RegularExpressions.Regex _AnyTag = new System.Text.RegularExpressions.Regex("<[^>]*>");

        //Text without tags, with normal spaces, trimmed, for comparing (the game writes non-breaking spaces)
        private static string Plain(string text)
        {
            return text == null ? "" : _AnyTag.Replace(text, "").Replace('\u00A0', ' ').Trim();
        }

        //"8" becomes "+ 8", "-12" becomes "- 12"; 0 and texts that are not a plain number stay as they are
        private static void Signed(Text value)
        {
            if (value == null || !value.isActiveAndEnabled || string.IsNullOrEmpty(value.text))
            {
                return;
            }
            System.Text.RegularExpressions.Match m = _Number.Match(value.text.Replace('\u00A0', ' '));
            if (!m.Success || m.Groups[3].Value.TrimStart('0').Length == 0)
            {
                return;
            }
            string signed = m.Groups[1].Value + (m.Groups[2].Value == "-" ? "- " : "+ ") + m.Groups[3].Value + m.Groups[4].Value;
            if (value.text != signed)
            {
                value.text = signed;
            }
        }

        //Where the top of the last row's letters goes, in row heights above the bottom of the tip (as with a full tip)
        private const float TurnFromBottom = 1.1f;

        private static void PlaceTrainingTip(WGTip tip)
        {
            if (tip == null || !tip.isActiveAndEnabled)
            {
                return;
            }
            WGTitleValue[] all = tip.GetComponentsInChildren<WGTitleValue>();
            //The Socialize tip (friendship): a paragraph, every line from the same left margin, wrapped lines justified
            bool social = false;
            foreach (WGTitleValue row in all)
            {
                //Only shown lines: the rows are reused, a hidden one still holds the text of an earlier tip
                if (row.Title != null && row.Title.isActiveAndEnabled && row.Title.text != null
                    && row.Title.text.IndexOf("friend", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    social = true;
                    break;
                }
            }

            var rows = new System.Collections.Generic.List<WGTitleValue>();
            //The rows drawn below the "Activity Outcome" heading (by position, the order of the rows is not the order shown)
            string outcome = Plain(Heluo.Game.Data.Get<StringTable>("SecondaryInterface1001")?.Text);
            float outcomeY = float.NaN;
            foreach (WGTitleValue row in all)
            {
                if (outcome.Length > 0 && row.Title != null && row.Title.isActiveAndEnabled && Plain(row.Title.text) == outcome)
                {
                    outcomeY = row.transform.position.y;
                }
            }
            foreach (WGTitleValue row in all)
            {
                foreach (Text text in new[] { row.Title, row.Value })
                {
                    if (text != null && _GamePosition.TryGetValue(text.rectTransform, out Vector2 position))
                    {
                        text.rectTransform.anchoredPosition = position;
                    }
                }
                if (social)
                {
                    SocialRow(row);
                }
                else if (_RowType.TryGetValue(row, out WGTip.TipType type))
                {
                    //The gains and costs listed under "Activity Outcome" are written "+ 8" and "- 12"
                    if (!float.IsNaN(outcomeY) && row.transform.position.y < outcomeY && type == WGTip.TipType.TitleValue)
                    {
                        Signed(row.Value);
                    }
                    CenterTrainingRow(row, type);
                }
                rows.Add(row);
            }

            //Socialize: every line starts where the first one does ("Relationship Lv 4" started further left than the rest)
            if (social)
            {
                float start = float.NaN;
                foreach (WGTitleValue row in rows)
                {
                    Text title = row.Title;
                    float left = float.MaxValue, right = float.MinValue;
                    if (title == null || !title.isActiveAndEnabled || string.IsNullOrEmpty(title.text) || !InkX(title, ref left, ref right))
                    {
                        continue;
                    }
                    if (float.IsNaN(start))
                    {
                        //The other lines keep this start; the first line itself ("Relationship Lv 4...") goes in the middle
                        start = left;
                        Vector3[] edges = new Vector3[4];
                        ((RectTransform)row.transform).GetWorldCorners(edges);
                        float middle = (edges[0].x + edges[2].x) / 2f - (left + right) / 2f;
                        RectTransform first = title.rectTransform;
                        if (first.parent != null && Mathf.Abs(middle) < first.rect.width / 2f)
                        {
                            first.anchoredPosition += new Vector2(first.parent.InverseTransformVector(new Vector3(middle, 0f, 0f)).x, 0f);
                        }
                        continue;
                    }
                    RectTransform rect = title.rectTransform;
                    if (rect.parent != null && Mathf.Abs(start - left) < rect.rect.width / 4f)
                    {
                        rect.anchoredPosition += new Vector2(rect.parent.InverseTransformVector(new Vector3(start - left, 0f, 0f)).x, 0f);
                    }
                }
            }

            //Top and bottom of each row's letters
            var tops = new System.Collections.Generic.List<float>();
            var bottoms = new System.Collections.Generic.List<float>();
            var texts = new System.Collections.Generic.List<Text[]>();
            foreach (WGTitleValue row in rows)
            {
                float top = float.MinValue, bottom = float.MaxValue;
                var shown = new System.Collections.Generic.List<Text>();
                foreach (Text text in new[] { row.Title, row.Value })
                {
                    if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text))
                    {
                        continue;
                    }
                    //The training tip is spaced by the tops of its letters below, only the Socialize tip needs the spans
                    if (!social)
                    {
                        shown.Add(text);
                        top = bottom = 0f;
                    }
                    else if (SkillBarNames.LettersSpan(text, out float textTop, out float textBottom))
                    {
                        top = Mathf.Max(top, textTop);
                        bottom = Mathf.Min(bottom, textBottom);
                        shown.Add(text);
                    }
                }
                if (shown.Count > 0)
                {
                    tops.Add(top);
                    bottoms.Add(bottom);
                    texts.Add(shown.ToArray());
                }
            }
            int count = tops.Count;
            if (count < 3)
            {
                return;
            }

            /*
             * The training tip: every row is one line in the same size, so equal steps between the tops of their capitals and
             * digits give equal gaps. Measured on the drawn letters without the enlarged arrows, whose taller line put the
             * level and damage rows off when measured from the line.
             */
            if (!social)
            {
                var capTops = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float top = float.MinValue;
                    foreach (Text text in texts[i])
                    {
                        if (CapsTop(text, out float textTop))
                        {
                            top = Mathf.Max(top, textTop);
                        }
                    }
                    if (top == float.MinValue)
                    {
                        return;
                    }
                    capTops[i] = top;
                }
                /*
                 * The last row ("1 Turn required") sits at the bottom of the tip: the tip keeps its height, with few rows
                 * (Nap) the last one was left high up. Its letters go TurnFromBottom (in rows) above the tip's bottom.
                 */
                RectTransform tipRect = tip.transform as RectTransform;
                float last = capTops[count - 1];
                if (tipRect != null)
                {
                    Vector3[] edges = new Vector3[4];
                    tipRect.GetWorldCorners(edges);
                    float rowHeight = Mathf.Abs(rows[0].transform.TransformVector(new Vector3(0f, ((RectTransform)rows[0].transform).rect.height, 0f)).y);
                    float wanted = edges[0].y + rowHeight * TurnFromBottom;
                    //Only lower, and only within the tip
                    if (wanted < last && wanted > edges[0].y)
                    {
                        foreach (Text text in texts[count - 1])
                        {
                            RectTransform rect = text.rectTransform;
                            if (rect.parent != null)
                            {
                                rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, wanted - last, 0f)).y);
                            }
                        }
                    }
                }

                //The other rows evenly spaced between the first and the one before the last, as the game stacked them
                int end = count - 2;
                if (end < 2)
                {
                    return;
                }
                float step = (capTops[0] - capTops[end]) / end;
                if (step <= 0f)
                {
                    return;
                }
                for (int i = 1; i < end; i++)
                {
                    float move = capTops[0] - i * step - capTops[i];
                    foreach (Text text in texts[i])
                    {
                        RectTransform rect = text.rectTransform;
                        if (rect.parent != null)
                        {
                            rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, move, 0f)).y);
                        }
                    }
                }
                return;
            }
            float letters = 0f;
            for (int i = 0; i < count; i++)
            {
                letters += tops[i] - bottoms[i];
            }
            float gap = (tops[0] - bottoms[count - 1] - letters) / (count - 1);
            //A gap that cannot be right (rows out of order, a measure gone wrong) leaves the rows where they are
            if (gap <= 0f || gap > (tops[0] - bottoms[count - 1]))
            {
                return;
            }
            float wantedTop = tops[0];
            for (int i = 0; i < count; i++)
            {
                float move = wantedTop - tops[i];
                foreach (Text text in texts[i])
                {
                    RectTransform rect = text.rectTransform;
                    if (rect.parent != null)
                    {
                        rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, move, 0f)).y);
                    }
                }
                wantedTop -= (tops[i] - bottoms[i]) + gap;
            }
        }
        private static readonly System.Text.RegularExpressions.Regex _Italic = new System.Text.RegularExpressions.Regex("</?i>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        //Every text of the training tip is drawn in this size (see TrainingSize)
        private const int TitleSize = 22;

        /*
         * The training tip (main life screen) mixed sizes 20 to 36 (headings, names, values, lines with their own size tag).
         * Every text and size tag gets TitleSize; the arrows of the level and damage changes, drawn bigger on purpose,
         * keep that much bigger.
         */
        private static readonly System.Text.RegularExpressions.Regex _SizeTag = new System.Text.RegularExpressions.Regex(@"<size=(\d+(?:\.\d+)?)>(\s*→)?");

        private static void TrainingSize(Text text)
        {
            if (text == null || string.IsNullOrEmpty(text.text) || text.fontSize <= 0)
            {
                return;
            }
            float scale = (float)TitleSize / text.fontSize;
            text.text = _SizeTag.Replace(text.text, m => m.Groups[2].Success
                ? "<size=" + Mathf.Max(1, Mathf.RoundToInt(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * scale)) + ">" + m.Groups[2].Value
                : "<size=" + TitleSize + ">");
            text.fontSize = TitleSize;
        }

        private static void CenterTrainingRow(WGTitleValue row, WGTip.TipType type)
        {
            Text title = row.Title, value = row.Value;
            bool hasTitle = title != null && title.gameObject.activeSelf && !string.IsNullOrEmpty(title.text);
            bool hasValue = value != null && value.gameObject.activeSelf && !string.IsNullOrEmpty(value.text);
            bool centered = type == WGTip.TipType.LevelUP || type == WGTip.TipType.TitleImportantValue
                || (type == WGTip.TipType.TitleValue && hasTitle && !hasValue);
            if (!centered || (!hasTitle && !hasValue))
            {
                return;
            }

            //The level change in the same size as the damage line (a heading, TitleSize), its arrow with it
            if (type == WGTip.TipType.LevelUP && hasValue && value.fontSize > 0 && value.fontSize != TitleSize)
            {
                value.text = TextUtil.ScaleSizeTags(value.text, (float)TitleSize / value.fontSize);
                value.fontSize = TitleSize;
            }

            //"Str +" and its "7" stood at the two ends of the row: the number follows the name after a space, in the name's
            //size and on the name's baseline
            if (type == WGTip.TipType.TitleImportantValue && hasTitle && hasValue)
            {
                if (value.fontSize > 0 && value.fontSize != title.fontSize)
                {
                    value.text = TextUtil.ScaleSizeTags(value.text, (float)title.fontSize / value.fontSize);
                    value.fontSize = title.fontSize;
                }
                //The number was slanted (italic), it is upright like the name, and in the same font
                value.fontStyle = title.fontStyle;
                value.font = title.font;
                if (value.text.IndexOf("<i>", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    value.text = _Italic.Replace(value.text, "");
                }
                /*
                 * On the name's line: same font, size and style as the name, so given the same vertical box (anchors,
                 * pivot, height, position) and the same vertical alignment, Unity writes both on the same line. Measuring
                 * each and moving one gave a pixel or two of difference from row to row.
                 */
                RectTransform valueRect = value.rectTransform, titleRect = title.rectTransform;
                if (valueRect.parent == titleRect.parent)
                {
                    valueRect.anchorMin = new Vector2(valueRect.anchorMin.x, titleRect.anchorMin.y);
                    valueRect.anchorMax = new Vector2(valueRect.anchorMax.x, titleRect.anchorMax.y);
                    valueRect.pivot = new Vector2(valueRect.pivot.x, titleRect.pivot.y);
                    valueRect.sizeDelta = new Vector2(valueRect.sizeDelta.x, titleRect.sizeDelta.y);
                    valueRect.anchoredPosition = new Vector2(valueRect.anchoredPosition.x, titleRect.anchoredPosition.y);
                    value.alignment = (TextAnchor)((int)title.alignment / 3 * 3 + (int)value.alignment % 3);
                }

                float titleLeft = float.MaxValue, titleRight = float.MinValue, valueLeft = float.MaxValue, valueRight = float.MinValue;
                if (InkX(title, ref titleLeft, ref titleRight) && InkX(value, ref valueLeft, ref valueRight) && value.rectTransform.parent != null)
                {
                    float space = Mathf.Abs(title.rectTransform.TransformVector(new Vector3(title.fontSize * 0.3f, 0f, 0f)).x);
                    float move = titleRight + space - valueLeft;
                    value.rectTransform.anchoredPosition += new Vector2(value.rectTransform.parent.InverseTransformVector(new Vector3(move, 0f, 0f)).x, 0f);
                }
            }

            float left = float.MaxValue, right = float.MinValue;
            if (hasTitle && !InkX(title, ref left, ref right) || hasValue && !InkX(value, ref left, ref right))
            {
                return;
            }
            Vector3[] corners = new Vector3[4];
            ((RectTransform)row.transform).GetWorldCorners(corners);
            float shift = (corners[0].x + corners[2].x) / 2f - (left + right) / 2f;

            /*
             * Top to bottom too: the rows are as high as each other but their letters sat at different heights in them (the
             * damage is drawn bigger, the level's arrow is resized), so the gaps between those rows differed. The letters,
             * capitals' top to baseline, are put in the middle of the row, which makes the rows evenly spaced.
             */
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (Text text in new[] { hasTitle ? title : null, hasValue ? value : null })
            {
                if (text != null && SkillBarNames.LettersSpan(text, out float textTop, out float textBottom))
                {
                    top = Mathf.Max(top, textTop);
                    bottom = Mathf.Min(bottom, textBottom);
                }
            }
            float up = top > bottom ? (corners[0].y + corners[1].y) / 2f - (top + bottom) / 2f : 0f;
            //Within the row, a bigger move is a measure gone wrong
            if (Mathf.Abs(up) > corners[1].y - corners[0].y)
            {
                up = 0f;
            }

            //The game puts both texts back at their place every time the row is set, so the move does not add up
            foreach (Text text in new[] { hasTitle ? title : null, hasValue ? value : null })
            {
                RectTransform rect = text != null ? text.rectTransform : null;
                if (rect != null && rect.parent != null)
                {
                    Vector3 local = rect.parent.InverseTransformVector(new Vector3(shift, up, 0f));
                    rect.anchoredPosition += new Vector2(local.x, local.y);
                }
            }
        }

        //World height of the top of a text's drawn characters, the tallest one left out when the text has an enlarged arrow
        private static bool CapsTop(Text text, out float top)
        {
            top = 0f;
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count < 4 || text.pixelsPerUnit <= 0f)
            {
                return false;
            }
            var tops = new System.Collections.Generic.List<float>();
            float tallest = 0f;
            int tallestIndex = -1;
            for (int i = 0; i + 3 < verts.Count; i += 4)
            {
                float low = float.MaxValue, high = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    low = Mathf.Min(low, verts[i + k].position.y);
                    high = Mathf.Max(high, verts[i + k].position.y);
                }
                //Spaces and rich text tags draw nothing
                if (high - low < 0.5f)
                {
                    continue;
                }
                if (high - low > tallest)
                {
                    tallest = high - low;
                    tallestIndex = tops.Count;
                }
                tops.Add(high);
            }
            if (text.text.IndexOf('→') >= 0 && tallestIndex >= 0 && tops.Count > 1)
            {
                tops.RemoveAt(tallestIndex);
            }
            if (tops.Count == 0)
            {
                return false;
            }
            float max = float.MinValue;
            foreach (float t in tops)
            {
                max = Mathf.Max(max, t);
            }
            top = rect.TransformPoint(new Vector3(0f, max / text.pixelsPerUnit, 0f)).y;
            return true;
        }

        //Widens left..right (world) with the drawn letters of a text, false when nothing is drawn
        private static bool InkX(Text text, ref float left, ref float right)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            if (_Generator.vertexCount == 0 || text.pixelsPerUnit <= 0f)
            {
                return false;
            }
            float min = float.MaxValue, max = float.MinValue;
            foreach (UIVertex vert in _Generator.verts)
            {
                min = Mathf.Min(min, vert.position.x);
                max = Mathf.Max(max, vert.position.x);
            }
            left = Mathf.Min(left, rect.TransformPoint(new Vector3(min / text.pixelsPerUnit, 0f, 0f)).x);
            right = Mathf.Max(right, rect.TransformPoint(new Vector3(max / text.pixelsPerUnit, 0f, 0f)).x);
            return true;
        }

        private static void ElementTip(Text text, RectTransform rect)
        {
            if (!_Left.TryGetValue(rect, out float left))
            {
                left = rect.offsetMin.x;
                _Left[rect] = left;
            }
            rect.offsetMin = new Vector2(left - ElementTipWider, rect.offsetMin.y);
            TextUtil.Justify(text);
        }

        private static readonly TextGenerator _Generator = new TextGenerator();

        public static void Postfix(WGTitleValue titlevalue, WGTip.TipType type)
        {
            if (titlevalue != null && TextUtil.OwningForm(titlevalue) is UINurturance)
            {
                if (titlevalue.Title != null)
                {
                    _GameText[titlevalue.Title] = titlevalue.Title.text;
                }
                TrainingSize(titlevalue.Title);
                TrainingSize(titlevalue.Value);
            }
            if (titlevalue != null && (type == WGTip.TipType.Title || type == WGTip.TipType.BigTitle || type == WGTip.TipType.VeryBigTitle))
            {
                OneLineTitle(titlevalue);
            }
            if (titlevalue != null && TextUtil.OwningForm(titlevalue) is UINurturance)
            {
                /*
                 * The tip's layout gives the rows their width only after this, so they were placed for the wrong width
                 * and only came right when the tip was set again a few seconds later. They are placed again over the next
                 * frames, each time from where the game put the texts.
                 */
                Text title = titlevalue.Title, value = titlevalue.Value;
                if (title != null) _GamePosition[title.rectTransform] = title.rectTransform.anchoredPosition;
                if (value != null) _GamePosition[value.rectTransform] = value.rectTransform.anchoredPosition;
                _RowType[titlevalue] = type;
                //The row is still hidden when it is set (shown right after), GetComponentInParent skips hidden objects
                WGTip tip = null;
                for (Transform t = titlevalue.transform.parent; t != null && tip == null; t = t.parent)
                {
                    tip = t.GetComponent<WGTip>();
                }
                if (tip != null)
                {
                    Replacer.Run(tip, () => PlaceTrainingTip(tip));
                }
            }
            if (titlevalue == null || (type != WGTip.TipType.Context && type != WGTip.TipType.TraitContext))
            {
                return;
            }
            Text text = titlevalue.Title;
            if (text == null || string.IsNullOrEmpty(text.text) || text.pixelsPerUnit <= 0f)
            {
                return;
            }

            //The rows are reused: a justified text is set to not wrap (its lines are made by Justify), so the next text
            //would stay on one line. Wrapping first, so the text is measured and justified at the width of the row.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;

            //Words wrap at spaces, the game's non-breaking spaces made lines break inside words ("fro / m")
            if (text.text.IndexOf(' ') >= 0)
            {
                text.text = text.text.Replace(' ', ' ');
            }
            RectTransform rect = text.rectTransform;
            if (TextUtil.OwningForm(titlevalue) is UIAttributeList)
            {
                ElementTip(text, rect);
            }

            //Drawn from the top of a tall enough area, where its letters start and end below that top
            float width = rect.rect.width;
            TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(width, text.preferredHeight + 200f));
            settings.textAnchor = TextAnchor.UpperLeft;
            _Generator.Populate(text.text, settings);
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0)
            {
                return;
            }
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (UIVertex vert in verts)
            {
                bottom = Mathf.Min(bottom, vert.position.y);
                top = Mathf.Max(top, vert.position.y);
            }
            float areaTop = settings.generationExtents.y * (1f - rect.pivot.y) * text.pixelsPerUnit;
            float inkTop = (areaTop - top) / text.pixelsPerUnit;
            float inkHeight = (top - bottom) / text.pixelsPerUnit;

            //Row as high as the letters and the gaps, the text from the top of the row with its letters TopGap below it
            titlevalue.SetHihgt(Mathf.CeilToInt(TopGap + inkHeight + BottomGap));
            text.alignment = (TextAnchor)((int)text.alignment % 3);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)titlevalue.transform);

            Vector3[] row = new Vector3[4];
            ((RectTransform)titlevalue.transform).GetWorldCorners(row);
            Vector3[] own = new Vector3[4];
            rect.GetWorldCorners(own);
            //Where the text's top should be, in world units: TopGap below the row top, raised by the room above its letters
            float wanted = row[1].y - Mathf.Abs(rect.TransformVector(new Vector3(0f, TopGap - inkTop, 0f)).y) * Mathf.Sign(TopGap - inkTop);
            if (rect.parent != null)
            {
                rect.anchoredPosition += new Vector2(0f, rect.parent.InverseTransformVector(new Vector3(0f, wanted - own[1].y, 0f)).y);
            }
        }
    }

    [HarmonyPatch]
    public class UIBattle_SkillHotKeys_Hook
    {
        //Part of the prefab opacity the badges keep
        private const float BadgeAlpha = 0.8f;

        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<Image, float> _Alpha = new System.Collections.Generic.Dictionary<Image, float>();
        //Made when first used, not when the hook is set up
        private static TextGenerator _Gen;
        private static TextGenerator _Generator => _Gen ?? (_Gen = new TextGenerator());

        //The numbers are shown again when the skill bar opens or changes, and when the keys are refreshed
        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIBattle), "OpenMenu");
            yield return AccessTools.Method(typeof(UIBattle), "UpdateMenu");
            yield return AccessTools.Method(typeof(UIBattle), "RefreshKeyHint");
            yield return AccessTools.Method(typeof(UIBattle), "KeyHintShow");
        }

        public static void Postfix(UIBattle __instance, System.Collections.Generic.List<WGText> ___SkillHotKeys, UIBattleUnitMenu ___unit_menu)
        {
            TextUtil.Prune(_Origin);
            TextUtil.Prune(_Alpha);

            Text[] texts = ___SkillHotKeys == null ? new Text[0] : ___SkillHotKeys.ConvertAll(TextUtil.TextOf).ToArray();
            System.Action names = SkillBarNames.Placer(___unit_menu, texts);
            Replacer.Run(__instance, () =>
            {
                Place(texts);
                names();
            });
        }

        private static void Place(Text[] texts)
        {
            foreach (Text text in texts)
            {
                if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                //A line of some fonts is a little taller than the number's area, it was cut and the number not shown
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;

                RectTransform rect = text.rectTransform;
                if (!_Origin.TryGetValue(rect, out Vector2 origin))
                {
                    origin = rect.anchoredPosition;
                    _Origin[rect] = origin;
                }
                rect.anchoredPosition = origin;

                Image badge = BadgeOf(text);
                if (badge == null || rect.parent == null)
                {
                    continue;
                }
                if (!_Alpha.TryGetValue(badge, out float alpha))
                {
                    alpha = badge.color.a;
                    _Alpha[badge] = alpha;
                }
                Color color = badge.color;
                color.a = alpha * BadgeAlpha;
                badge.color = color;

                Vector3? ink = InkCenter(text);
                if (ink == null)
                {
                    if (LayoutLog.Enabled) LayoutLog.Once("key " + text.GetInstanceID(), "Key number '" + text.text + "': nothing drawn when measured, " + LayoutLog.Describe(text));
                    continue;
                }
                Vector3 middle = DrawnMiddle(badge);
                Vector3 shift = rect.parent.InverseTransformVector(middle - ink.Value);
                //The number is moved within its badge, a move bigger than the badge is a measure gone wrong
                Vector2 badgeSize = badge.rectTransform.rect.size;
                bool sane = Mathf.Abs(shift.x) <= badgeSize.x && Mathf.Abs(shift.y) <= badgeSize.y;
                if (LayoutLog.Enabled) LayoutLog.Once("key " + text.GetInstanceID(), "Key number '" + text.text + "': " + LayoutLog.Describe(text)
                    + ", drawn middle " + LayoutLog.V(ink.Value) + ", badge '" + badge.name + "' size " + LayoutLog.V(badgeSize)
                    + " middle " + LayoutLog.V(middle) + ", move " + LayoutLog.V(shift) + (sane ? "" : " TOO BIG, not moved"));
                if (sane)
                {
                    rect.anchoredPosition = origin + new Vector2(shift.x, shift.y);
                }
            }
        }

        /*
         * World middle of what the badge draws: an image can have a transparent margin around its picture (wider on one
         * side), then the middle of its area is not the middle of the diamond seen. The margin is taken off when known.
         */
        internal static Vector3 DrawnMiddle(Image badge)
        {
            RectTransform rect = badge.rectTransform;
            Rect area = rect.rect;
            Sprite sprite = badge.sprite;
            if (sprite != null && badge.type == Image.Type.Simple && !badge.preserveAspect && sprite.rect.width > 0f && sprite.rect.height > 0f)
            {
                Vector4 padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
                Vector2 size = sprite.rect.size;
                area = Rect.MinMaxRect(area.xMin + area.width * padding.x / size.x, area.yMin + area.height * padding.y / size.y,
                    area.xMax - area.width * padding.z / size.x, area.yMax - area.height * padding.w / size.y);
            }
            return rect.TransformPoint(area.center);
        }

        //The smallest image around the number, the number itself or one next to it, no more than a few times its size
        internal static Image BadgeOf(Text text)
        {
            Vector3[] own = new Vector3[4];
            text.rectTransform.GetWorldCorners(own);
            Vector3 middle = (own[0] + own[2]) / 2f;
            float size = Mathf.Abs(text.rectTransform.TransformVector(new Vector3(0f, text.fontSize, 0f)).y);
            Vector3[] corners = new Vector3[4];
            Image best = null;
            float bestArea = float.MaxValue;
            Transform parent = text.transform.parent;
            if (parent == null)
            {
                return null;
            }
            var candidates = new System.Collections.Generic.List<Transform> { parent };
            foreach (Transform child in parent)
            {
                candidates.Add(child);
            }
            foreach (Transform t in candidates)
            {
                Image image = t.GetComponent<Image>();
                if (image == null || !image.isActiveAndEnabled)
                {
                    continue;
                }
                image.rectTransform.GetWorldCorners(corners);
                float width = corners[2].x - corners[0].x, height = corners[1].y - corners[0].y;
                if (middle.x < corners[0].x || middle.x > corners[2].x || middle.y < corners[0].y || middle.y > corners[1].y
                    || width > size * 4f || height > size * 4f)
                {
                    continue;
                }
                if (width * height < bestArea)
                {
                    best = image;
                    bestArea = width * height;
                }
            }
            return best;
        }

        //World middle of the drawn letters of a text
        internal static Vector3? InkCenter(Text text)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            System.Collections.Generic.IList<UIVertex> verts = _Generator.verts;
            if (verts.Count == 0 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (UIVertex vert in verts)
            {
                min = Vector2.Min(min, vert.position);
                max = Vector2.Max(max, vert.position);
            }
            return rect.TransformPoint((min + max) / 2f / text.pixelsPerUnit);
        }
    }

    /*
     * Relationship screen, the ability text of every level: the text area was wider than the gray bar behind it,
     * so long texts ran past the bar. The text area now ends a little before the right end of the bar, and all
     * levels get the same font size, the largest at which every unlocked text fits on its lines.
     */
    public static class RelationshipAbilities
    {
        private const float Margin = 12f;
        private const int MinSize = 14;

        private static readonly AccessTools.FieldRef<WGFavorability, Text> _Content = AccessTools.FieldRefAccess<WGFavorability, Text>("content");

        //Width, position and font size each text has in the prefab
        private static readonly System.Collections.Generic.Dictionary<Text, float[]> _Original = new System.Collections.Generic.Dictionary<Text, float[]>();

        public static void Fit(WGFavorability[] levels)
        {
            if (levels == null)
            {
                return;
            }

            var texts = new System.Collections.Generic.List<Text>();
            foreach (WGFavorability level in levels)
            {
                Text content = level != null ? _Content(level) : null;
                RectTransform bar = content != null ? content.transform.parent as RectTransform : null;
                if (bar == null)
                {
                    continue;
                }

                RectTransform rect = content.rectTransform;
                if (!_Original.TryGetValue(content, out float[] original))
                {
                    original = new[] { rect.sizeDelta.x, rect.anchoredPosition.x, content.fontSize };
                    _Original[content] = original;
                }
                rect.sizeDelta = new Vector2(original[0], rect.sizeDelta.y);
                rect.anchoredPosition = new Vector2(original[1], rect.anchoredPosition.y);
                content.fontSize = (int)original[2];

                //Keeping the left end, the right end comes Margin before the end of the bar
                float left = rect.rect.xMin;
                float barRight = rect.InverseTransformPoint(bar.TransformPoint(new Vector3(bar.rect.xMax, 0f, 0f))).x;
                float width = barRight - Margin - left;
                if (width > 0f && width < rect.rect.width)
                {
                    float shrink = rect.rect.width - width;
                    rect.sizeDelta -= new Vector2(shrink, 0f);
                    rect.anchoredPosition -= new Vector2(shrink * (1f - rect.pivot.x), 0f);
                }

                content.text = content.text.Replace('\u00A0', ' ');
                content.horizontalOverflow = HorizontalWrapMode.Wrap;
                texts.Add(content);
            }

            int size = int.MaxValue;
            foreach (Text text in texts)
            {
                size = Mathf.Min(size, text.fontSize);
            }
            while (size > MinSize && !AllFit(texts, size))
            {
                size--;
            }
            foreach (Text text in texts)
            {
                text.fontSize = size;
            }
        }

        //Whether every unlocked text fits its area at this size without breaking its lines
        private static bool AllFit(System.Collections.Generic.List<Text> texts, int size)
        {
            foreach (Text text in texts)
            {
                //Locked levels hide their text
                if (!text.gameObject.activeSelf || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                text.fontSize = size;
                float width = text.rectTransform.rect.width;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                bool fits = text.preferredWidth <= width;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (!fits || text.preferredHeight > text.rectTransform.rect.height)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /*
     * Showing the date the English way, "Year 1 Month 3" instead of "1. Year 3. Month".
     * The date box has separate texts for the unit (Chinese only), year, year unit, month and month unit.
     * SetYear and SetMonth are called on their own, so both lay out the whole line again.
    */
    public static class DateLayout
    {
        //Center of the line inside the date box and the gaps between label and number and between year and month
        private const float CenterX = 20f;
        private const float PosY = 41.5f;
        private const float LabelGap = 8f;
        private const float GroupGap = 16f;

        public static void Layout(UIDate ui)
        {
            //Child 0 of the UIDate is the UI_Date, its child 0 is the Date_BG holding Unit (0), Year (1), YearUnit (2), Month (3) and MonthUnit (4)
            Transform dateBG = ui.transform.GetChild(0).GetChild(0);
            Text unit = dateBG.GetChild(0).GetComponent<Text>();
            Text year = dateBG.GetChild(1).GetComponent<Text>();
            Text yearUnit = dateBG.GetChild(2).GetComponent<Text>();
            Text month = dateBG.GetChild(3).GetComponent<Text>();
            Text monthUnit = dateBG.GetChild(4).GetComponent<Text>();

            unit.text = "";
            unit.gameObject.SetActive(false);
            yearUnit.text = "Year";
            yearUnit.fontSize = 28;
            monthUnit.text = "Month";
            monthUnit.fontSize = 28;

            Text[] order = { yearUnit, year, monthUnit, month };
            float[] gaps = { LabelGap, GroupGap, LabelGap, 0f };
            float total = 0f;
            foreach (Text t in order)
            {
                //Left aligned on one line, keeping the vertical alignment, so each text starts where we put it
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.alignment = (TextAnchor)((int)t.alignment / 3 * 3);
                t.rectTransform.pivot = new Vector2(0f, t.rectTransform.pivot.y);
            }
            for (int i = 0; i < order.Length; i++)
            {
                total += order[i].preferredWidth + gaps[i];
            }

            float x = CenterX - total / 2f;
            for (int i = 0; i < order.Length; i++)
            {
                RectTransform rt = order[i].rectTransform;
                float width = order[i].preferredWidth;
                rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
                rt.localPosition = new Vector3(x, PosY, 0f);
                x += width + gaps[i];
            }
        }
    }

    [HarmonyPatch(typeof(UIDate), "SetYear")]
    public class UIDate_SetYear_Hook
    {
        public static void Postfix(UIDate __instance)
        {
            DateLayout.Layout(__instance);
        }
    }

    [HarmonyPatch(typeof(UIDate), "SetMonth")]
    public class UIDate_SetMonth_Hook
    {
        public static void Postfix(UIDate __instance)
        {
            DateLayout.Layout(__instance);
        }
    }

    //The "精神" (Energy) label above the energy bar is written in the prefab, nothing translates it
    [HarmonyPatch(typeof(UINurturance), "UpdateValue")]
    public class UINurturance_UpdateValue_Hook
    {
        //The screen is created once, so its texts only need to be searched the first time
        private static UINurturance _Translated;
        private static System.Collections.Generic.List<Text[]> _Bars;
        private static UINurturance _BarsOf;

        public static void Postfix(UINurturance __instance, Text ___Money, WGValueRunBar[] ___NurturanceValues, WGEmotionRunBar ___Emotion, Text ___TotalBasicValue)
        {
            /*
             * The values of the bars ("330 / 1000") sat lower than their names ("Str"): each value goes on its name's line.
             * Names and values are looked for once, then placed over the next frames (the screen slides in).
             */
            if (_Bars == null || _BarsOf != __instance)
            {
                _BarsOf = __instance;
                _Bars = new System.Collections.Generic.List<Text[]>();
                var displays = new System.Collections.Generic.List<Component>();
                foreach (WGValueRunBar bar in ___NurturanceValues ?? new WGValueRunBar[0])
                {
                    displays.Add(bar);
                }
                displays.Add(___Emotion);
                foreach (Component bar in displays)
                {
                    WGText display = bar == null ? null : Traverse.Create(bar).Field("display").GetValue<WGText>();
                    Text value = TextUtil.TextOf(display);
                    Transform root = bar != null && bar.transform.parent != null ? bar.transform.parent : null;
                    Text label = SameLine.LabelLeftOf(value, root);
                    if (value != null && label != null)
                    {
                        SameLine.Keep(value);
                        _Bars.Add(new[] { value, label });
                    }
                }
                //"(Total value: 1287)" on the line of the "Stats" heading next to it
                Text statsLabel = ___TotalBasicValue != null && ___TotalBasicValue.transform.parent != null
                    ? SameLine.LabelLeftOf(___TotalBasicValue, ___TotalBasicValue.transform.parent.parent ?? ___TotalBasicValue.transform.parent) : null;
                if (statsLabel != null)
                {
                    SameLine.Keep(___TotalBasicValue);
                    _Bars.Add(new[] { ___TotalBasicValue, statsLabel });
                }
            }
            var bars = _Bars;
            Replacer.Run(__instance, () =>
            {
                foreach (Text[] pair in bars)
                {
                    SameLine.Align(pair[0], pair[1]);
                }
            });

            //The money in the top right corner is Arial in the prefab (not the game's font), it gets the English font too
            if (___Money != null && TextUtil.UIFont != null)
            {
                ___Money.font = TextUtil.UIFont;
            }

            if (_Translated == __instance)
            {
                return;
            }
            _Translated = __instance;

            foreach (Text text in __instance.GetComponentsInChildren<Text>(true))
            {
                if (text.text != null && text.text.Trim() == "精神")
                {
                    text.text = Heluo.Game.Data.Get<StringTable>("NurturanceProperty_Emotion")?.Text ?? "Energy";
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }
        }
    }

    /*
    * Do all MainUI manipulation in this function.
    */

    [HarmonyPatch(typeof(UIMain), "Show")]
    public class UIMain_Show_Hook
    {
        public static void Postfix(ref UIMain __instance)
        {
            RectTransform dynamicCanvas = (RectTransform)__instance.transform.Find("DynamicCanvas");
            RectTransform menu = (RectTransform)dynamicCanvas.transform.Find("Menu");
            //Updating the Credits Button Text
            RectTransform btnMember = (RectTransform)menu.Find("BtnMember");
            RectTransform btn = (RectTransform)btnMember.Find("Btn");
            RectTransform btn_text = (RectTransform)btn.Find("Text");
            UnityEngine.UI.Text btn_text_text = (UnityEngine.UI.Text)btn_text.gameObject.GetComponentInChildren(typeof(UnityEngine.UI.Text));
            btn_text_text.text = "Credits";

            //Forcing the UI to update itself after all the changes we made. We just force every RectTransform to update itself
            for (int i = 0; i < __instance.transform.childCount; ++i)
            {
                Transform t = __instance.transform.GetChild(i);
                if (t != null && t.GetType() == typeof(RectTransform))
                {
                    RectTransform rt = t as RectTransform;
                    rt.ForceUpdateRectTransforms();
                }
            }
        }
    }

    /*
     * Quest list rows: the time ("2 Turns") ran into the hourglass icon on its left. It shrinks a size at a time (not below
     * MinSize) until its drawn letters start IconGap right of the icon, and is moved right if that is still not enough.
     * Always from the size and place the game gives it, the rows are reused.
     */
    [HarmonyPatch(typeof(WGCommissionScrollBtn), "UpdateWidget")]
    public class WGCommissionScrollBtn_UpdateWidget_Hook
    {
        private const int MinSize = 14;
        private const float IconGap = 4f;

        private static readonly AccessTools.FieldRef<WGScrollBtn, WGText> _Remark = AccessTools.FieldRefAccess<WGScrollBtn, WGText>("remark");
        private static readonly AccessTools.FieldRef<WGScrollBtn, WGImage> _RemarkIcon = AccessTools.FieldRefAccess<WGScrollBtn, WGImage>("remarkIcon");
        private static readonly System.Collections.Generic.Dictionary<Text, int> _Size = new System.Collections.Generic.Dictionary<Text, int>();
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _Origin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private static TextGenerator _Gen;
        private static TextGenerator _Generator => _Gen ?? (_Gen = new TextGenerator());

        public static void Postfix(WGCommissionScrollBtn __instance)
        {
            Text time = TextUtil.TextOf(_Remark(__instance));
            WGImage iconWidget = _RemarkIcon(__instance);
            if (time == null || iconWidget == null || string.IsNullOrEmpty(time.text) || time.rectTransform.parent == null)
            {
                return;
            }
            RectTransform rect = time.rectTransform;
            if (!_Size.TryGetValue(time, out int size))
            {
                TextUtil.Prune(_Size);
                TextUtil.Prune(_Origin);
                size = time.fontSize;
                _Size[time] = size;
                _Origin[rect] = rect.anchoredPosition;
            }
            time.fontSize = size;
            if (!_Origin.TryGetValue(rect, out Vector2 origin))
            {
                origin = rect.anchoredPosition;
                _Origin[rect] = origin;
            }
            rect.anchoredPosition = origin;
            time.horizontalOverflow = HorizontalWrapMode.Overflow;

            Vector3[] corners = new Vector3[4];
            ((RectTransform)iconWidget.transform).GetWorldCorners(corners);
            float limit = Mathf.Max(corners[0].x, corners[2].x) + Mathf.Abs(rect.TransformVector(new Vector3(IconGap, 0f, 0f)).x);
            float? left = InkLeft(time);
            while (left != null && left.Value < limit && time.fontSize > MinSize)
            {
                time.fontSize--;
                left = InkLeft(time);
            }
            if (left != null && left.Value < limit)
            {
                rect.anchoredPosition += new Vector2(rect.parent.InverseTransformVector(new Vector3(limit - left.Value, 0f, 0f)).x, 0f);
            }
        }

        //World x where the text's drawn letters start
        private static float? InkLeft(Text text)
        {
            RectTransform rect = text.rectTransform;
            _Generator.Populate(text.text, TextUtil.Measuring(text, rect.rect.size));
            if (_Generator.vertexCount == 0 || text.pixelsPerUnit <= 0f)
            {
                return null;
            }
            float min = float.MaxValue;
            foreach (UIVertex vert in _Generator.verts)
            {
                min = Mathf.Min(min, vert.position.x);
            }
            return rect.TransformPoint(new Vector3(min / text.pixelsPerUnit, 0f, 0f)).x;
        }
    }

    //Quest screen reward rows ("Crimson Phoenix Dex Powder  1 pc"): kept on one line, shrunk until they fit their row
    [HarmonyPatch(typeof(WGCommissionIntroduction), "UpdateWidget")]
    public class WGCommissionIntroduction_UpdateWidget_Hook
    {
        private const int MinSize = 14;

        /*
         * Several criteria ("All four basic stats 40; participate with Duan Hong'er and Zhong Ruoxin") took three widely
         * spaced lines and ran over the line under their row. The lines are drawn closer, then smaller if still needed,
         * until the last one ends above that line (RowLineAbove the "Reward" heading's capitals, at 1920x1080).
         */
        private const float RowLineAbove = 20f;
        private const float MinLineSpacing = 0.85f;
        private static readonly System.Collections.Generic.Dictionary<Text, int> _CriteriaSize = new System.Collections.Generic.Dictionary<Text, int>();

        private static void FitCriteria(Text criteria, Text label, Text below)
        {
            if (criteria == null)
            {
                return;
            }
            if (!_CriteriaSize.TryGetValue(criteria, out int size))
            {
                TextUtil.Prune(_CriteriaSize);
                size = criteria.fontSize;
                _CriteriaSize[criteria] = size;
            }
            criteria.fontSize = size;
            criteria.lineSpacing = 1f;
            //Drawn from the top of a tall area, the first line can be far from the label: a bigger move is allowed
            SameLine.Align(criteria, label, 6f);
            if (below == null || !SkillBarNames.LettersSpan(below, out float belowTop, out float _))
            {
                return;
            }
            float limit = belowTop + Mathf.Abs(below.rectTransform.TransformVector(new Vector3(0f, RowLineAbove, 0f)).y);
            for (int step = 0; step < 20; step++)
            {
                if (!SkillBarNames.LettersSpan(criteria, out float _, out float lastBase))
                {
                    return;
                }
                //Room for letters going below the line (g, p, y)
                float descent = Mathf.Abs(criteria.rectTransform.TransformVector(new Vector3(0f, criteria.fontSize * 0.25f, 0f)).y);
                if (lastBase - descent >= limit)
                {
                    return;
                }
                if (criteria.lineSpacing > MinLineSpacing)
                {
                    criteria.lineSpacing = Mathf.Max(MinLineSpacing, criteria.lineSpacing - 0.05f);
                }
                else if (criteria.fontSize > MinSize)
                {
                    criteria.fontSize--;
                }
                else
                {
                    return;
                }
                SameLine.Align(criteria, label, 6f);
            }
        }

        public static void Postfix(WGCommissionIntroduction __instance, WGText[] ___EvaluationReward, WGText ___PickUpConditionDescription, WGText ___DeadLine)
        {
            /*
             * "Courtesy 380; Daytime" sat under its "Criteria" label: its first line goes on the label's line like the rows
             * above, from the top, so more criteria take more lines below. The date of "Validity" goes on that word's line.
             */
            Text criteria = TextUtil.TextOf(___PickUpConditionDescription);
            Text deadline = TextUtil.TextOf(___DeadLine);
            if (criteria != null)
            {
                criteria.alignment = (TextAnchor)((int)criteria.alignment % 3);
                criteria.verticalOverflow = VerticalWrapMode.Overflow;
                criteria.text = (criteria.text ?? "").Replace('\u00A0', ' ');
            }
            SameLine.Keep(criteria);
            SameLine.Keep(deadline);
            /*
             * The labels by their text, on the whole quest screen (the "Criteria" label is not inside this widget), looked
             * for again while placing: on the first update their English text is not set yet.
             */
            Transform screen = TextUtil.OwningForm(__instance) is UIForm form ? form.transform : __instance.transform.root;
            string criteriaWords = Heluo.Game.Data.Get<StringTable>("SecondaryInterface1713")?.Text;
            string validityWords = Heluo.Game.Data.Get<StringTable>("SecondaryInterface1715")?.Text;
            string rewardWords = Heluo.Game.Data.Get<StringTable>("SecondaryInterface1714")?.Text;
            Text criteriaLabel = null, deadlineLabel = null, rewardLabel = null;
            Replacer.Run(__instance, () =>
            {
                if (criteriaLabel == null) criteriaLabel = SameLine.Find(screen, criteriaWords);
                if (deadlineLabel == null) deadlineLabel = SameLine.Find(screen, validityWords);
                if (rewardLabel == null) rewardLabel = SameLine.Find(screen, rewardWords);
                FitCriteria(criteria, criteriaLabel, rewardLabel);
                SameLine.Align(deadline, deadlineLabel);
            });

            if (___EvaluationReward == null)
            {
                return;
            }

            foreach (WGText reward in ___EvaluationReward)
            {
                TextUtil.ShrinkToWidth(TextUtil.TextOf(reward), MinSize);
            }

            AlignRows(___EvaluationReward);
        }

        /*
         * The reward texts are spaced a little wider apart than the lines between the rows: the first one sits in the middle
         * of its row, the second about 2 pixels and the third about 7 pixels too low (measured at 1920x1080).
         * The rows below the first one are moved up so every text sits in the middle of its row. The lines are this much
         * closer together than the texts were (51.5 against 55 pixels), so it works the same at any resolution.
         */
        private const float RowToTextSpacing = 51.5f / 55f;

        //Where each reward text sits in the prefab, so we always move it from there
        private static readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _RowOrigin = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();

        private static void AlignRows(WGText[] rewards)
        {
            if (rewards.Length < 3)
            {
                return;
            }

            RectTransform[] rects = new RectTransform[rewards.Length];
            for (int i = 0; i < rewards.Length; i++)
            {
                Text text = TextUtil.TextOf(rewards[i]);
                if (text == null)
                {
                    return;
                }
                rects[i] = text.rectTransform;
                if (!_RowOrigin.TryGetValue(rects[i], out Vector2 origin))
                {
                    origin = rects[i].anchoredPosition;
                    _RowOrigin[rects[i]] = origin;
                }
                rects[i].anchoredPosition = origin;
            }

            float first = Center(rects[0]);
            float spacing = (first - Center(rects[2])) / 2f * RowToTextSpacing;
            for (int i = 1; i < rects.Length; i++)
            {
                rects[i].position += new Vector3(0f, first - i * spacing - Center(rects[i]), 0f);
            }
        }

        //Vertical world position of the middle of a rect
        private static float Center(RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center).y;
        }
    }

    //Quest rewards are built in code as "{name}  {count}個", 個 being the Chinese counter for items
    [HarmonyPatch(typeof(CtrlCommission), "get_reward")]
    public class CtrlCommission_GetReward_Hook
    {
        public static void Postfix(ref string __result)
        {
            if (__result == null || !__result.EndsWith("個"))
            {
                return;
            }

            string withoutCounter = __result.Substring(0, __result.Length - 1);
            string count = System.Text.RegularExpressions.Regex.Match(withoutCounter, @"\d+$").Value;
            __result = withoutCounter + (count == "1" ? " pc" : " pcs");
        }
    }

    /*
     * Reaching the intermediate level in one of the Four Arts adds the Intermediate (and later Expert) training of all four arts to the menu,
     * greyed out until their own art is high enough. We hide those tiers until the player qualifies for them.
     */
    [HarmonyPatch(typeof(NurturanceOrderTree), "CheckShow")]
    public class NurturanceOrderTree_CheckShow_Hook
    {
        //Intermediate (x2) and Expert (x3) tiers of Zither, Go, Calligraphy and Painting
        private static readonly System.Text.RegularExpressions.Regex _ArtTier = new System.Text.RegularExpressions.Regex(@"^Nurturance_04_[1-4][23]$");

        public static void Postfix(NurturanceOrderTree __instance, ref bool __result)
        {
            Nurturance nurturance = __instance.Value;
            if (!__result || nurturance == null || nurturance.OpenCondition == null || !_ArtTier.IsMatch(nurturance.Id))
            {
                return;
            }

            __result = nurturance.OpenCondition.GetValue<bool>();
        }
    }

    //The NPC speech bubble on the reading screen: wrap at spaces and shrink the quote until it fits the bubble
    [HarmonyPatch(typeof(WGNpcTalk), "UpdateWidget")]
    public class WGNpcTalk_UpdateWidget_Hook
    {
        public static void Postfix(WGText ___npcTalk)
        {
            Text text = TextUtil.TextOf(___npcTalk);
            if (text != null)
            {
                TextUtil.FitToBox(text, 12);
            }
        }
    }

    //The 新 ("new") badges on the inventory tabs, if they are a text. Their tabs are set up after the screen is shown.
    [HarmonyPatch(typeof(UIInventory), "UpdateTabNotice")]
    public class UIInventory_UpdateTabNotice_Hook
    {
        public static void Postfix(System.Collections.Generic.List<GameObject> ___TabNotices, int index, bool isOn)
        {
            if (!isOn || ___TabNotices == null || index < 0 || index >= ___TabNotices.Count || ___TabNotices[index] == null)
            {
                return;
            }

            TextUtil.TranslateNewBadges(___TabNotices[index].transform);
        }
    }

    /*
     * The 新 ("new") badges of every other screen (library tabs and books, forge, alchemy, medicine, shop, commissions,
     * save and load tabs, help entries): a text written into the prefab, hidden or shown later. Done every time a screen
     * is shown, items added to its lists are copies of texts already changed.
     */
    [HarmonyPatch(typeof(UIForm), "Show")]
    public class UIForm_Show_NewBadge_Hook
    {
        public static void Postfix(UIForm __instance)
        {
            if (__instance != null)
            {
                TextUtil.TranslateNewBadges(__instance);
            }
        }
    }
}