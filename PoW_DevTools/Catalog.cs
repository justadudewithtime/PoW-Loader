using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Heluo;
using Heluo.Data;

namespace DevToolsProbe
{
    //One entry of a picker list: the ID, its English name, the developers' note (often Chinese) and longer details
    internal class Entry
    {
        public string Id;
        public string Name;
        public string Note;
        public string Detail;
        public string SearchText;

        public Entry(string id, string name, string note, string detail)
        {
            Id = id;
            Name = Catalog.Clean(name);
            Note = Catalog.Clean(note);
            Detail = Catalog.Clean(detail);
            SearchText = (Id + " " + Name + " " + Note + " " + Detail).ToLowerInvariant();
        }
    }

    internal static class Catalog
    {
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = Regex.Replace(s, "<[^>]*>", "");
            return s.Replace("\n", " ").Replace("\r", " ").Trim();
        }

        public static List<Entry> Load(PickKind kind)
        {
            switch (kind)
            {
                case PickKind.Item:
                    return Game.Data.Get<Props>().OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(d => new Entry(d.Key, d.Value.Name, d.Value.Remark,
                        d.Value.PropsType + ", price " + d.Value.Price + ". " + d.Value.Description)).ToList();
                case PickKind.Skill:
                    return Game.Data.Get<Skill>().OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(d => new Entry(d.Key, d.Value.Name, d.Value.Remark, d.Value.Description)).ToList();
                case PickKind.Mantra:
                    return Game.Data.Get<Mantra>().OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(d => new Entry(d.Key, d.Value.Name, d.Value.Remark, d.Value.Description)).ToList();
                case PickKind.Trait:
                    return Game.Data.Get<Trait>().OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(d => new Entry(d.Key, d.Value.Name, d.Value.Remark, d.Value.Description)).ToList();
                case PickKind.Reward:
                    return Game.Data.Get<Reward>().OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(d => new Entry(d.Key, d.Value.Description, d.Value.Remark, RewardText(d.Value))).ToList();
                case PickKind.Character:
                    return Characters();
                case PickKind.Flag:
                    return Flags();
            }
            return new List<Entry>();
        }

        private static string RewardText(Reward reward)
        {
            try { var lines = reward.GetRewardText(); return lines == null ? string.Empty : string.Join(", ", lines.ToArray()); } catch { return string.Empty; }
        }

        private static List<Entry> Characters()
        {
            var exteriors = new Dictionary<string, CharacterExterior>();
            foreach (var e in Game.Data.Get<CharacterExterior>())
            {
                string id = string.IsNullOrEmpty(e.Value.InfoId) ? e.Key : e.Value.InfoId;
                if (!exteriors.ContainsKey(id)) exteriors[id] = e.Value;
            }
            var known = new HashSet<string>(Game.GameData.Character.Keys);
            var list = new List<Entry> { new Entry("Player", "You (the hero)", "主角", "") };
            foreach (var info in Game.Data.Get<Heluo.Data.CharacterInfo>().OrderBy(k => known.Contains(k.Key) ? 0 : 1).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                exteriors.TryGetValue(info.Key, out CharacterExterior ex);
                string name = ex != null ? ((ex.SurName ?? "") + (ex.Name ?? "")).Trim() : "";
                string chinese = ex != null && !string.IsNullOrEmpty(ex.Remark) ? ex.Remark : info.Value.Remark;
                string where = known.Contains(info.Key) ? "" : "(not in save yet) ";
                list.Add(new Entry(info.Key, name, chinese, where + (ex != null ? ex.Description : "")));
            }
            return list;
        }

        /*
         * Story flags: every flag the game's data sets or checks (dialogue, quests, map events, cutscenes, battles),
         * read once from the translation files, plus the ones already in the save. Each shows its value in the save,
         * the quest of the same event and the first line that sets it.
         */
        private class FlagUse
        {
            public int Sets, Checks;
            public string SetTable, SetRow;
        }

        private static volatile Dictionary<string, FlagUse> _flagUses;
        private static volatile bool _scanning;
        private static readonly Regex FlagRegex = new Regex(@"(SetFlagAction|CheckFlag)\\?""\s*:\s*[^""\\]*\\?""([^""\\]+)", RegexOptions.Compiled);

