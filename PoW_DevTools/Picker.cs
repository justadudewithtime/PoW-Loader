using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Heluo;
using Heluo.Data;
using UnityEngine;
using System.Text.RegularExpressions;

namespace DevToolsProbe
{
    /*
     * The console's ID fields get a "▼" button next to them. It opens a list of everything of that kind in the game
     * (ID and English name, read from the game's own data), with a search box. Picking an entry fills the field.
     * The fields are told apart by the console section drawing them and their order in it.
     */
    internal enum PickKind { None, Character, Skill, Mantra, Trait, Item, Reward, Flag }

    internal static class Picker
    {
        //Field kinds per console section, in the order its text fields are drawn
        private static readonly Dictionary<string, PickKind[]> Sections = new Dictionary<string, PickKind[]>
        {
            { "DrawCharacterUpgradableProperty", new[] { PickKind.Character, PickKind.None } },
            { "DrawSkill", new[] { PickKind.Character, PickKind.Skill, PickKind.Skill, PickKind.Skill, PickKind.Skill, PickKind.None } },
            { "DrawMantra", new[] { PickKind.Character, PickKind.Mantra, PickKind.Mantra, PickKind.Mantra, PickKind.Mantra, PickKind.None } },
            { "DrawTrait", new[] { PickKind.Character, PickKind.Trait, PickKind.Trait, PickKind.Trait, PickKind.Trait, PickKind.None } },
            { "DrawItem", new[] { PickKind.Item, PickKind.None } },
            { "DrawReward", new[] { PickKind.Reward } },
            { "DrawFlag", new[] { PickKind.Flag, PickKind.None } },
        };

        private static string _section;
        private static int _fieldIndex;

        //The field the open list fills, and a value picked for a field, handed over the next time that field is drawn
        private static string _openKey;
        private static string _drawnKey;
        private static PickKind _openKind;
        private static string _pendingKey;
        private static string _pendingValue;

        private static List<Entry> _entries = new List<Entry>();
        private static List<Entry> _shown = new List<Entry>();
        private static Entry _hover;
        private static bool _waitingForFlags;
        private static string _search = string.Empty;
        private static string _searchShown;
        private static Vector2 _scroll;
        public static float PanelRight = 320f;

        //The battle replay list opens in the same place
        public static void CloseList()
        {
            _openKey = null;
        }

        public static void BeginSection(string name)
        {
            _section = name;
            _fieldIndex = 0;
        }

        public static void EndSection()
        {
            _section = null;
        }

        //After each console text field: hand over a picked value, then draw the button that opens the list
        public static void AfterField(ref string result)
        {
            if (_section == null || !Sections.TryGetValue(_section, out PickKind[] kinds))
            {
                return;
            }
            int index = _fieldIndex++;
            PickKind kind = index < kinds.Length ? kinds[index] : PickKind.None;
            if (kind == PickKind.None)
            {
                return;
            }

            string key = _section + "#" + index;
            if (_pendingKey == key)
            {
                result = _pendingValue;
                _pendingKey = null;
            }

            bool open = _openKey == key;
            if (GUILayout.Button(open ? "▲" : "▼", GUILayout.Width(26f)))
            {
                if (open)
                {
                    _openKey = null;
                }
                else
                {
                    Open(key, kind);
                    BattleReplay.Open = false;
                }
            }
        }

        private static void Open(string key, PickKind kind)
        {
            _openKey = key;
            _openKind = kind;
            _search = string.Empty;
            _searchShown = null;
            _hover = null;
            _scroll = Vector2.zero;
            if (kind == PickKind.Flag && !Catalog.FlagsReady)
            {
                Catalog.StartFlagScan();
                _entries = new List<Entry>();
                _waitingForFlags = true;
                return;
            }
            try
            {
                _entries = Catalog.Load(kind);
            }
            catch (Exception e)
            {
                Debug.LogError("[DevToolsProbe] Could not list " + kind + ": " + e);
                _entries = new List<Entry>();
            }
        }

        private const float Row = 26f;
        private static int _first, _count;
        private static GUIStyle _rowStyle;
        private static GUIStyle _detailStyle;
        private const float DetailHeight = 130f;

