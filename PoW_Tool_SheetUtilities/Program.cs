using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using PoW_Tool_SheetUtilities.Handler;
using PoW_Tool_SheetUtilities.MachineTranslator;

using Google.Apis.Sheets.v4.Data;

namespace PoW_Tool_SheetUtilities
{
    internal class Program
    {
        private static void UpdateSpreadsheets()
        {
            SpreadsheetUpdater.UpdateSpreadsheetsFromGameFiles();
        }

        private static void CheckAssetFormat(string fileName = null)
        {
            if (fileName == null)
            {
                Console.WriteLine("Enter File Name:");
                fileName = Console.ReadLine();
            }

            var filePath = Environment.CurrentDirectory + Path.DirectorySeparatorChar + "Input" + Path.DirectorySeparatorChar + "chs" + Path.DirectorySeparatorChar + "textfiles" + Path.DirectorySeparatorChar + fileName + ".bytes";

            string line;
            int variableAmount = 0;

            string[] exampleEntry = null;
            System.IO.StreamReader reader = new System.IO.StreamReader(filePath);
            while ((line = reader.ReadLine()) != null)
            {
                string[] data = line.Split('\t');
                variableAmount = data.Length;
                if (exampleEntry == null)
                {
                    exampleEntry = new string[variableAmount];
                }
                for (int i = 0; i < variableAmount; i++)
                {
                    if (!string.IsNullOrEmpty(data[i]) && (string.IsNullOrEmpty(exampleEntry[i]) || exampleEntry[i] == "0" || data[i].Length > exampleEntry[i].Length))
                    {
                        exampleEntry[i] = data[i];
                    }
                }
            }

            if (exampleEntry != null)
            {
                Console.WriteLine("Variable count: " + variableAmount);
                Console.WriteLine("Example values (this is not an existing entry, but glued together so all values are as representative as possible): ");
                for (int i = 0; i < variableAmount; i++)
                {
                    Console.WriteLine("\t\t" + i + ": " + exampleEntry[i]);
                }
            }

            reader.Close();
        }

        private static void Export(string target = null, string sheetFilter = null)
        {
            string workingDirectory = Environment.CurrentDirectory;
            if (target == null)
            {
                Console.WriteLine("Where to export to?");
                Console.WriteLine("     1: /Output");
                Console.WriteLine("     2: ../../Mod/ModResources/EnglishTranslate/Config");

                target = Console.ReadKey().KeyChar.ToString();
                Console.WriteLine("\r          \n");
            }

            string outputFolder;
            switch (target)
            {
                case "1":
                case "output":
                    outputFolder = workingDirectory + Path.DirectorySeparatorChar + "Output";
                    break;

                case "2":
                case "mod":
                    outputFolder = workingDirectory + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "Mod/ModResources/EnglishTranslate/Config/";
                    break;

                default:
                    Console.WriteLine("ERROR: Invalid option!");
                    return;
            }

            SpreadsheetUpdater.ExportToMod(outputFolder, sheetFilter);
        }

        //Cell colors the translators actually use, as documented on the Legend tab of the spreadsheets.
        //Anything not listed here lands in the catch all bucket and is printed with its hex value.
        private static readonly string[] ProofReadColors = { "C9DAF8", "00FFFF", "A4C2F4", "6D9EEB", "3C78D8" };

        private static readonly string[] TranslatedColors = { "B6D7A8", "93C47D", "D9EAD3", "34A853" };

        private static readonly string[] NeedsCheckColors = { "CCCC05" };

        //FFA800 is what the tool writes, FF9900 is the swatch the Legend tab shows
        private static readonly string[] MachineTranslatedColors = { "FFA800", "FF9900" };

        private static readonly string[] ManuallyMarkedBadColors = { "FF0000" };

        private static void AddColors(TranslationStatEntry entry, string[] hexColors)
        {
            foreach (string hex in hexColors)
            {
                entry.AcceptableColors.Add(ColorHelper.FromHex(hex));
            }
        }

        private static List<Color> AllTranslatedColors()
        {
            List<Color> colors = new List<Color>();
            foreach (string hex in ProofReadColors)
            {
                colors.Add(ColorHelper.FromHex(hex));
            }
            foreach (string hex in TranslatedColors)
            {
                colors.Add(ColorHelper.FromHex(hex));
            }

            return colors;
        }

