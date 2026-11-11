using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using HarmonyLib.Tools;
using Heluo;
using Heluo.UI;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PathOfWuxiaEndingEnglish
{
    [BepInPlugin(modGUID, modName, modVersion)]
    [BepInDependency(englishPatchGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("PathOfWuxia.exe")]
    public class PathOfWuxiaEndingEnglish : BaseUnityPlugin
    {
        private const string modGUID = "PathOfWuxiaEndingEnglish";
        private const string modName = "PathOfWuxiaEndingEnglish";
        private const string modVersion = "0.0.0.1";

        // The font of the endings is set in the English Patch's config file (EndingFont, else Font)
        private const string englishPatchGUID = "plugins.englishpatch";

        private readonly Harmony harmony = new Harmony(modGUID);

        private readonly char directorySeparatorChar = Path.DirectorySeparatorChar;

        // Developer mode enables certain commands that assist with debugging ending translations.
        // Pressing J generates a csv of all ending text based on the <EndingIds> file
        // Pressing H plays all endings. These endings can be played from anywhere, even in the title screen.
        private const bool developerMode = false;

        // There are a few ways to disable ending translations.
        // This may be useful for grabbing original text
        // Option 1: Set this variable to true.
        // Option 2: Remove the translation csv.
        private const bool blockTranslation = false;

        // a list of ending ids, taken from a file.
        // note, ending ids were retrieved manually through extracting the prefabs
        private List<string> endingIds;

        void Awake()
        {
            HarmonyFileLog.Enabled = developerMode;
        }

        void Start()
        {
            // populate endingIds
            endingIds = new List<string>();
            try
            {
                StreamReader reader = new StreamReader($"ModResources{directorySeparatorChar}EnglishTranslate{directorySeparatorChar}Config{directorySeparatorChar}chs{directorySeparatorChar}textfiles{directorySeparatorChar}EndingIds.txt");
                string endingId;
                while ((endingId = reader.ReadLine()) != null)
                {
                    endingIds.Add(endingId);
                }
                reader.Close();
            }catch(Exception ex)
            {
                FileLog.Log("Exception likely due to EndingIds.txt missing. " + ex.Message);
            }

            // populate translation dictionary, where the key is the id of the text and the value is the translation
            Dictionary<string, string> endingTranslationDict = new Dictionary<string, string>();
            try
            {
                StreamReader reader = new StreamReader($"ModResources{directorySeparatorChar}EnglishTranslate{directorySeparatorChar}Config{directorySeparatorChar}chs{directorySeparatorChar}textfiles{directorySeparatorChar}EndingTranslations.csv");
                string translationLine;
                while ((translationLine = reader.ReadLine()) != null)
                {
                    ParseCsvEndingTranslation(endingTranslationDict, translationLine);
                }
                reader.Close();
            }
            catch (Exception ex)
            {
                FileLog.Log("Exception likely due to EndingTranslations.csv missing. " + ex.Message);
            }

            // translate endings based on translation dictionary
            if (!blockTranslation)
            {
                foreach (string endingIdIter in endingIds)
                {
                    // Note, do not use <directorySeparatorChar> in Game.Resource.Load, it breaks Heluo's function
                    string pathEnding = $"UI/UIFrom/UIEnding_chs/{endingIdIter}.prefab";
                    GameObject endingObj = Game.Resource.Load<GameObject>(pathEnding);

                    if(endingObj == null)
                    {
                        continue;
                    }

                    // Location 1, endingObj.Text.Text00, single text, very commonly found as titles
                    Text titleText = endingObj.transform.Find("Text")?.Find("Text00")?.GetComponent<Text>();
                    if (titleText != null && endingTranslationDict.ContainsKey($"{endingIdIter} title"))
                    {
                        titleText.text = endingTranslationDict[$"{endingIdIter} title"];
                        FormatText(titleText, 16);
                    }

                    // Location 2, endingObj.Text.Grouop, list of text, very commonly found as paragraph text
                    Transform lineParent = endingObj.transform.Find("Text")?.Find("Grouop");
                    if (lineParent != null)
                    {
                        foreach (Transform lineTran in lineParent)
                        {
                            if (endingTranslationDict.ContainsKey($"{endingIdIter} line {lineTran.name}"))
                            {
                                Text lineTranText = lineTran.GetComponent<Text>();
                                lineTranText.text = endingTranslationDict[$"{endingIdIter} line {lineTran.name}"];
                                FormatText(lineTranText, 16);
                            }
                        }
                    }

                    // Location 3, endingObj.Text.Grouop01, list of text, used when Grouop is full, found in ending0106f_in0209dead_sub
                    Transform lineParent2 = endingObj.transform.Find("Text")?.Find("Grouop01");
                    if (lineParent2 != null)
                    {
                        foreach (Transform lineTran2 in lineParent2)
                        {
                            if (endingTranslationDict.ContainsKey($"{endingIdIter} line2 {lineTran2.name}"))
                            {
                                Text lineTran2Text = lineTran2.GetComponent<Text>();
                                lineTran2Text.text = endingTranslationDict[$"{endingIdIter} line2 {lineTran2.name}"];
                                FormatText(lineTran2Text, 16);
                            }
                        }
                    }

                    // Location 4, endingObj, list of text, found in ending0104_b
                    // note, a few lines are hard to see in white, we change the colour to black
                    foreach (Transform endingObjChild in endingObj.transform)
                    {
                        Text cinematicText = endingObjChild.GetComponent<Text>();
                        if (cinematicText != null && endingTranslationDict.ContainsKey($"{endingIdIter} cinematic {endingObjChild.name}"))
                        {
                            cinematicText.text = endingTranslationDict[$"{endingIdIter} cinematic {endingObjChild.name}"];
                            FormatText(cinematicText, 72);
                        }
                    }

                    Game.Resource.Unload(pathEnding);
                }
            }
        }

        void Update()
        {
            // plays all endings in EndingIds
            if (developerMode && Input.GetKeyDown(KeyCode.H))
            {
                // loading ui ending seems to be what's causing the black screen
                UIEnding uiEnding = Game.UI.Open<UIEnding>();

                foreach(string endingId in endingIds)
                {
                    uiEnding.AddEnding(endingId);
                }

                uiEnding.AutoFinish = false;
                uiEnding.NextEnding();
            }

            // generates a csv for all endings in the file EndingIds
            if (developerMode && Input.GetKeyDown(KeyCode.J))
            {
                // write the ending text into a file
                StreamWriter writer = new StreamWriter($"ModResources{directorySeparatorChar}EnglishTranslateRaw{directorySeparatorChar}EndingTranslations.csv");
                writer.WriteLine("TextId,Translated,Original");

                foreach(string endingIdIter in endingIds)
                {
                    // Note, do not use <directorySeparatorChar> in Game.Resource.Load, it breaks Heluo's function
                    string pathEnding = $"UI/UIFrom/UIEnding_chs/{endingIdIter}.prefab";
                    GameObject endingObj = Game.Resource.Load<GameObject>(pathEnding);

                    if (endingObj == null)
                    {
                        continue;
                    }

                    // Location 1, endingObj.Text.Text00, single text, very commonly found as titles
                    Text titleText = endingObj.transform.Find("Text")?.Find("Text00")?.GetComponent<Text>();
                    if(titleText != null)
                    {
                        string titleStr = RemoveNewLine(titleText.text);
                        writer.WriteLine($"{endingIdIter} title,,\"{titleStr}\"");
                    }

                    // Location 2, endingObj.Text.Grouop, list of text, very commonly found as paragraph text
                    Transform lineParent = endingObj.transform.Find("Text")?.Find("Grouop");
                    if (lineParent != null)
                    {
                        foreach (Transform lineTran in lineParent)
                        {
                            string lineStr = RemoveNewLine(lineTran.GetComponent<Text>().text);
                            writer.WriteLine($"{endingIdIter} line {lineTran.name},,\"{lineStr}\"");
                        }
                    }

                    // Location 3, endingObj.Text.Grouop01, list of text, used when Grouop is full, found in ending0106f_in0209dead_sub
                    Transform lineParent2 = endingObj.transform.Find("Text")?.Find("Grouop01");
                    if (lineParent2 != null)
                    {
                        foreach (Transform lineTran2 in lineParent2)
                        {
                            string lineStr2 = RemoveNewLine(lineTran2.GetComponent<Text>().text);
                            writer.WriteLine($"{endingIdIter} line2 {lineTran2.name},,\"{lineStr2}\"");
                        }
                    }

                    // Location 4, endingObj, list of text, found in ending0104_b
                    foreach (Transform endingObjChild in endingObj.transform)
                    {
                        string cinematicText = RemoveNewLine(endingObjChild.GetComponent<Text>()?.text);
                        if(cinematicText != null)
                        {
                            writer.WriteLine($"{endingIdIter} cinematic {endingObjChild.name},,\"{cinematicText}\"");
                        }
                    }

                    // ending0103 does not have any text

                    Game.Resource.Unload(pathEnding);
                }

                writer.Close();
            }
        }

        // Removes new line characters from <text>. This is used to clean text.
        private static string RemoveNewLine(string text)
        {
            return text?.Replace("\n", "")?.Replace("\r", "");
        }

        /**
         * Sets <text> to the ending font with a font size of <fontSize>.
         * This function allows english text to fit more easily.
         * Note, overflow behaviours are not specified to keep unity's word wrapping and auto shrink feature.
         */
        private static void FormatText(Text text, int fontSize)
        {
            text.font = EndingFont;
            text.fontSize = fontSize;
        }

        private static Font endingFont;

        // The font chosen in the English Patch's config file: EndingFont, or Font when it is empty. Arial without the English Patch,
        // or when the font is not installed. Looked up once.
        private static Font EndingFont
        {
            get
            {
                if (endingFont == null)
                {
                    string name = EnglishPatchSetting("EndingFont");
                    if (string.IsNullOrEmpty(name))
                    {
                        name = EnglishPatchSetting("Font");
                    }
                    endingFont = LoadFont(name);
                }
                return endingFont;
            }
        }

        // A [Text] setting of the English Patch, empty when the English Patch or the setting is missing
        private static string EnglishPatchSetting(string key)
        {
            if (Chainloader.PluginInfos.TryGetValue(englishPatchGUID, out PluginInfo info) && info.Instance != null
                && info.Instance.Config.TryGetEntry("Text", key, out ConfigEntry<string> entry))
            {
                return entry.Value?.Trim() ?? "";
            }
            return "";
        }

        // An installed Windows font by its name (spaces, dashes and capitals ignored), with Arial behind it for missing characters
        private static Font LoadFont(string name)
        {
            Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (string.IsNullOrEmpty(name) || name.Equals("Arial", StringComparison.OrdinalIgnoreCase))
            {
                return arial;
            }

            string wanted = Simplified(name);
            foreach (string installed in Font.GetOSInstalledFontNames())
            {
                if (Simplified(installed) == wanted)
                {
                    Font font = Font.CreateDynamicFontFromOSFont(new[] { installed, "Arial" }, 16);
                    if (font != null)
                    {
                        return font;
                    }
                }
            }
            FileLog.Log("Ending font '" + name + "' is not installed, using Arial");
            return arial;
        }

        private static string Simplified(string fontName)
        {
            return fontName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
        }

        /**
         * Parse <endingTranslationLine> and add the translation into <endingTranslationDict>
         * 
         * The csv is read line by line creating <endingTranslationLine>
         * <endingTranslationLine> should be in tsv format where:
         * Column 1 contains the identifier
         * Column 2 contains the translated text
         */
        private static void ParseCsvEndingTranslation(Dictionary<string, string> endingTranslationDict, string endingTranslationLine)
        {
            string[] columns = endingTranslationLine.Split('\t');
            int columnNum = columns.Length;

            // if there are not 2 columns, guaranteed invalid
            if (columnNum != 2)
            {
                return;
            }

            string textId = columns[0];
            string textTranslation = columns[1];

            // NEWLINE substitutes for a new line character.
            textTranslation = textTranslation.Replace("NEWLINE", "\n");

            endingTranslationDict.Add(textId, textTranslation);
        }
    }
}
