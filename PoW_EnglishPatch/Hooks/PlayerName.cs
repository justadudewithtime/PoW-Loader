using HarmonyLib;

using Heluo;
using Heluo.Data;
using Heluo.UI;
using Heluo.Utility;

namespace EnglishPatch.Hooks
{
    /*
     * The game shows names as SurName + Name without a space (fine for Chinese).
     * The translated NPC surnames already end with a space ("Shi " + "Kun"), but the player types their own name,
     * so it ended up as "ZhaoZilong". We give the player surname the same trailing space.
     * Dialogue uses the surname on its own, so it gets the trimmed version (see StringTool_ParseName_Hook).
     */

    public static class PlayerName
    {
        public static void AddSpaceToSurName(GameData gameData)
        {
            if (gameData == null || gameData.Exterior == null || !gameData.Exterior.ContainsKey(GameConfig.Player))
            {
                return;
            }

            CharacterExteriorData player = gameData.Exterior[GameConfig.Player];
            string surName = player.SurName;
            if (string.IsNullOrEmpty(surName))
            {
                return;
            }

            //Already spaced, or a Chinese name that should stay without a space
            char last = surName[surName.Length - 1];
            if (char.IsWhiteSpace(last) || last >= '⺀')
            {
                return;
            }

            player.SurName = surName + " ";
        }

        /*
         * Character names are copied into the save the first time a character is used. Saves made with an older translation
         * keep the surname without its trailing space ("Ning" + "Chuchu" = "NingChuchu"), even though CharacterExterior.txt has "Ning ".
         * We only add the missing space back when the saved surname is otherwise the same, names changed by the story stay as they are.
         *
         * Some cutscene renames put the space in front of the name instead ("Cheng" + " Yong"). Adding the surname space to those
         * gave "Cheng  Yong", so the space stays on the surname and the name loses its leading one (also fixes saves that have both).
         */
        public static void RestoreNpcSurNameSpaces(GameData gameData)
        {
            if (gameData == null || gameData.Exterior == null)
            {
                return;
            }

            foreach (var entry in gameData.Exterior)
            {
                if (entry.Key == GameConfig.Player || entry.Value == null || string.IsNullOrEmpty(entry.Value.SurName))
                {
                    continue;
                }

                RestoreSurNameSpace(entry.Key, entry.Value);

                string surName = entry.Value.SurName;
                string name = entry.Value.Name;
                if (char.IsWhiteSpace(surName[surName.Length - 1]) && !string.IsNullOrEmpty(name) && char.IsWhiteSpace(name[0]))
                {
                    entry.Value.Name = name.TrimStart();
                }
            }
        }

        private static void RestoreSurNameSpace(string id, CharacterExteriorData saved)
        {
            CharacterExterior current = Game.Data.Get<CharacterExterior>(id);
            string currentSurName = current?.SurName;
            if (string.IsNullOrEmpty(currentSurName) || !char.IsWhiteSpace(currentSurName[currentSurName.Length - 1]))
            {
                return;
            }

            if (saved.SurName == currentSurName.TrimEnd())
            {
                saved.SurName = currentSurName;
            }
        }
    }

    //Loading a save (also fixes saves made before this patch)
    [HarmonyPatch(typeof(GameData), "Initialize")]
    public class GameData_Initialize_Hook
    {
        public static void Postfix(GameData __instance)
        {
            PlayerName.AddSpaceToSurName(__instance);
            PlayerName.RestoreNpcSurNameSpaces(__instance);
        }
    }

    //Dialogue inserts the surname alone with {name_1} and the translations expect it without the space ("{name_1}, ...", "{name_1} {name_2}"),
    //so we fill in {name_1} with the trimmed surname before the game does.
    [HarmonyPatch(typeof(StringTool), "ParseName")]
    public class StringTool_ParseName_Hook
    {
        public static void Prefix(ref string text)
        {
            if (text == null || !text.Contains("{name_1}"))
            {
                return;
            }

            string surName = Game.GameData.Exterior[GameConfig.Player].SurName;
            text = text.Replace("{name_1}", surName?.TrimEnd());
        }
    }

    //Finishing the new game registration. Not done while typing, since the name is set on every keystroke.
    [HarmonyPatch(typeof(CtrlRegistration), "InitialRewards")]
    public class CtrlRegistration_InitialRewards_Hook
    {
        public static void Postfix()
        {
            PlayerName.AddSpaceToSurName(Game.GameData);
        }
    }
}
