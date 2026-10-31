using System.Collections.Generic;
using System.IO;
using System.Linq;

using BepInEx;
using BepInEx.Bootstrap;

using EnglishPatch.Hooks;

using Heluo.Data;

using UnityEngine;

namespace EnglishPatch
{
    /*
     * BepInEx/EnglishPatch_Diagnostics.log: what is installed and loaded on this PC, to find out why the game looks or
     * behaves differently than on another one. Started empty every time the game starts, only when DiagnosticLog is on.
     * At the start: versions and locations of the mod files, other copies of them in the game folder, the plugins
     * BepInEx loaded, the translation folders and the fonts. While playing: the translated text the game really got,
     * the game controllers Windows reports and every controller axis or button that moves (a held axis walks like W).
     */
    internal class Diagnostics : MonoBehaviour
    {
        private static readonly string _Path = Path.Combine(Paths.BepInExRootPath, "EnglishPatch_Diagnostics.log");

        //A broken controller can move an axis every frame, the log stops growing after this many lines
        private const int MaxLines = 3000;
        private static int _Lines;

        public static void Log(string line)
        {
            if (_Lines >= MaxLines)
            {
                return;
            }
            try
            {
                _Lines++;
                File.AppendAllText(_Path, System.DateTime.Now.ToString("HH:mm:ss ") + line + "\n");
                if (_Lines == MaxLines)
                {
                    File.AppendAllText(_Path, "(log full, nothing more is written)\n");
                }
            }
            catch (System.Exception)
            {
                //The log is only a help, a file that cannot be written is left alone
            }
        }

        public static void Begin(BaseUnityPlugin plugin)
        {
            try
            {
                File.WriteAllText(_Path, "English Patch diagnostics log\n");
            }
            catch (System.Exception)
            {
                return;
            }

            Section("System", LogSystem);
            Section("Mod files", LogModFiles);
            Section("Translation folders", LogTranslationFolders);
            //The fonts in Start: Unity's built-in Arial is not loaded yet while the plugins start
            plugin.gameObject.AddComponent<Diagnostics>();
        }

        //One part of the start report, a part that fails does not stop the others
        private static void Section(string title, System.Action log)
        {
            Log("---- " + title);
            try
            {
                log();
            }
            catch (System.Exception e)
            {
                Log("Could not read: " + e.Message);
            }
        }

        private static void LogSystem()
        {
            Log("OS: " + SystemInfo.operatingSystem + ", language " + Application.systemLanguage + ", culture " + System.Globalization.CultureInfo.CurrentCulture.Name);
            Log("Unity " + Application.unityVersion + ", game version " + Application.version + ", screen " + Screen.width + "x" + Screen.height
                + " (" + Screen.currentResolution.width + "x" + Screen.currentResolution.height + (Screen.fullScreen ? " full screen)" : " window)"));
            Log("Game folder: " + Paths.GameRootPath);
        }

        private static void LogModFiles()
        {
            Log("English Patch " + EnglishPatch.Version + ": " + Describe(typeof(EnglishPatch).Assembly.Location));
            Log("Mod API: " + Describe(typeof(ModAPI.ModAPI).Assembly.Location));

            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                Log("Plugin loaded: " + info.Metadata.Name + " " + info.Metadata.Version + " (" + info.Metadata.GUID + ") " + info.Location);
            }

            //Another copy of a mod DLL (an older install, a backup...) may be the one that is really loaded
            string[] modDlls = { "PoW_EnglishPatch", "PoW_ModAPI", "PathOfWuxiaEndingEnglish", "EnglishPatch", "ModAPI", "AutoTranslator" };
            foreach (string file in Directory.GetFiles(Paths.GameRootPath, "*.dll", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (modDlls.Any(dll => name.IndexOf(dll, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Log("Mod DLL in the game folder: " + Describe(file));
                }
            }

            foreach (string entry in Directory.GetFileSystemEntries(Paths.GameRootPath))
            {
                Log("Game folder entry: " + Path.GetFileName(entry) + (Directory.Exists(entry) ? "\\" : ""));
            }
        }

        private static void LogTranslationFolders()
        {
            //ModResources is the folder of this patch, Mods the one of the 2020 patch
            foreach (string folder in new[] { "ModResources", "Mods" })
            {
                string path = Path.Combine(Paths.GameRootPath, folder);
                if (!Directory.Exists(path))
                {
                    Log(folder + ": none");
                    continue;
                }
                foreach (string sub in Directory.GetDirectories(path))
                {
                    Log(folder + "\\" + Path.GetFileName(sub) + ": " + Directory.GetFiles(sub, "*", SearchOption.AllDirectories).Length + " files");
                }
            }

            string exterior = Path.Combine(Paths.GameRootPath, Path.Combine("ModResources", "EnglishTranslate", "Config", "chs", "textfiles", "CharacterExterior.txt"));
            if (File.Exists(exterior))
            {
                Log("CharacterExterior.txt " + new FileInfo(exterior).Length + " bytes, " + File.GetLastWriteTime(exterior).ToString("yyyy-MM-dd HH:mm"));
                string row = File.ReadAllLines(exterior).FirstOrDefault(line => line.StartsWith(CheckedId + "\t"));
                Log("Its " + CheckedId + " row: " + Shortened(row));
            }
            else
            {
                Log("CharacterExterior.txt: missing");
            }
        }

        private static void LogFonts()
        {
            string[] installed = Font.GetOSInstalledFontNames();
            Log(installed.Length + " fonts installed in Windows");
            Log("The game's font DFKai-SB installed: " + installed.Any(font => font == "DFKai-SB" || font == "標楷體"));
            Font font = TextUtil.UIFont;
            Log("Font setting '" + EnglishPatch.FontName + "', in use: " + (font != null ? string.Join(", ", font.fontNames ?? new string[0]) + " (" + font.name + ")" : "none"));
            Log("CombatFont setting '" + EnglishPatch.CombatFontName + "', DialogueFont setting '" + EnglishPatch.DialogueFontName + "'");
        }

        private static string Describe(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                return file ?? "unknown";
            }
            return file + " (" + new FileInfo(file).Length + " bytes, " + File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm") + ")";
        }

