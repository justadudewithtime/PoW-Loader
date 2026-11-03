using HarmonyLib;

using Heluo;
using Heluo.UI;

using ModAPI;

using UnityEngine;

namespace PoW_ModAPI.ResourceRedirection
{
    /*
    ImageLanguage (and WgBattleGameObject, used for the battle popups like Parry) only loads its language sprite if the Image has no sprite yet.
    If the prefab already ships with the Chinese sprite set, the load never happens and our redirect is skipped.
    So after the original ran, we load the sprite ourselves whenever we have a redirect for it.
    */

    public static class ImageLanguageRedirector
    {
        public static void ApplyRedirect(ImageLanguage imageLanguage)
        {
            if (imageLanguage.Image == null)
            {
                return;
            }

            string path = $"{imageLanguage.path}/{imageLanguage.spritename}_{GameConfig.Language}.png";
            var resourceRedirector = ResourceRedirectManager.GetInstance();
            if (!resourceRedirector.ExistsRedirect(path))
            {
                return;
            }

            Sprite sprite = resourceRedirector.GetRedirectedSprite(path);
            if (sprite != null)
            {
                imageLanguage.Image.sprite = sprite;
            }
        }
    }

    //WGBattleBillBorard calls LoadSprite on the popup templates when a battle starts
    [HarmonyPatch(typeof(ImageLanguage), "LoadSprite")]
    public class ImageLanguage_LoadSprite_Hook
    {
        public static void Postfix(ImageLanguage __instance)
        {
            ImageLanguageRedirector.ApplyRedirect(__instance);
        }
    }

    //Every popup copy shown in battle
    [HarmonyPatch(typeof(ImageLanguage), "Awake")]
    public class ImageLanguage_Awake_Hook
    {
        public static void Postfix(ImageLanguage __instance)
        {
            ImageLanguageRedirector.ApplyRedirect(__instance);
        }
    }
}