        private static void GetTranslationStats(string sheetFilter = null)
        {
            List<TranslationStatEntry> stats = new List<TranslationStatEntry>();

            TranslationStatEntry proofReadStats = new TranslationStatEntry("Proofread");
            AddColors(proofReadStats, ProofReadColors);
            stats.Add(proofReadStats);

            TranslationStatEntry translatedStats = new TranslationStatEntry("Translated");
            AddColors(translatedStats, TranslatedColors);
            stats.Add(translatedStats);

            TranslationStatEntry manualCheckingReqStats = new TranslationStatEntry("Needs manual Checking (usually due to game update)");
            AddColors(manualCheckingReqStats, NeedsCheckColors);
            stats.Add(manualCheckingReqStats);

            TranslationStatEntry mtlStats = new TranslationStatEntry("Machine Translated");
            AddColors(mtlStats, MachineTranslatedColors);
            stats.Add(mtlStats);

            TranslationStatEntry markedBadStats = new TranslationStatEntry("Manually marked as bad (red)");
            AddColors(markedBadStats, ManuallyMarkedBadColors);
            stats.Add(markedBadStats);

            //Catch all, has to stay last: it takes every line whose color none of the buckets above knows
            TranslationStatEntry unknownStats = new TranslationStatEntry("Marked in unknown cell color")
            {
                MatchAll = true
            };
            stats.Add(unknownStats);

            SpreadsheetUpdater.GetTranslationStats(ref stats, sheetFilter);

            Console.WriteLine("");
            int totalLines = 0;
            foreach (TranslationStatEntry statEntry in stats)
            {
                totalLines += statEntry.LineCount;
                Console.WriteLine(statEntry.Name.PadRight(52) + " Lines: " + statEntry.LineCount.ToString().PadLeft(7) + " (Words: " + statEntry.WordCount.ToString() + " )");
            }
            Console.WriteLine("Marked lines in total".PadRight(52) + " Lines: " + totalLines.ToString().PadLeft(7));

            //Unknown colors are a bug report: either the color belongs in a bucket above or the sheet needs fixing
            if (unknownStats.LineCount > 0)
            {
                Console.WriteLine("");
                Console.WriteLine("Unknown cell colors (add them to a bucket in Program.cs or fix the sheet):");
                foreach (var countedColor in unknownStats.CountedColors)
                {
                    Console.WriteLine("     " + countedColor.Key + ": " + countedColor.Value + " lines");
                }
            }
        }

        private static void ExportTranslatedToCSV(string sheetFilter = null)
        {
            List<Color> acceptableColors = AllTranslatedColors();

            string workingDirectory = Environment.CurrentDirectory;
            string outPath = workingDirectory + Path.DirectorySeparatorChar + "ExportOutput";
            SpreadsheetUpdater.ExportTranslatedLinesToCSV(outPath, ref acceptableColors, sheetFilter);
        }


        private static void TestTranslation()
        {
            string PreContext = "凭你那点Cheap Tricks，也敢做我们的对手？哈哈哈！一边凉快去吧！";
            string Text = "Earth Dragon Sect没教过你们礼数？";
            string PostContext = "你⋯⋯什麽意思?";

            DeepL_Website translator = new DeepL_Website();

            while (true)
            {
                for (int i = 0; i < 150; ++i)
                {
                    var req = new TranslationRequest(Text, new string[] { PreContext }, new string[] { PostContext }, Text);
                    translator.AddTranslationRequest(ref req);
                }
                Task t = translator.ForceTranslate();
                t.Wait();
            }
        }

        private static string TestRegexPattern = @"{ \\""BattleResultAddSecondaryGoal\\"" : \\"".*\\""}";
        private static Regex TestRegex = new Regex(TestRegexPattern);

        private static void TestRegexInput()
        {
            string testText = File.ReadAllText("RegexTestInput.txt");

            var m = TestRegex.Match(testText);
            while (m.Success)
            {
                Console.WriteLine(m.ToString());
                m = m.NextMatch();
            }
        }

