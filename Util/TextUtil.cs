using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2CppYgomGame.Card;
using UnityEngine;

namespace BlindDuel
{
    public static class TextUtil
    {
        private static readonly HashSet<string> BannedTexts = new() { "00:00", "You can add new Cards to your Deck.", "×n" };

        // Matches <card mrk = '12345'/> tags (self-closing, card name placeholder)
        private static readonly Regex CardTagPattern = new(@"<card\s+mrk\s*=\s*'(\d+)'\s*/>", RegexOptions.Compiled);
        private static readonly Regex TagPattern = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceOnly = new(@"^\s*$", RegexOptions.Compiled);
        private static readonly Regex WhitespaceOrPunctuation = new(@"^\s*$|[.!]+$", RegexOptions.Compiled);
        // CJK Unified Ideographs, Hiragana, Katakana, CJK Symbols, Fullwidth Forms
        private static readonly Regex CJKPattern = new(@"[\u3000-\u303F\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\uFF00-\uFFEF]+", RegexOptions.Compiled);
        // Matches "1 word(s)" patterns — resolves to singular/plural based on the number
        private static readonly Regex PluralPattern = new(@"(\d+)\s+(\w+?)\(s\)", RegexOptions.Compiled);

        public static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Resolve <card mrk='XXXX'/> tags to actual card names before stripping
            text = ResolveCardTags(text);
            return TagPattern.Replace(text, "");
        }

        /// <summary>
        /// Replace <card mrk='XXXX'/> self-closing tags with the actual card name from Content.
        /// </summary>
        private static string ResolveCardTags(string text)
        {
            return CardTagPattern.Replace(text, new MatchEvaluator(ResolveCardTagMatch));
        }

        private static string ResolveCardTagMatch(Match match)
        {
            try
            {
                string mrkStr = match.Groups[1].Value;
                if (int.TryParse(mrkStr, out int mrk))
                {
                    var content = Content.s_instance;
                    if (content != null)
                    {
                        string name = content.GetName(mrk);
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"[TextUtil] ResolveCardTag error: {ex.Message}");
            }
            return "";
        }

        /// <summary>
        /// Strips CJK unicode characters (Chinese/Japanese/Korean) from text.
        /// Screen readers often can't handle these characters properly.
        /// </summary>
        public static string StripCJK(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return CJKPattern.Replace(text, "").Trim();
        }

        /// <summary>
        /// Converts "43 day(s)" → "43 days", "1 day(s)" → "1 day", etc.
        /// Works for any word: day(s), hour(s), ticket(s), pack(s), etc.
        /// </summary>
        public static string FixPlurals(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return PluralPattern.Replace(text, FixPluralMatch);
        }

        private static string FixPluralMatch(Match m)
        {
            string num = m.Groups[1].Value;
            string word = m.Groups[2].Value;
            return num == "1" ? num + " " + word : num + " " + word + "s";
        }

        /// <summary>
        /// Returns true if the text should be filtered out, considering the element name.
        /// </summary>
        public static bool IsBannedText(GameObject textElement, string text, bool hasMenuContext)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (BannedTexts.Contains(text)) return true;

            bool useStrictFilter = hasMenuContext || textElement.name.Equals("Button");
            Regex pattern = useStrictFilter ? WhitespaceOnly : WhitespaceOrPunctuation;
            return pattern.IsMatch(text);
        }
    }
}
