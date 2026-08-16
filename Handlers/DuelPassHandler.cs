using System;
using Il2CppYgomSystem.UI;
using Il2CppYgomGame.Duelpass;
using UnityEngine;
using UnityEngine.UI;

namespace BlindDuel
{
    public class DuelPassHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "DuelPassMenu" or "DuelPass" or "DuelPassRewardList";

        public bool OnScreenEntered(string viewControllerName) => false;

        public string OnButtonFocused(SelectionButton button)
        {
            var widget = FindRewardWidget(button);
            if (widget == null)
            {
                // Widget not found - try reading from hierarchy directly
                // The button shows "×10" etc. - read icon sprite + count from parent
                try
                {
                    string count = null;
                    string itemType = null;

                    // Walk up looking for count text and item icon
                    var t = button.transform;
                    for (int i = 0; i < 5 && t != null; i++)
                    {
                        if (string.IsNullOrEmpty(count))
                        {
                            var tmps = t.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(true);
                            if (tmps != null)
                            {
                                foreach (var tmp in tmps)
                                {
                                    try
                                    {
                                        string text = tmp?.text?.Trim();
                                        if (!string.IsNullOrEmpty(text) && text.StartsWith("×"))
                                            count = text;
                                    }
                                    catch { }
                                }
                            }
                        }

                        // Find ConsumeIcon sprite → extract item ID → use ItemUtil
                        if (string.IsNullOrEmpty(itemType))
                        {
                            var images = t.GetComponentsInChildren<Image>(true);
                            if (images != null)
                            {
                                foreach (var img in images)
                                {
                                    try
                                    {
                                        if (img?.sprite == null) continue;
                                        string sn = img.sprite.name ?? "";
                                        if (sn.StartsWith("ConsumeIcon"))
                                        {
                                            // Parse ID from "ConsumeIcon0001" → 1
                                            string numStr = sn.Substring("ConsumeIcon".Length);
                                            if (int.TryParse(numStr, out int itemId) && itemId > 0)
                                            {
                                                try
                                                {
                                                    itemType = Il2CppYgomGame.Utility.ItemUtil.GetItemName(
                                                        false, 1, itemId);
                                                }
                                                catch { }
                                            }
                                            if (string.IsNullOrEmpty(itemType))
                                                itemType = sn; // fallback to sprite name
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(count) && !string.IsNullOrEmpty(itemType)) break;
                        t = t.parent;
                    }

                    string passType = FindPassType(button);
                    string grade = FindGrade(button);

                    string result = "";
                    if (!string.IsNullOrEmpty(grade)) result = $"Grade {grade}";
                    if (!string.IsNullOrEmpty(itemType)) result += (result.Length > 0 ? ", " : "") + itemType;
                    if (!string.IsNullOrEmpty(count)) result += (result.Length > 0 ? " " : "") + count;
                    if (!string.IsNullOrEmpty(passType)) result += $", {passType} Pass";

                    if (!string.IsNullOrEmpty(result)) return result;
                }
                catch (Exception ex) { Log.Write($"[DuelPass] fallback: {ex.Message}"); }

                return null;
            }

            try
            {
                // Read quantity from countText or rewardNumText
                string count = ReadText(widget.countText) ?? ReadText(widget.rewardNumText);

                // Read item type from icon sprites in the rewardThumbHolder
                string itemName = null;
                try
                {
                    var holder = widget.rewardThumbHolder;
                    if (holder != null)
                        itemName = ReadItemFromSprites(holder);
                }
                catch { }

                // Fallback: try text extraction
                if (string.IsNullOrEmpty(itemName))
                {
                    try
                    {
                        var holder = widget.rewardThumbHolder;
                        if (holder != null)
                            itemName = TextExtractor.ExtractFirst(holder);
                    }
                    catch { }
                }

                // Determine pass type (Normal/Gold) from hierarchy
                string passType = FindPassType(button);

                // Find grade from parent column header
                string grade = FindGrade(button);

                // Build result
                string result = "";
                if (!string.IsNullOrEmpty(grade))
                    result = $"Grade {grade}";
                if (!string.IsNullOrEmpty(itemName))
                    result += (result.Length > 0 ? ", " : "") + itemName;
                if (!string.IsNullOrEmpty(count))
                    result += (result.Length > 0 ? " " : "") + count;
                if (!string.IsNullOrEmpty(passType))
                    result += $", {passType} Pass";

                return !string.IsNullOrEmpty(result) ? result : null;
            }
            catch (Exception ex)
            {
                Log.Write($"[DuelPass] {ex.Message}");
                return null;
            }
        }

        private static string ReadText(Il2CppTMPro.TMP_Text tmp)
        {
            try
            {
                if (tmp == null) return null;
                string t = tmp.text?.Trim();
                return string.IsNullOrEmpty(t) ? null : t;
            }
            catch { return null; }
        }

        /// <summary>
        /// Read item type from sprites in the reward icon area.
        /// Icons use sprite names like "icon_cp_n", "icon_cp_r", "icon_cp_sr", "icon_cp_ur",
        /// "icon_gem", etc.
        /// </summary>
        private static string ReadItemFromSprites(GameObject holder)
        {
            var images = holder.GetComponentsInChildren<Image>(true);
            if (images == null) return null;

            foreach (var img in images)
            {
                try
                {
                    if (img == null || img.sprite == null) continue;
                    string spriteName = img.sprite.name?.ToLower() ?? "";

                    if (spriteName.Contains("cp_ur") || spriteName.Contains("craft_ur")) return "CP UR";
                    if (spriteName.Contains("cp_sr") || spriteName.Contains("craft_sr")) return "CP SR";
                    if (spriteName.Contains("cp_r") || spriteName.Contains("craft_r")) return "CP R";
                    if (spriteName.Contains("cp_n") || spriteName.Contains("craft_n")) return "CP N";
                    if (spriteName.Contains("gem")) return "Gems";
                }
                catch { }
            }
            return null;
        }

        private static string FindPassType(SelectionButton button)
        {
            try
            {
                var t = button.transform;
                for (int i = 0; i < 6 && t != null; i++)
                {
                    string n = t.name?.ToLower() ?? "";
                    if (n.Contains("normal")) return "Normal";
                    if (n.Contains("gold")) return "Gold";
                    t = t.parent;
                }
            }
            catch { }
            return "";
        }

        private static string FindGrade(SelectionButton button)
        {
            // Walk up to find a sibling/parent with grade number text
            try
            {
                var t = button.transform.parent;
                for (int i = 0; i < 4 && t != null; i++)
                {
                    // Look for a child named like "grade" or containing the grade number
                    var gradeText = t.Find("GradeText") ?? t.Find("gradeText") ?? t.Find("Grade");
                    if (gradeText != null)
                    {
                        string text = TextExtractor.ExtractFirst(gradeText.gameObject);
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                    t = t.parent;
                }
            }
            catch { }
            return null;
        }

        private static DuelpassRewardButtonWidget FindRewardWidget(SelectionButton button) =>
            button.GetComponentInParent<DuelpassRewardButtonWidget>();
    }
}
