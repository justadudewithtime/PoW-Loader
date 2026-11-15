using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Heluo;
using Heluo.UI;
using UnityEngine;

namespace PoWDevTools
{
    /*
     * Dev Tools: tools for modding and having fun with the game, not part of the English release.
     * F5: the game's own cheating console (it listens for F5 itself, we only attach it), in English, with ID pickers (see Picker).
     * F6: animation browser. F7: photo mode (see PhotoMode). F8: wardrobe, colours and texture mods (see Wardrobe).
     */
    [BepInPlugin("pow.devtools", "Dev Tools", "1.0.0")]
    [BepInProcess("PathOfWuxia.exe")]
    public class DevToolsPlugin : BaseUnityPlugin
    {
        private CheatingConsole _console;
        private readonly PhotoMode _photo = new PhotoMode();
        private readonly Wardrobe _wardrobe = new Wardrobe();
        private string _message;
        private float _messageUntil;

        private void Awake()
        {
            new Harmony("pow.devtools").PatchAll(typeof(DevToolsPlugin).Assembly);

            //The console is a MonoBehaviour the release build never creates: we create it, it handles F5 and draws itself
            _console = gameObject.AddComponent<CheatingConsole>();
            Logger.LogInfo("Cheating Console attached, F5 toggles it (with Battle replay). F6: animation browser, F7: photo mode, F8: wardrobe.");
        }

        private void Update()
        {
            _photo.Update();
            _wardrobe.Update();
            BattleReplay.Update();
        }

        private static bool GameGlobalIsInBattle()
        {
            try { return Heluo.Global.GameGlobal.IsInBattle; } catch { return false; }
        }

        private void Show(string message)
        {
            Logger.LogInfo(message);
            _message = message;
            _messageUntil = Time.realtimeSinceStartup + 6f;
        }

        private GUIStyle _style;

        private void OnGUI()
        {
            _photo.OnGUI();
            _wardrobe.OnGUI();
            if (_photo.Active) return;

            BattleReplay.DrawList(ConsolePanel.IsOpen(_console) && !BattleReplay.InBattle);

            //The ID list of the console, drawn after the console so it stays on top
            Picker.DrawList(ConsolePanel.IsOpen(_console) && !GameGlobalIsInBattle());

            if (_message == null || Time.realtimeSinceStartup > _messageUntil)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { fontSize = 20, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            }
            GUI.Box(new Rect(Screen.width / 2f - 400f, 20f, 800f, 60f), _message, _style);
        }
    }

