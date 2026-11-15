using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PoWDevTools
{
    /*
     * English for the developers' Chinese notes (items, skills, internal arts, traits, quests, characters, rewards).
     * All 4808 notes are translated in Notes.tsv, built into the plugin, so nothing is looked up online.
     * BepInEx\config\DevTools_Translations.txt (Chinese<TAB>English per line) can override or add lines.
     */
    internal static class Translator
    {
        private static readonly Regex Chinese = new Regex("[㐀-鿿]", RegexOptions.Compiled);
        private static Dictionary<string, string> _english;
        public static string Status = "";

        public static bool NeedsTranslation(string s)
        {
            return !string.IsNullOrEmpty(s) && Chinese.IsMatch(s);
        }

        //The English text of a note, or null when there is none
        public static string Get(string chinese)
        {
            if (!NeedsTranslation(chinese)) return null;
            Load();
            return _english.TryGetValue(chinese.Trim(), out string english) ? english : null;
        }

        //Kept for the plugin's Update loop, nothing to do any more
        public static void Pump(MonoBehaviour host) { }

        private static void Load()
        {
            if (_english != null) return;
            _english = new Dictionary<string, string>();
            try
            {
                using (Stream s = typeof(Translator).Assembly.GetManifestResourceStream("PoWDevTools.Notes.tsv"))
                {
                    if (s != null)
                    {
                        using (var reader = new StreamReader(s, Encoding.UTF8))
                        {
                            Read(reader.ReadToEnd());
                        }
                    }
                }
                string file = Path.Combine(BepInEx.Paths.ConfigPath, "DevTools_Translations.txt");
                if (File.Exists(file)) Read(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Debug.LogError("[DevTools] Could not load the note translations: " + e.Message);
            }
        }

        private static void Read(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                string chinese = line.Substring(0, tab).Trim();
                string english = line.Substring(tab + 1).Trim();
                if (english.Length == 0) continue;
                _english[chinese] = english;
                //The pickers show notes without rich text tags, look them up that way too
                string clean = Catalog.Clean(chinese);
                if (clean != chinese) _english[clean] = english;
            }
        }
    }
}
