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

        //Kept open while the game runs, every line is flushed so the log is complete even after a crash
        private static StreamWriter _Writer;

        //A broken controller can move an axis every frame, the log stops growing after this many lines
        private const int MaxLines = 3000;
        private static int _Lines;

        public static void Log(string line)
        {
            if (_Writer == null || _Lines >= MaxLines)
            {
                return;
            }
            try
            {
                _Lines++;
                _Writer.Write(System.DateTime.Now.ToString("HH:mm:ss ") + line + "\n");
                if (_Lines == MaxLines)
                {
                    _Writer.Write("(log full, nothing more is written)\n");
                }
            }
            catch (System.Exception)
            {
                //The log is only a help, a file that cannot be written is left alone
            }
        }

        private static string _Build;

        //The start of the SHA-256 of this DLL, tells two builds of the same size apart. Worked out once.
        public static string Build
        {
            get
            {
                if (_Build == null)
                {
                    try
                    {
                        using (var sha = System.Security.Cryptography.SHA256.Create())
                        {
                            byte[] hash = sha.ComputeHash(File.ReadAllBytes(typeof(EnglishPatch).Assembly.Location));
                            _Build = System.BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
                        }
                    }
                    catch (System.Exception)
                    {
                        _Build = "unknown";
                    }
                }
                return _Build;
            }
        }

        //logger: BepInEx's log of the plugin, LogOutput.log says whether this file could be written
        public static void Begin(BaseUnityPlugin plugin, BepInEx.Logging.ManualLogSource logger)
        {
            try
            {
                _Writer = new StreamWriter(_Path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                _Writer.Write("English Patch diagnostics log\n");
            }
            catch (System.Exception e)
            {
                logger.LogWarning("Could not write the diagnostic log " + _Path + ": " + e.Message);
                return;
            }
            logger.LogInfo("Diagnostic log: " + _Path);

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
            Log("English Patch " + EnglishPatch.Version + ", build " + Build + ": " + Describe(typeof(EnglishPatch).Assembly.Location));
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

        //The axes this game defines and the last value written for each, in half steps: a stick or pedal resting
        //off-centre shows, small moves of a stick do not fill the log
        private string[] _DefinedAxes = new string[0];
        private float[] _AxisValues = new float[0];
        private readonly HashSet<KeyCode> _HeldButtons = new HashSet<KeyCode>();
        private string _Joysticks;
        private int _Controllers;
        private float _NextControllerCheck;
        private bool _KeyW;
        private bool _DataChecked;
        private float _NextDataCheck;

        //Asking Windows for the controllers can take long, the game itself does it once. Every few seconds is enough to see one plugged in.
        private const float ControllerCheckInterval = 2f;

        //Buttons per controller in Unity's KeyCode (Joystick1Button0 to Joystick1Button19)
        private const int ButtonsPerController = 20;

        private void Start()
        {
            Section("Fonts", LogFonts);

            var defined = new List<string>();
            foreach (string axis in _Axes)
            {
                try
                {
                    Input.GetAxisRaw(axis);
                    defined.Add(axis);
                }
                catch (System.Exception)
                {
                    Log("Axis not defined by the game: " + axis);
                }
            }
            _DefinedAxes = defined.ToArray();
            _AxisValues = new float[_DefinedAxes.Length];
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _NextControllerCheck)
            {
                _NextControllerCheck = now + ControllerCheckInterval;
                CheckControllers();
            }

            //The input is only watched while a controller is plugged in, without one nothing moves the axes
            if (_Controllers > 0)
            {
                LogInput();
            }

            if (!_DataChecked && now >= _NextDataCheck)
            {
                _NextDataCheck = now + 2f;
                LogLoadedText();
            }
        }

        private void CheckControllers()
        {
            string[] names = Input.GetJoystickNames();
            string joysticks = string.Join(" | ", names.Select(name => string.IsNullOrEmpty(name) ? "(empty slot)" : name).ToArray());
            //Unity keeps an empty slot for a controller that was unplugged
            _Controllers = names.Any(name => !string.IsNullOrEmpty(name)) ? Mathf.Min(names.Length, 8) : 0;
            if (joysticks != _Joysticks)
            {
                _Joysticks = joysticks;
                Log("Game controllers reported by Windows: " + (joysticks.Length > 0 ? joysticks : "none"));
            }
        }

        private void LogInput()
        {
            bool keyW = Input.GetKey(KeyCode.W);
            if (keyW != _KeyW)
            {
                _KeyW = keyW;
                Log("Keyboard W " + (keyW ? "down" : "up"));
            }

            for (int i = 0; i < _DefinedAxes.Length; i++)
            {
                float value = Mathf.Round(Input.GetAxisRaw(_DefinedAxes[i]) * 2f) / 2f;
                if (value != _AxisValues[i])
                {
                    _AxisValues[i] = value;
                    Log("Axis " + _DefinedAxes[i] + " = " + value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + (_DefinedAxes[i] == "Vertical" && value > 0f && !keyW ? " (W is not pressed: a controller moves it)" : ""));
                }
            }

            //Per controller (Joystick1Button0...), the generic JoystickButton0... would write every press twice
            KeyCode last = KeyCode.Joystick1Button0 + _Controllers * ButtonsPerController - 1;
            for (KeyCode button = KeyCode.Joystick1Button0; button <= last; button++)
            {
                bool held = Input.GetKey(button);
                if (held ? _HeldButtons.Add(button) : _HeldButtons.Remove(button))
                {
                    Log("Controller " + button + (held ? " down" : " up"));
                }
            }
        }

        private void OnApplicationQuit()
        {
            _Writer?.Dispose();
            _Writer = null;
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
