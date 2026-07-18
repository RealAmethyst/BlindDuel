using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppTMPro;
using Il2CppYgomSystem.ElementSystem;

namespace BlindDuel
{
    /// <summary>
    /// Reads text from ElementObjectManager's serializedElements array.
    /// This is the game's own UI element system — more reliable than raw hierarchy scanning.
    /// </summary>
    public static class ElementReader
    {
        private static readonly HashSet<string> ContainerLabels = new(StringComparer.OrdinalIgnoreCase)
            { "Root", "RootContent", "RootBottom" };

        /// <summary>
        /// Read all labeled text elements from an EOM's serializedElements.
        /// Returns (label, text) pairs. Skips container labels to avoid parent/child overlaps.
        /// </summary>
        public static List<(string label, string text)> ReadElements(ElementObjectManager eom, string logPrefix = null)
        {
            var results = new List<(string label, string text)>();
            if (eom == null) return results;

            try
            {
                var serialized = eom.serializedElements;
                if (serialized == null || serialized.Length == 0) return results;

                foreach (var elem in serialized)
                {
                    if (elem == null) continue;
                    string label = elem.label;
                    if (string.IsNullOrEmpty(label) || ContainerLabels.Contains(label)) continue;

                    GameObject go = elem.gameObject;
                    if (go == null) continue;

                    // Mirror TextExtractor's active-filtering: exclude inactive TMP_Text so
                    // hidden/disabled sibling variants (e.g. off-state images) don't leak into
                    // dialog title/body extraction. GetComponentsInChildren(false) already skips
                    // inactive, and the activeInHierarchy re-check mirrors TextExtractor exactly.
                    var tmpTexts = go.GetComponentsInChildren<TMP_Text>(false);
                    if (tmpTexts == null) continue;

                    foreach (var tmp in tmpTexts)
                    {
                        if (tmp == null) continue;
                        if (!tmp.gameObject.activeInHierarchy) continue;
                        string rawText = tmp.text;
                        if (string.IsNullOrWhiteSpace(rawText)) continue;

                        string cleanText = TextUtil.StripTags(rawText).Trim();
                        if (string.IsNullOrWhiteSpace(cleanText)) continue;

                        results.Add((label, cleanText));

                        if (logPrefix != null)
                            Log.Write($"{logPrefix} [{label}] {tmp.transform.parent?.name}/{tmp.name} = {cleanText}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"[ElementReader] Error: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Get text from a specific named element in the EOM.
        /// </summary>
        public static string GetElementText(ElementObjectManager eom, string elementLabel)
        {
            if (eom == null || string.IsNullOrEmpty(elementLabel)) return null;

            try
            {
                var go = eom.GetElement(elementLabel);
                if (go == null) return null;

                var tmp = go.GetComponentInChildren<TMP_Text>(true);
                if (tmp == null) return null;

                string text = tmp.text;
                return string.IsNullOrWhiteSpace(text) ? null : TextUtil.StripTags(text).Trim();
            }
            catch (Exception ex)
            {
                Log.Write($"[ElementReader] GetElementText({elementLabel}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extract title and body text from a list of labeled elements using keyword matching.
        /// Useful for dialogs and screen announcements.
        /// </summary>
        public static (string title, string body) ExtractTitleAndBody(List<(string label, string text)> elements)
        {
            string title = null;
            string body = null;

            // First pass: match by keyword
            foreach (var (label, text) in elements)
            {
                string lower = label.ToLower();
                if (lower.StartsWith("button") || lower.Contains("cancel")) continue;

                if (title == null && (lower.Contains("title") || lower.Contains("header") || lower.Contains("start")))
                    title = text;
                else if (body == null && (lower.Contains("message") || lower.Contains("desc") || lower.Contains("info") || lower.Contains("body")))
                    body = text;
            }

            // Second pass: if title found but no body, grab first long text after title
            if (title != null && body == null)
            {
                bool pastTitle = false;
                foreach (var (label, text) in elements)
                {
                    if (text == title) { pastTitle = true; continue; }
                    if (!pastTitle || text.Length < 30) continue;
                    string lower = label.ToLower();
                    if (lower.StartsWith("button") || lower.Contains("cancel")) continue;
                    body = text;
                    break;
                }
            }

            // Fallback: first two non-button texts
            if (title == null && body == null)
            {
                foreach (var (label, text) in elements)
                {
                    string lower = label.ToLower();
                    if (lower.StartsWith("button") || lower.Contains("cancel")) continue;
                    if (IsPlaceholder(text)) continue;

                    if (title == null) title = text;
                    else { body = text; break; }
                }
            }

            return (title, body);
        }

        // Heuristic for "this text is the label of an action button, not body content":
        // short single token, no lowercase letters, at least one uppercase letter.
        // Catches OK / CANCEL / YES / NO / AGREE / etc. while leaving numbers, dates,
        // and any mixed-case text intact. Callers should only apply this where the
        // focused button will speak its own label separately (i.e. dialogs).
        public static bool LooksLikeButtonText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 10) return false;
            bool sawUpper = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) return false;
                if (char.IsLetter(c))
                {
                    if (!char.IsUpper(c)) return false;
                    sawUpper = true;
                }
            }
            return sawUpper;
        }

        /// <summary>
        /// Detect placeholder text (unreplaced key names — long strings with no spaces).
        /// </summary>
        public static bool IsPlaceholder(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            return text.Length > 30 && !text.Contains(' ') && !text.Contains('\n');
        }

        /// <summary>
        /// Format title and body into a dialog announcement string.
        /// </summary>
        public static string FormatAnnouncement(string title, string body)
        {
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(body))
                return $"Dialog. {title}. {body}";
            return $"Dialog. {(string.IsNullOrEmpty(title) ? body : title)}";
        }
    }
}
