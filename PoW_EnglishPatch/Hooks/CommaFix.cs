using System.Reflection;
using System.Text;

using HarmonyLib;

using Heluo.Data;

using UnityEngine.UI;

namespace EnglishPatch.Hooks
{
    //The text files use the full-width comma '，' wherever the game would split on ',' (Book speech bubbles, Mantra effects,
    //Evaluation...). Those columns have to keep it, so it is turned into a normal comma when the text is shown instead.
    internal static class CommaFix
    {
        public const char FullWidthComma = '，';

        //'，' next to a Chinese character is left alone, full-width brackets and colons also appear in the English texts.
        public static string Fix(string s)
        {
            if (s == null || s.IndexOf(FullWidthComma) < 0) return s;

            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char prev = i > 0 ? s[i - 1] : ' ';
                char next = i + 1 < s.Length ? s[i + 1] : ' ';
                if (c != FullWidthComma || IsChinese(prev) || IsChinese(next))
                {
                    sb.Append(c);
                    continue;
                }
                sb.Append(next == ' ' || i + 1 == s.Length ? "," : ", ");
            }
            return sb.ToString();
        }

        private static bool IsChinese(char c)
        {
            return (c >= '㐀' && c <= '鿿') || (c >= '豈' && c <= '﫿');
        }
    }

    [HarmonyPatch(typeof(Text), "text", MethodType.Setter)]
    public class Text_setText_CommaFix_Hook
    {
        public static void Prefix(ref string value)
        {
            value = CommaFix.Fix(value);
        }
    }

    /*
     * "Suitable for:" on gear (blacksmith, shops, inventory) lists the characters joined with the Chinese list comma '、'
     * ("Suitable for:Zhao Zilong、Xiao Qianling"). They are joined with ", " and a space follows the colon.
     */
    [HarmonyPatch(typeof(Heluo.UI.CtrlFrom), "GetPropsConditionDescripition")]
    public class CtrlFrom_GetPropsConditionDescripition_Hook
    {
        private static readonly System.Text.RegularExpressions.Regex _Colon = new System.Text.RegularExpressions.Regex(@"^([^:：]*)[:：]\s*");

        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || __result.IndexOf('、') < 0 && __result.IndexOf(':') < 0 && __result.IndexOf('：') < 0)
            {
                return;
            }
            string list = __result.Replace("、", ", ").TrimEnd(' ', ',');
            __result = _Colon.Replace(list, "$1: ", 1);
        }
    }

    //A Mantra effect is "level, text" and the game splits it on every ',', so the text is cut at its first normal comma.
    //Keep the first comma as the separator and turn the rest into '，' (shown as ", " by the hook above).
    [HarmonyPatch]
    public class MantraEffectDescriptionConverter_StringToField_Hook
    {
        //Looked up by name: the converter derives from FileHelpers, which the project does not reference.
        //Asking the game assembly directly avoids scanning every type of every loaded assembly.
        public static MethodBase TargetMethod()
        {
            var converter = typeof(GameData).Assembly.GetType("Heluo.Data.Converter.MantraEffectDescriptionConverter");
            return AccessTools.Method(converter, "StringToField");
        }

        public static void Prefix(ref string from)
        {
            if (from == null) return;
            int first = from.IndexOf(',');
            if (first < 0 || from.IndexOf(',', first + 1) < 0) return;
            from = from.Substring(0, first + 1) + from.Substring(first + 1).Replace(',', CommaFix.FullWidthComma);
        }
    }
}
