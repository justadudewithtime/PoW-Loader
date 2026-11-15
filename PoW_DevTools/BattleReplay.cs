using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Heluo;
using Heluo.Data;
using Heluo.Global;
using Heluo.UI;
using UnityEngine;

namespace PoWDevTools
{
    /*
     * Battle replay (F5 console > Battle replay): any of the game's battles can be fought again.
     * Before the battle the game is saved into its own slot (SaveData\DevTools_Replay), when the battle ends that save is
     * loaded back: no result screen, no drops, no cutscene, no story flags or rewards from the battle, the player is back
     * where they stood. Loading another save or going to the menu during a replay ends replay mode, so later battles of
     * the story are never touched.
     */
    internal static class BattleReplay
    {
        private const string Snapshot = "DevTools_Replay";

        public static bool Open;
        public static float PanelRight = 320f;
        public static bool InBattle { get { return GameGlobal.IsInBattle; } }

        //A replay is running: its end is ours
        public static bool Replaying { get; private set; }
        public static bool BattleStarted { get { return _sawBattle && Game.BattleStateMachine != null; } }
        private static bool _sawBattle;
        private static float _outOfBattleSince = -1f;
        private static bool _starting;

        private static List<KeyValuePair<string, string>> _battles;
        private static List<KeyValuePair<string, string>> _shown = new List<KeyValuePair<string, string>>();
        private static string _search = "", _searchShown;
        private static bool _tests, _testsShown;
        private static Vector2 _scroll;
        private static int _first, _count;
        private static string _selected;
        private static string _message = "";
        private static GUIStyle _rowStyle, _noteStyle;

        //-------------------------------------------------------------- start
        private static void Start(string battleId)
        {
            if (_starting || Replaying) return;
            if (InBattle) { _message = "Already in a battle."; return; }
            if (Game.GameData == null || Game.EntityManager?.GetPlayerEntity() == null) { _message = "Load a save first."; return; }

            _starting = true;
            _message = "Saving your place...";
            try
            {
                //Saved first, the battle starts once the file is written
                Game.SaveAsync(Snapshot, () =>
                {
                    _starting = false;
                    Replaying = true;
                    _sawBattle = false;
                    _outOfBattleSince = -1f;
                    Open = false;
                    ConsolePanel.Close();
                    Debug.Log("[DevTools] Battle replay: " + battleId);
                    Game.FSM.SendEvent("BATTLE", new Heluo.FSM.Main.BattleEventArgs { BattleId = battleId });
                });
            }
            catch (Exception e)
            {
                _starting = false;
                _message = "Could not save before the battle: " + e.Message;
                Debug.LogError("[DevTools] Battle replay save: " + e);
            }
        }

        //-------------------------------------------------------------- end (called from the EndBattle hook)
        public static void Finish(object battleManager)
        {
            Replaying = false;
            Debug.Log("[DevTools] Battle replay over, back to " + Snapshot);
            try
            {
                Traverse.Create(battleManager).Field("InBattle").SetValue(false);
                Game.UI.Close<UIBattle>();
            }
            catch (Exception e)
            {
                Debug.LogError("[DevTools] Battle replay end: " + e.Message);
            }
            Game.LoadAsync(Snapshot);
        }

        //A different save loaded (or the menu): replay mode ends, its own load and the game's battle retry do not end it
        public static void OnLoad(string filename)
        {
            if (Replaying && filename != Snapshot && filename != GameConfig.BattleSaveFile)
            {
                Replaying = false;
                Debug.Log("[DevTools] Battle replay left (" + filename + " loaded)");
            }
        }

        public static void Update()
        {
            if (!Replaying) return;
            //Safety net: out of battle for a long while after the replay started (title screen, a crash of the battle...)
            if (InBattle)
            {
                _sawBattle = true;
                _outOfBattleSince = -1f;
            }
            else if (_sawBattle)
            {
                if (_outOfBattleSince < 0f) _outOfBattleSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _outOfBattleSince > 30f)
                {
                    Replaying = false;
                    Debug.Log("[DevTools] Battle replay left (out of battle)");
                }
            }
        }

        private static bool SnapshotExists
        {
            get
            {
                try { return System.IO.File.Exists(string.Format(GameConfig.SaveFilePath, Snapshot)); } catch { return false; }
            }
        }

        //Back to the place saved before the last replay (for a replay that did not come back by itself)
        private static void Restore()
        {
            if (InBattle) { _message = "Finish or leave the battle first."; return; }
            Replaying = false;
            Open = false;
            ConsolePanel.Close();
            Debug.Log("[DevTools] Battle replay: restoring " + Snapshot);
            Game.LoadAsync(Snapshot);
        }