        private static string Shortened(string text)
        {
            if (text == null)
            {
                return "none";
            }
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }

        //Shangguan Xu, the description that looked like the 2020 translation
        private const string CheckedId = "in0110";

        //The axes the game reads (its InputManager), keyboard and controllers
        private static readonly string[] _Axes =
        {
            "Horizontal", "Vertical",
            "Xbox360 Left Horizontal", "Xbox360 Left Vertical", "Xbox360 Right Horizontal", "Xbox360 Right Vertical",
            "Xbox360 DPad Horizontal", "Xbox360 DPad Vertical",
            "PS4 Left Horizontal", "PS4 Left Vertical", "PS4 Right Horizontal", "PS4 Right Vertical",
            "PS4 DPad Horizontal", "PS4 DPad Vertical"
        };

        //Axis => last value written, rounded to a tenth. Axes this game does not define are dropped.
        private readonly Dictionary<string, float> _AxisValues = new Dictionary<string, float>();
        private readonly HashSet<KeyCode> _HeldButtons = new HashSet<KeyCode>();
        private string _Joysticks;
        private bool _KeyW;
        private bool _DataChecked;
        private float _NextDataCheck;

        private void Start()
        {
            Section("Fonts", LogFonts);

            foreach (string axis in _Axes)
            {
                try
                {
                    Input.GetAxisRaw(axis);
                    _AxisValues[axis] = 0f;
                }
                catch (System.Exception)
                {
                    Log("Axis not defined by the game: " + axis);
                }
            }
        }

        private void Update()
        {
            string joysticks = string.Join(" | ", Input.GetJoystickNames().Select(name => string.IsNullOrEmpty(name) ? "(empty slot)" : name).ToArray());
            if (joysticks != _Joysticks)
            {
                _Joysticks = joysticks;
                Log("Game controllers reported by Windows: " + (joysticks.Length > 0 ? joysticks : "none"));
            }

            bool keyW = Input.GetKey(KeyCode.W);
            if (keyW != _KeyW)
            {
                _KeyW = keyW;
                Log("Keyboard W " + (keyW ? "down" : "up"));
            }

            foreach (string axis in _AxisValues.Keys.ToList())
            {
                float value = Mathf.Round(Input.GetAxisRaw(axis) * 10f) / 10f;
                if (value != _AxisValues[axis])
                {
                    _AxisValues[axis] = value;
                    Log("Axis " + axis + " = " + value.ToString("0.0") + (axis == "Vertical" && value > 0f && !keyW ? " (W is not pressed: a controller moves it)" : ""));
                }
            }

            //Per controller (Joystick1Button0...), the generic JoystickButton0... would write every press twice
            for (KeyCode button = KeyCode.Joystick1Button0; button <= KeyCode.Joystick8Button19; button++)
            {
                bool held = Input.GetKey(button);
                if (held ? _HeldButtons.Add(button) : _HeldButtons.Remove(button))
                {
                    Log("Controller " + button + (held ? " down" : " up"));
                }
            }

            if (!_DataChecked && Time.unscaledTime >= _NextDataCheck)
            {
                _NextDataCheck = Time.unscaledTime + 2f;
                LogLoadedText();
            }
        }

        //The text the game really got for the checked row, once its data is loaded
        private void LogLoadedText()
        {
            try
            {
                CharacterExterior exterior = Heluo.Game.Data?.Get<CharacterExterior>(CheckedId);
                if (exterior != null)
                {
                    _DataChecked = true;
                    Log("---- Loaded data");
                    Log("Game text of " + CheckedId + ": " + Shortened(exterior.Description));
                }
            }
            catch (System.Exception)
            {
                //The game data is not ready yet, tried again later
            }
        }
    }
}
