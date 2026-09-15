using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Google.Apis.Sheets.v4.Data;
using PoW_Tool_SheetUtilities.Handler;
using PoW_Tool_SheetUtilities.Handler.BattleAssets;
using PoW_Tool_SheetUtilities.Handler.BufferAssets;
using PoW_Tool_SheetUtilities.Handler.TextAssets;

namespace PoW_Tool_SheetUtilities
{
    internal class SpreadsheetUpdater
    {
        private static readonly List<AssetHandler> _Handlers = new List<AssetHandler>()
        {
            new SkillAssetHandler(),
            new BattleAssetHandler(),
            new MantraAssetHandler(),
            new BufferAssetHandler(),
            new AdjustmentAssetHandler(),
            new CinameticAssetHandler(),
            new NoteDescriptionAssetHandler(),
            new GameFormulaAssetHandler(),
            new HelpAssetHandler(),
            new HelpDescriptionAssetHandler(),
            new TalentAssetHandler(),
            new ShopAssetHandler(),
            new RewardAssetHandler(),
            new RefiningAssetHandler(),
            new MapAssetHandler(),
            new ForgeAssetHandler(),
            new FavorabilityAssetHandler(),
            new EvaluationAssetHandler(),
            new ElectiveAssetHandler(),
            new CharacterExteriorAssetHandler(),
            new CharacterBehaviourAssetHandler(),
            new BookAssetHandler(),
            new TraitAssetHandler(),
            new StringTableAssetHandler(),
            new QuestAssetHandler(),
            new PropsAssetHandler(),
            new NurturanceAssetHandler(),
            new EventCubeAssetHandler(),
            new NpcAssetHandler(),
            new CharacterInfoAssetHandler(),
            new BattleAreaAssetHandler(),
            new AchievementAssetHandler(),
            new AlchemyAssetHandler(),
            new RoundAssetHandler(),
            new RegistrationBonusAssetHandler(),
            new NurturanceIdleAssetHandler(),
            new TalkAssetHandler(),
            new EndingIdsAssetHandler(),
            new EndingTranslationsAssetHandler(),
        };

        //Selects the handlers to run. sheetFilter is a comma separated list of asset names, null or empty means all of them.
        private static List<AssetHandler> SelectHandlers(string sheetFilter)
        {
            if (string.IsNullOrWhiteSpace(sheetFilter))
            {
                return _Handlers;
            }

            string[] wanted = sheetFilter.Split(',');
            List<AssetHandler> selected = new List<AssetHandler>();
            foreach (string name in wanted)
            {
                string trimmedName = name.Trim();
                if (trimmedName.Length == 0)
                {
                    continue;
                }

                AssetHandler handler = _Handlers.Find(h => string.Equals(h.AssetName, trimmedName, StringComparison.OrdinalIgnoreCase));
                if (handler == null)
                {
                    throw new ArgumentException("Unknown sheet '" + trimmedName + "'. Known sheets: " + string.Join(", ", KnownSheetNames()));
                }

                selected.Add(handler);
            }

            return selected;
        }

        internal static List<string> KnownSheetNames()
        {
            List<string> names = new List<string>();
            foreach (AssetHandler handler in _Handlers)
            {
                names.Add(handler.AssetName);
            }
            names.Sort();
            return names;
        }

        //The sheets are only throttled against each other, a single sheet does not need to wait
        private static void Throttle(int handlerCount, int milliseconds)
        {
            if (handlerCount > 1)
            {
                Thread.Sleep(milliseconds);
            }
        }

        public static void UpdateSpreadsheetsFromGameFiles(string sheetFilter = null)
        {
            //Get input folder path
            string workingDirectory = Environment.CurrentDirectory;
            string inputFolder = workingDirectory + Path.DirectorySeparatorChar + "Input";
            List<AssetHandler> handlers = SelectHandlers(sheetFilter);
            foreach (AssetHandler handler in handlers)
            {
                handler.UpdateSheetFromGameFile(inputFolder);
                Throttle(handlers.Count, 5000);
            }
        }

        public static void ExportToMod(string BuildGameDataFromSheet, string sheetFilter = null)
        {
            List<AssetHandler> handlers = SelectHandlers(sheetFilter);
            foreach (AssetHandler handler in handlers)
            {
                handler.BuildGameDataFromSheet(BuildGameDataFromSheet);
                Throttle(handlers.Count, 5000);
            }
        }

        internal static void GetTranslationStats(ref List<TranslationStatEntry> stats, string sheetFilter = null)
        {
            List<AssetHandler> handlers = SelectHandlers(sheetFilter);
            foreach (AssetHandler handler in handlers)
            {
                //Retry instead of aborting the whole run: a single failed sheet would silently shrink every total
                const int attempts = 3;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        handler.GetTranslationStats(ref stats);
                        break;
                    }
                    catch (Exception e) when (attempt < attempts)
                    {
                        Console.WriteLine("Attempt " + attempt + " for " + handler.AssetName + " failed, retrying: " + e.Message);
                        Thread.Sleep(15000);
                    }
                }

                Throttle(handlers.Count, 10000);
            }
        }

        internal static void ExportTranslatedLinesToCSV(string outPath, ref List<Color> acceptableColors, string sheetFilter = null)
        {
            List<AssetHandler> handlers = SelectHandlers(sheetFilter);
            foreach (AssetHandler handler in handlers)
            {
                bool success = false;
                while (!success)
                {
                    try
                    {
                        handler.ExportTranslatedLinesToCSV(outPath, ref acceptableColors);
                        success = true;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        //Always back off after a failure, even for a single sheet, or this spins on the API
                        Thread.Sleep(60000);
                    }
                }

                Throttle(handlers.Count, 60000);
            }
        }
    }
}