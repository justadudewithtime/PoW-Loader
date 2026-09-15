using Google.Apis.Sheets.v4.Data;

using System.Collections.Generic;

namespace PoW_Tool_SheetUtilities.Handler
{
    public class TranslationStatEntry
    {
        public string Name = "Unnamed Type";
        public int LineCount = 0;
        public int WordCount = 0;
        public bool MatchAll = false;

        public List<Color> AcceptableColors = new List<Color>();

        //How many lines were counted per actual cell color, so unexpected colors can be reported instead of vanishing
        public Dictionary<string, int> CountedColors = new Dictionary<string, int>();

        public TranslationStatEntry(string name)
        {
            Name = name;
        }

        public bool Matches(Color color)
        {
            for (int i = 0; i < AcceptableColors.Count; i++)
            {
                if (ColorHelper.IsSameColor(color, AcceptableColors[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public void NoteColor(Color color)
        {
            string hex = ColorHelper.ToHex(color);
            int count;
            CountedColors.TryGetValue(hex, out count);
            CountedColors[hex] = count + 1;
        }
    }
}