        //Ready once the background read of the data files is done
        public static bool FlagsReady { get { return _flagUses != null; } }

        //The files are read on a worker thread (no Unity calls there), the game keeps running meanwhile
        public static void StartFlagScan()
        {
            if (_flagUses != null || _scanning) return;
            _scanning = true;
            string root = Path.Combine(BepInEx.Paths.GameRootPath, Path.Combine("ModResources", Path.Combine("EnglishTranslate", Path.Combine("Config", "chs"))));
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var uses = new Dictionary<string, FlagUse>();
                try
                {
                    if (Directory.Exists(root))
                    {
                        foreach (string file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                        {
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            //battle\buffer holds the game's binary files, not text
                            if ((ext != ".txt" && ext != ".json") || file.Replace('\\', '/').IndexOf("/battle/buffer/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            ScanFile(file, ext, uses);
                        }
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("[DevToolsProbe] Reading the flags failed: " + e.Message);
                }
                _flagUses = uses;
                _scanning = false;
            });
        }

        private static void ScanFile(string file, string ext, Dictionary<string, FlagUse> uses)
        {
            string table = Path.GetFileNameWithoutExtension(file);
            try
            {
                string text = File.ReadAllText(file, System.Text.Encoding.UTF8);
                if (text.IndexOf("Flag", StringComparison.Ordinal) < 0) return;
                foreach (string line in text.Split('\n'))
                {
                    if (line.IndexOf("Flag", StringComparison.Ordinal) < 0) continue;
                    string rowId = ext == ".txt" ? line.Split('\t')[0].Trim('\uFEFF') : table;
                    foreach (Match m in FlagRegex.Matches(line))
                    {
                        string flag = m.Groups[2].Value;
                        if (!uses.TryGetValue(flag, out FlagUse use)) uses[flag] = use = new FlagUse();
                        if (m.Groups[1].Value == "CheckFlag")
                        {
                            use.Checks++;
                        }
                        else
                        {
                            use.Sets++;
                            if (use.SetTable == null) { use.SetTable = table; use.SetRow = rowId; }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[DevToolsProbe] Could not read flags from " + file + ": " + e.Message);
            }
        }

        //Where a flag is set, readable: the dialogue line, quest name or just the table and row
        private static string Place(string table, string rowId)
        {
            try
            {
                if (table == "Talk") return "dialogue " + rowId;
                if (table == "Quest")
                {
                    Quest quest = Game.Data.Get<Quest>(rowId);
                    if (quest != null) return "quest " + rowId + " " + Clean(quest.Name);
                }
            }
            catch { }
            return table + " " + rowId;
        }

        private static readonly Regex EventNumber = new Regex(@"^[a-z]+(\d{7})", RegexOptions.Compiled);

        private static List<Entry> Flags()
        {
            var uses = _flagUses ?? new Dictionary<string, FlagUse>();
            var save = Game.GameData.Flag;
            //Quest names by event number (q1010203_00 -> 1010203)
            var quests = new Dictionary<string, string>();
            foreach (var q in Game.Data.Get<Quest>())
            {
                Match m = EventNumber.Match(q.Key);
                if (m.Success && !quests.ContainsKey(m.Groups[1].Value)) quests[m.Groups[1].Value] = Clean(q.Value.Name);
            }

            var ids = new HashSet<string>(uses.Keys);
            ids.UnionWith(save.Keys);
            var list = new List<Entry>();
            foreach (string id in ids.OrderBy(k => save.ContainsKey(k) ? 0 : 1).ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                string value = save.TryGetValue(id, out int v) ? "= " + v : "not set";
                Match m = EventNumber.Match(id);
                string quest = m.Success && quests.TryGetValue(m.Groups[1].Value, out string q) ? "quest: " + q : "";
                uses.TryGetValue(id, out FlagUse use);
                string detail = use == null ? "Only in your save, not in the game's data." :
                    "Set " + use.Sets + "x, checked " + use.Checks + "x." + (use.SetTable != null ? " First set by " + Place(use.SetTable, use.SetRow) + "." : " Never set by the game's data (checked only).");
                list.Add(new Entry(id, value + (quest.Length > 0 ? "   " + quest : ""), "", detail));
            }
            return list;
        }
    }
}
