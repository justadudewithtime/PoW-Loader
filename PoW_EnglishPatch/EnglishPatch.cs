using System.IO;

using BepInEx;
using BepInEx.Configuration;

using HarmonyLib;

using ModAPI;

namespace EnglishPatch
{
    [BepInPlugin("plugins.englishpatch", "English Patch", "0.9.0")]
    [BepInDependency("plugins.modapi", BepInDependency.DependencyFlags.HardDependency)]
    [BepInProcess("PathOfWuxia.exe")]
    public class EnglishPatch : BaseUnityPlugin, IPoWMod
    {
        private static readonly string _VERSION = "0.9.0";

        public static string Version => _VERSION;

        public string GetVersion()
        {
            return _VERSION;
        }

        public string GetName()
        {
            return "English Patch";
        }

        private Harmony _HM;

        //Arial is the font the English texts always had, so updating the mod does not change how the game looks
        private const string DefaultFont = "Arial";

        //Font for the English texts, from BepInEx/config/plugins.englishpatch.cfg
        public static string FontName { get; private set; } = DefaultFont;

        //Font for the combat texts that use the game's own font (DFKai-SB), empty keeps that font
        public static string CombatFontName { get; private set; } = "";

        //Hides the dark tab behind the labels under the buff and debuff icons in combat
        public static bool HideBuffLabelBackground { get; private set; } = true;

        //Font and size of the dialogue text in the talk box, empty and 0 keep the English font and its size
        public static string DialogueFontName { get; private set; } = "";
        public static int DialogueFontSize { get; private set; } = 0;

        //Writes BepInEx/EnglishPatch_Layout.log, how some texts were measured and moved, to report layout problems
        public static bool LayoutLog { get; private set; } = false;

        //Writes BepInEx/EnglishPatch_Diagnostics.log, what is installed and loaded and the controller input, to report problems on another PC
        public static bool DiagnosticLog { get; private set; } = true;

        //Values of the ChristmasTitle setting
        private const string ChristmasAuto = "Auto";
        private const string ChristmasAlways = "Always";
        private const string ChristmasNever = "Never";

        public void Load()
        {
            ModAPI.ModAPI.GetInstance().ResourceRedirector.AddRessourceFolder("ModResources" + Path.DirectorySeparatorChar + "EnglishTranslate");

            _HM = new Harmony("EnglishPatch");
            //Patching hook by hook, so one hook broken by a game update does not stop all the others
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(EnglishPatch).Assembly))
            {
                try
                {
                    _HM.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError("[EnglishPatch] Could not apply hook " + type.FullName + ": " + e.Message);
                }
            }
        }

        public void Unload()
        {
            _HM.UnpatchAll("EnglishPatch");
        }

        private void Awake()
        {
            this.name = GetName();
            FontName = Config.Bind("Text", "Font", DefaultFont,
                "Font for the English texts. Any font installed in Windows works, by its name as shown in the Windows font list " +
                "(for example Palatino Linotype, Book Antiqua, Constantia, Georgia, Cambria, Arial). " +
                "A font that is not installed falls back to Arial. Restart the game after changing it.").Value;
            CombatFontName = Config.Bind("Text", "CombatFont", "",
                "Font for the combat texts that are written in the game's own font, DFKai-SB (skill names, victory and failure " +
                "conditions...). Leave it empty to keep the game's font. Otherwise any font installed in Windows, by its name " +
                "as for Font (for example Arial). A font that is not installed falls back to Arial. Restart the game after changing it.").Value;
            DialogueFontName = Config.Bind("Text", "DialogueFont", "",
                "Font for the dialogue text in the talk box only. Leave it empty to use Font. Otherwise any font installed in Windows, " +
                "by its name as for Font. A font that is not installed falls back to Arial. Restart the game after changing it.").Value;
            DialogueFontSize = Config.Bind("Text", "DialogueFontSize", 0,
                "Size of the dialogue text in the talk box. 0 keeps the size of the other English texts (20). Restart the game after changing it.").Value;
            HideBuffLabelBackground = Config.Bind("Text", "HideBuffLabelBackground", true,
                "Hide the dark background behind the labels under the buff and debuff icons in combat (\"Regeneration Lv 3\"). " +
                "Restart the game after changing it.").Value;

            LayoutLog = Config.Bind("Debug", "LayoutLog", false,
                "Write BepInEx/EnglishPatch_Layout.log: how the turn counter and the skill key numbers in combat were measured " +
                "and moved. Only useful to report a text that is misplaced or missing. Restart the game after changing it.").Value;
            DiagnosticLog = Config.Bind("Debug", "DiagnosticLog", true,
                "Write BepInEx/EnglishPatch_Diagnostics.log: versions and locations of the mod files, the translation folders, " +
                "the fonts, and every game controller axis or button that moves. Send this file with a problem report, it shows " +
                "what differs from another PC. Set it to false to stop writing it. Restart the game after changing it.").Value;
            //Which build runs, in LogOutput.log, whatever the setting
            Logger.LogInfo("English Patch " + _VERSION + ", build " + Diagnostics.Build + ", diagnostic log " + (DiagnosticLog ? "on" : "off (DiagnosticLog in BepInEx/config/plugins.englishpatch.cfg)"));
            if (DiagnosticLog)
            {
                Diagnostics.Begin(this, Logger);
            }

            //The title of the main menu has a Christmas version (Main_title_Christmas.png), shown in December by default
            string christmasTitle = Config.Bind("Seasonal", "ChristmasTitle", ChristmasAuto, new ConfigDescription(
                "Christmas version of the title on the main menu. Auto: only in December (by the date of this PC). " +
                "Always: all year. Never: never. Restart the game after changing it.",
                new AcceptableValueList<string>(ChristmasAuto, ChristmasAlways, ChristmasNever))).Value;
            if (christmasTitle == ChristmasAlways || (christmasTitle == ChristmasAuto && System.DateTime.Now.Month == 12))
            {
                ModAPI.ModAPI.GetInstance().ResourceRedirector.ImageVariant = "Christmas";
            }
            //Adding it to ModAPI list
            ModAPI.ModAPI.GetInstance().AddMod(this);
        }
    }
}