        //-------------------------------------------------------------- list
        private static List<KeyValuePair<string, string>> Battles
        {
            get
            {
                if (_battles != null) return _battles;
                _battles = new List<KeyValuePair<string, string>>();
                try
                {
                    foreach (var b in Game.Data.Get<BattleArea>().OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        _battles.Add(new KeyValuePair<string, string>(b.Key, Catalog.Clean(b.Value.Remark)));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[DevTools] Could not list the battles: " + e);
                }
                return _battles;
            }
        }

        private static bool IsTest(KeyValuePair<string, string> b)
        {
            return b.Key.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 || b.Value.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void DrawList(bool consoleOpen)
        {
            if (!Open || !consoleOpen) return;
            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true, clipping = TextClipping.Clip };
                _noteStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            }

            float width = 760f, height = Mathf.Min(Screen.height - 20f, 760f);
            Rect area = new Rect(PanelRight + 10f, 10f, width, height);
            GUI.DrawTexture(area, ConsolePanel.Background);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 8f, area.width - 16f, area.height - 16f));
            bool layout = Event.current.type == EventType.Layout;

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("<b>Battle replay</b>   (" + _shown.Count + " battles)", _noteStyle, GUILayout.Width(400f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Width(70f))) Open = false;
            }
            GUILayout.Label("Fight any battle again. Afterwards you are back here exactly as before: no result screen, no rewards, no story flags.", _noteStyle);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Search:", GUILayout.Width(60f));
                _search = GUILayout.TextField(_search ?? "");
                _tests = GUILayout.Toggle(_tests, " Test battles", GUILayout.Width(110f));
            }
            if (layout && (_searchShown != _search || _testsShown != _tests))
            {
                _searchShown = _search;
                _testsShown = _tests;
                string s = _search.Trim();
                _shown = Battles.Where(b => (_tests || !IsTest(b)) &&
                    (s.Length == 0 || b.Key.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 || b.Value.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                _scroll = Vector2.zero;
            }

            const float row = 26f;
            float listHeight = height - 190f;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(listHeight));
            if (layout)
            {
                _first = Mathf.Max(0, (int)(_scroll.y / row) - 1);
                _count = Mathf.Max(0, Mathf.Min(_shown.Count - _first, (int)(listHeight / row) + 3));
            }
            int first = _first, count = Mathf.Min(_count, Mathf.Max(0, _shown.Count - _first));
            GUILayout.Space(first * row);
            for (int i = first; i < first + count; i++)
            {
                var b = _shown[i];
                string label = (b.Key == _selected ? "<color=#ffd27a><b>" : "") + b.Value + "   <color=#9a9a9a>" + b.Key + "</color>" + (b.Key == _selected ? "</b></color>" : "");
                if (GUILayout.Button(label, _rowStyle, GUILayout.Height(row - 2f))) _selected = b.Key;
            }
            GUILayout.Space(Mathf.Max(0, _shown.Count - first - count) * row);
            GUILayout.EndScrollView();

            string name = _selected == null ? null : Battles.FirstOrDefault(b => b.Key == _selected).Value;
            GUILayout.Label(_selected == null ? "Pick a battle." : "<b>" + name + "</b>  (" + _selected + ")", _noteStyle);
            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = _selected != null && !_starting;
                if (GUILayout.Button(_starting ? "Saving..." : "Start replay", GUILayout.Height(30f))) Start(_selected);
                GUI.enabled = true;
            }
            if (SnapshotExists)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("<color=#9a9a9a>Your place before the last replay is kept in SaveData\\" + Snapshot + ".</color>", _noteStyle);
                    if (GUILayout.Button("Restore snapshot", GUILayout.Width(150f))) Restore();
                }
            }
            if (_message.Length > 0) GUILayout.Label(_message, _noteStyle);
            GUILayout.EndArea();
        }
    }

    //The end of a replayed battle: straight back to the saved place instead of the result screen
    [HarmonyPatch(typeof(Heluo.FSM.Battle.EndBattle), "OnEnable")]
    internal static class EndBattle_OnEnable
    {
        private static bool Prefix(Heluo.FSM.Battle.EndBattle __instance)
        {
            if (!BattleReplay.Replaying) return true;
            BattleReplay.Finish(Traverse.Create(__instance).Property("BattleManager").GetValue());
            return false;
        }
    }

    /*
     * A battle on another map loads that map with a LoadingEventArgs whose TimeStage is left at its default, Begin.
     * Outside the Begin stage the loader then moves the calendar on (Round.NextReoud) and plays the next period's
     * opening cutscene instead of the battle. While a replay starts, the load keeps the time stage and plays no cutscene.
     */
    [HarmonyPatch(typeof(Heluo.FSM.Main.Loading), "OnEnable")]
    internal static class Loading_OnEnable
    {
        private static void Prefix(Heluo.FSM.Main.Loading __instance)
        {
            if (!BattleReplay.Replaying || BattleReplay.BattleStarted) return;
            try
            {
                var args = Traverse.Create(Game.FSM).Property("eventArgs").GetValue() as Heluo.FSM.Main.LoadingEventArgs;
                if (args == null || !args.IsRestartBattle) return;
                args.TimeStage = Heluo.Manager.TimeStage.None;
                args.CinematicId = "";
                args.IsNextTime = false;
                Debug.Log("[DevTools] Battle replay: battle map load keeps the time stage");
            }
            catch (Exception e)
            {
                Debug.LogError("[DevTools] Battle replay load: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.LoadAsync))]
    internal static class Game_LoadAsync
    {
        private static void Prefix(string filename)
        {
            BattleReplay.OnLoad(filename);
        }
    }
}