        //Drawn by the plugin after the console: the open list, to the right of the console panel
        public static void DrawList(bool consoleOpen)
        {
            if (Event.current.type == EventType.Layout)
            {
                _drawnKey = _openKey;
            }
            if (_drawnKey == null)
            {
                return;
            }
            if (!consoleOpen)
            {
                _openKey = null;
                return;
            }
            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, richText = true };
                _detailStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true, fontSize = 14 };
                _detailStyle.normal.textColor = Color.white;
            }

            float width = 760f;
            float height = Mathf.Min(Screen.height - 20f, 720f);
            Rect area = new Rect(PanelRight + 10f, 10f, width, height);
            GUI.DrawTexture(area, ConsolePanel.Background);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 8f, area.width - 16f, area.height - 16f));

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Pick " + Title(_openKind) + "   (" + _shown.Count + ")", GUILayout.Width(300f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Width(70f)))
                {
                    _openKey = null;
                }
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Search:", GUILayout.Width(60f));
                GUI.SetNextControlName("PickerSearch");
                _search = GUILayout.TextField(_search ?? string.Empty);
            }
            //What the list shows only changes in the Layout pass, Unity wants the same controls in every pass of a frame
            bool layout = Event.current.type == EventType.Layout;
            if (layout && _waitingForFlags && Catalog.FlagsReady)
            {
                _waitingForFlags = false;
                _entries = Catalog.Load(PickKind.Flag);
                _searchShown = null;
            }
            if (layout && _searchShown != _search)
            {
                _searchShown = _search;
                string s = _search.Trim().ToLowerInvariant();
                _shown = s.Length == 0 ? _entries : _entries.Where(e => e.SearchText.Contains(s)).ToList();
                _scroll = Vector2.zero;
            }
            if (_openKind == PickKind.Flag)
            {
                GUILayout.Label(_waitingForFlags ? "Reading the story flags from the game's data files..." : "Flags set in your save first, then every flag the game's data uses.");
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            //Only the rows in view are drawn, the lists have up to 2000 entries
            float viewHeight = height - 110f - DetailHeight;
            if (layout)
            {
                _first = Mathf.Max(0, (int)(_scroll.y / Row) - 1);
                _count = Mathf.Max(0, Mathf.Min(_shown.Count - _first, (int)(viewHeight / Row) + 3));
            }
            int first = _first;
            int count = Mathf.Min(_count, Mathf.Max(0, _shown.Count - first));
            GUILayout.Space(first * Row);
            for (int i = first; i < first + count; i++)
            {
                Entry entry = _shown[i];
                string english = Translator.Get(entry.Note);
                string name = entry.Name.Length > 0 ? entry.Name : (english ?? "");
                string note = english != null && entry.Name.Length > 0 ? "   <color=#c8b47a>" + english + "</color>"
                    : (entry.Note.Length > 0 && english == null ? "   <color=#9a9a9a>" + entry.Note + "</color>" : "");
                if (GUILayout.Button("<b>" + entry.Id + "</b>   " + name + note, _rowStyle, GUILayout.Height(Row - 2f)))
                {
                    _pendingKey = _openKey;
                    _pendingValue = entry.Id;
                    _openKey = null;
                    //A focused text field would keep showing what was typed in it
                    GUIUtility.keyboardControl = 0;
                }
                if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    _hover = entry;
                }
            }
            GUILayout.Space(Mathf.Max(0, _shown.Count - first - count) * Row);
            GUILayout.EndScrollView();

            //The entry under the mouse in full: name, the developers' note and its description
            Entry shown = _hover;
            string text = shown == null ? "Point at an entry to see its details." : Details(shown);
            if (Translator.Status.Length > 0) text = "<color=#c8b47a>" + Translator.Status + "</color>\n" + text;
            GUILayout.Label(text, _detailStyle, GUILayout.Height(DetailHeight));
            GUILayout.EndArea();
        }

        private static string _detailFor;
        private static string _detailText;

        //Everything about one entry. Dialogue lines and translations are looked up only for the entry pointed at.
        private static string Details(Entry e)
        {
            string english = Translator.Get(e.Note);
            string detail = e.Detail;
            if (_openKind == PickKind.Flag && _detailFor != e.Id)
            {
                _detailFor = e.Id;
                _detailText = detail;
                Match m = Regex.Match(detail, @"dialogue (\S+)");
                if (m.Success)
                {
                    try
                    {
                        Heluo.Data.Talk talk = Game.Data.Get<Heluo.Data.Talk>(m.Groups[1].Value);
                        if (talk != null) _detailText = detail.Replace(m.Value, m.Value + " \"" + Catalog.Clean(talk.Message) + "\"");
                    }
                    catch { }
                }
            }
            if (_openKind == PickKind.Flag) detail = _detailText;
            string translatedDetail = Translator.NeedsTranslation(detail) ? Translator.Get(detail) : null;
            return "<b>" + e.Id + "</b>  " + e.Name
                + (e.Note.Length > 0 ? "\n<color=#bdbdbd>" + e.Note + "</color>" + (english != null ? "  <color=#c8b47a>" + english + "</color>" : Translator.NeedsTranslation(e.Note) ? "  <color=#777777>(no translation)</color>" : "") : "")
                + (detail.Length > 0 ? "\n" + (translatedDetail ?? detail) : "");
        }

        private static string Title(PickKind kind)
        {
            switch (kind)
            {
                case PickKind.Mantra: return "internal art";
                case PickKind.Flag: return "story flag";
                default: return kind.ToString().ToLowerInvariant();
            }
        }
    }

    //The console sections, so each text field knows which section it belongs to
    [HarmonyPatch]
    internal static class ConsoleSections
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "DrawCharacterUpgradableProperty", "DrawSkill", "DrawMantra", "DrawTrait", "DrawItem", "DrawReward", "DrawFlag" })
            {
                yield return AccessTools.Method(typeof(CheatingConsole), name);
            }
        }

        private static void Prefix(System.Reflection.MethodBase __originalMethod) { Picker.BeginSection(__originalMethod.Name); }
        private static void Finalizer() { Picker.EndSection(); }
    }

    [HarmonyPatch(typeof(GUILayout), nameof(GUILayout.TextField), new[] { typeof(string), typeof(GUILayoutOption[]) })]
    internal static class GUILayout_TextField
    {
        private static void Postfix(ref string __result)
        {
            if (ConsoleText.Drawing)
            {
                Picker.AfterField(ref __result);
            }
        }
    }
}