    //The Cheating Console labels in English, only while the console draws itself
    internal static class ConsoleText
    {
        public static bool Drawing;

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "修改升級數值", "Edit attributes" },
            { "修改技能", "Edit skills" },
            { "修改心法", "Edit internal arts" },
            { "修改特質", "Edit traits" },
            { "增加道具", "Add item" },
            { "增加獎勵", "Give reward" },
            { "修改旗標", "Edit story flag" },
            { "其他修改", "Other" },
            { "玩家陣營一擊斃殺", "Party: one-hit kills" },
            { "玩家陣營無限行動", "Party: unlimited actions" },
            { "玩家陣營無冷卻時間", "Party: no cooldowns" },
            { "玩家陣營真氣無限", "Party: unlimited Qi" },
            { "全好感+10", "All affection +10" },
            { "全能力+10", "All attributes up" },
            { "加1000元", "+1000 money" },
            { "精神全滿", "Full energy" },
            { "全閱讀", "Read all books" },
            { "換性別", "Swap gender" },
            { "修改", "Apply" },
            { "新增", "Add" },
            { "輸入要修改的人物編號 : ", "Character ID : " },
            { "輸入人物編號 : ", "Character ID : " },
            { "輸入增加的值 : ", "Amount : " },
            { "增加的值 : ", "Amount : " },
            { "輸入道具編號 : ", "Item ID : " },
            { "數量 : ", "Count : " },
            { "輸入獎勵編號 : ", "Reward ID : " },
            { "輸入旗標編號 : ", "Flag ID : " },
            { "編號 : ", "ID : " },
            { "修改技能 : ", "Replace skill : " },
            { "舊技能編號 : ", "Old skill ID : " },
            { "新技能編號 : ", "New skill ID : " },
            { "新增技能 : ", "Add skill : " },
            { "增加技能經驗 : ", "Skill exp : " },
            { "修改心法 : ", "Replace art : " },
            { "新心法編號 : ", "New art ID : " },
            { "新增心法 : ", "Add art : " },
            { "增加心法經驗 : ", "Art exp : " },
            { "修改特質 : ", "Replace trait : " },
            { "舊特質編號 : ", "Old trait ID : " },
            { "新增特質 : ", "Add trait : " },
            { "增加特質經驗 : ", "Trait exp : " },
        };

        public static void Translate(ref string text)
        {
            if (Drawing && text != null && English.TryGetValue(text, out string english))
            {
                text = english;
            }
        }
    }

    /*
     * A solid dark panel behind the console, so it reads over the minimap and the scenery. The console has no size of
     * its own (each section is drawn at a growing y), so every frame records how far its parts reach and the next frame's
     * panel is drawn that big, with a margin.
     */
    internal static class ConsolePanel
    {
        private static readonly AccessTools.FieldRef<CheatingConsole, bool> _IsOpen = AccessTools.FieldRefAccess<CheatingConsole, bool>("IsOpen");
        private const float Margin = 8f;

        private static Texture2D _background;
        public static Texture2D Background { get { Ensure(); return _background; } }

        private static void Ensure()
        {
            if (_background == null)
            {
                _background = new Texture2D(1, 1);
                _background.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.06f, 0.92f));
                _background.Apply();
            }
        }

        public static bool IsOpen(CheatingConsole console) { return console != null && _IsOpen(console); }
        private static Rect _last = new Rect(0f, 0f, 320f, 330f);
        private static float _right, _bottom, _contentBottom = 320f;
        private static CheatingConsole _console;
        private static bool _drawing;

        //The console goes away (the battle replay starts from it)
        public static void Close()
        {
            if (_console != null) _IsOpen(_console) = false;
        }

        public static void Begin(CheatingConsole console)
        {
            _console = console;
            _drawing = _IsOpen(console) && Application.isPlaying;
            if (!_drawing)
            {
                return;
            }
            Ensure();
            GUI.DrawTexture(_last, _background);
            if (Event.current.type == EventType.Repaint)
            {
                _right = 0f;
                _bottom = 0f;
            }
        }

        public static void Reached(Rect rect)
        {
            if (ConsoleText.Drawing && Event.current.type == EventType.Repaint)
            {
                _right = Mathf.Max(_right, rect.xMax);
                _bottom = Mathf.Max(_bottom, rect.yMax);
            }
        }

        public static void End()
        {
            if (!_drawing || Event.current == null)
            {
                return;
            }
            bool repaint = Event.current.type == EventType.Repaint && _bottom > 0f;
            if (repaint)
            {
                _contentBottom = _bottom;
            }

            //Our own entry under the console's sections (not in combat, where the console shows its combat toggles)
            float extra = 0f;
            if (!BattleReplay.InBattle)
            {
                Rect button = new Rect(10f, _contentBottom + 8f, 230f, 28f);
                if (GUI.Button(button, BattleReplay.Open ? "Battle replay  ▲" : "Battle replay  ▼"))
                {
                    BattleReplay.Open = !BattleReplay.Open;
                    if (BattleReplay.Open) Picker.CloseList();
                }
                extra = 8f + 28f;
            }

            if (repaint)
            {
                _last = new Rect(0f, 0f, Mathf.Max(_right, 240f) + Margin, _bottom + extra + Margin);
                Picker.PanelRight = _last.xMax;
                BattleReplay.PanelRight = _last.xMax;
            }
        }
    }

    [HarmonyPatch(typeof(CheatingConsole), "OnGUI")]
    internal static class CheatingConsole_OnGUI
    {
        private static void Prefix(CheatingConsole __instance)
        {
            ConsoleText.Drawing = true;
            ConsolePanel.Begin(__instance);
        }

        private static void Finalizer()
        {
            ConsolePanel.End();
            ConsoleText.Drawing = false;
        }
    }

    [HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(string) })]
    internal static class GUI_Toggle
    {
        private static void Prefix(Rect position, ref string text)
        {
            ConsoleText.Translate(ref text);
            ConsolePanel.Reached(new Rect(position.x, position.y, 230f, 24f));
        }
    }

    [HarmonyPatch(typeof(GUILayout), nameof(GUILayout.BeginArea), new[] { typeof(Rect), typeof(GUIStyle) })]
    internal static class GUILayout_BeginArea { private static void Prefix(Rect screenRect) { ConsolePanel.Reached(screenRect); } }

    [HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string) })]
    internal static class GUI_Button
    {
        private static void Prefix(Rect position, ref string text)
        {
            ConsoleText.Translate(ref text);
            ConsolePanel.Reached(position);
        }
    }

    [HarmonyPatch(typeof(GUILayout), nameof(GUILayout.Label), new[] { typeof(string), typeof(GUILayoutOption[]) })]
    internal static class GUILayout_Label { private static void Prefix(ref string text) { ConsoleText.Translate(ref text); } }

    [HarmonyPatch(typeof(GUILayout), nameof(GUILayout.Button), new[] { typeof(string), typeof(GUILayoutOption[]) })]
    internal static class GUILayout_Button { private static void Prefix(ref string text) { ConsoleText.Translate(ref text); } }
}