        private const string UsageText =
            "Usage: SheetUtilities [command] [options]\n" +
            "\n" +
            "Commands (no command starts the interactive menu):\n" +
            "     update              Update the spreadsheets from the game files in ./Input\n" +
            "     build <output|mod>  Build the English Mod data from the spreadsheets\n" +
            "     formats <asset>     Print the variable format of ./Input/chs/textfiles/<asset>.bytes\n" +
            "     stats               Print the translation statistics\n" +
            "     csv                 Export the translated lines to ./ExportOutput\n" +
            "     sheets              List the known sheet names\n" +
            "\n" +
            "Options:\n" +
            "     --sheet <names>     Restrict update, build, stats and csv to a comma separated list of sheets\n" +
            "\n" +
            "Example: SheetUtilities stats --sheet Talk";

        private static int RunCommand(string[] args)
        {
            string command = args[0].TrimStart('-').ToLowerInvariant();
            string sheetFilter = null;
            string target = null;

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--sheet" || arg == "-s")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.WriteLine("ERROR: --sheet needs a value!");
                        return 1;
                    }

                    sheetFilter = args[++i];
                }
                else if (arg.StartsWith("--sheet="))
                {
                    sheetFilter = arg.Substring("--sheet=".Length);
                }
                else
                {
                    target = arg;
                }
            }

            switch (command)
            {
                case "update":
                    Console.WriteLine("Updating spreadsheets after game update...");
                    SpreadsheetUpdater.UpdateSpreadsheetsFromGameFiles(sheetFilter);
                    return 0;

                case "build":
                    if (target == null)
                    {
                        Console.WriteLine("ERROR: build needs a target, either output or mod!");
                        return 1;
                    }

                    Console.WriteLine("Building English Mod data from spreadsheets...");
                    Export(target, sheetFilter);
                    return 0;

                case "formats":
                    if (target == null)
                    {
                        Console.WriteLine("ERROR: formats needs an asset name!");
                        return 1;
                    }

                    Console.WriteLine("Checking Asset Formats...");
                    CheckAssetFormat(target);
                    return 0;

                case "stats":
                    Console.WriteLine("Getting Translation Stats...");
                    GetTranslationStats(sheetFilter);
                    return 0;

                case "csv":
                    Console.WriteLine("Exporting to CSV...");
                    ExportTranslatedToCSV(sheetFilter);
                    return 0;

                case "sheets":
                    foreach (string name in SpreadsheetUpdater.KnownSheetNames())
                    {
                        Console.WriteLine(name);
                    }
                    return 0;

                case "help":
                case "h":
                case "?":
                    Console.WriteLine(UsageText);
                    return 0;

                default:
                    Console.WriteLine("ERROR: Unknown command '" + command + "'!");
                    Console.WriteLine("");
                    Console.WriteLine(UsageText);
                    return 1;
            }
        }

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            //Non interactive mode, so a single sheet can be checked without driving the menu
            if (args != null && args.Length > 0)
            {
                try
                {
                    return RunCommand(args);
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: " + e.Message);
                    return 1;
                }
            }

            Console.WriteLine("Do you want to update the spreadsheets after a game update or do you want to build the English Mod data from the spreadsheets?");
            Console.WriteLine("     1: Update after game update");
            Console.WriteLine("     2: Build English Mod data");
            Console.WriteLine("     3: Get Asset Formats");
            Console.WriteLine("     4: Get Translation Stats");
            Console.WriteLine("     5: Export to CSV");

            var input = Console.ReadKey().KeyChar;
            Console.WriteLine("\r          \n");
            switch (input)
            {
                case '1':
                    Console.WriteLine("Updating spreadsheets after game update...");
                    UpdateSpreadsheets();
                    break;

                case '2':
                    Console.WriteLine("Building English Mod data from spreadsheets...");
                    Export();
                    break;

                case '3':
                    Console.WriteLine("Checking Asset Formats...");
                    CheckAssetFormat();
                    break;

                case '4':
                    Console.WriteLine("Getting Translation Stats...");
                    GetTranslationStats();
                    break;
                
                case '5':
                    Console.WriteLine("Exporting to CSV...");
                    ExportTranslatedToCSV();
                    break;

                default:
                    Console.WriteLine("ERROR: Invalid option!");
                    break;
            }

            Console.WriteLine("Finished! Press any button to exit...");
            Console.ReadKey();
            return 0;
        }
    }
}