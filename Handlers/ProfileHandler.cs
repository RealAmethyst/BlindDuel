using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppYgomGame.Menu;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class ProfileHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "Profile" or "PlayerProfile" or "ProfileData";

        public bool OnScreenEntered(string viewControllerName)
        {
            if (viewControllerName == "ProfileData")
                return HandleDataScreen();

            return HandleProfileScreen();
        }

        private bool HandleProfileScreen()
        {
            string header = ScreenDetector.ReadGameHeaderText();
            string announcement = header ?? "Profile";

            try
            {
                var profileVC = ScreenDetector.GetFocusVC<ProfileViewController>();
                if (profileVC == null)
                {
                    Speech.AnnounceScreen(announcement);
                    return true;
                }

                var card = profileVC.profileCard;
                string name = null;
                string level = null;
                string followers = null;
                string following = null;

                if (card?.eom != null)
                {
                    // Read text using ProfileCard's static EOM label fields
                    level = TryGetElement(card.eom, ProfileCard.TXT_LEVEL_LABEL);
                    followers = TryGetElement(card.eom, ProfileCard.TXT_FOLLOWER_LABEL);
                    following = TryGetElement(card.eom, ProfileCard.TXT_FOLLOW_LABEL);

                    // Player name via PlatformPlayerNameGroup component
                    name = TryGetPlayerName(card.eom);
                }

                // Build announcement
                if (!string.IsNullOrEmpty(name))
                    announcement += $", {name}";

                if (!string.IsNullOrEmpty(level))
                {
                    // Level text may already include "LV" prefix from game UI
                    if (level.StartsWith("LV", StringComparison.OrdinalIgnoreCase) ||
                        level.StartsWith("Level", StringComparison.OrdinalIgnoreCase))
                        announcement += $", {level}";
                    else
                        announcement += $", Level {level}";
                }

                // Player ID — use formatted pcode if available, fall back to EOM text
                long pcode = profileVC.pcode;
                if (pcode > 0)
                    announcement += $", ID: {FormatPcode(pcode)}";
                else if (card?.eom != null)
                {
                    string idText = TryGetElement(card.eom, ProfileCard.TXT_ID_LABEL);
                    if (!string.IsNullOrEmpty(idText))
                        announcement += $", {idText}";
                }

                if (!string.IsNullOrEmpty(followers))
                    announcement += $", {followers}";
                if (!string.IsNullOrEmpty(following))
                    announcement += $", {following}";

                // Read profile tags (skip empty "-" slots)
                if (card?.eom != null)
                {
                    var tags = new List<string>();
                    for (int i = 1; i <= 4; i++)
                    {
                        string tag = TryGetElement(card.eom, $"TextTag{i}");
                        if (!string.IsNullOrEmpty(tag) && tag != "-")
                            tags.Add(tag);
                    }
                    if (tags.Count > 0)
                        announcement += $", {string.Join(", ", tags)}";
                }
            }
            catch (Exception ex)
            {
                Log.Write($"[ProfileHandler] Screen error: {ex.Message}");
            }

            Speech.AnnounceScreen(announcement);
            return true;
        }

        private bool HandleDataScreen()
        {
            // Announce header only — stats are read by ProfilePatches
            // after the scroll view data loads asynchronously
            string header = ScreenDetector.ReadGameHeaderText();
            Speech.AnnounceScreen(header ?? "Data");

            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                // Data screen has no meaningful button navigation — let default handle it
                if (ScreenDetector.GetFocusVC<ProfileDataViewController>() != null)
                    return null;

                string text = TextExtractor.ExtractFirst(button.gameObject);
                if (string.IsNullOrEmpty(text)) return null;

                // Walk up to find a parent that contains multiple SelectionButton children
                // (buttons may be wrapped in individual containers)
                var (idx, total) = TransformSearch.GetButtonIndexAmongAncestors(button);
                if (total > 1 && idx > 0)
                    return $"{text}, {idx} of {total}";

                return text;
            }
            catch (Exception ex)
            {
                Log.Write($"[ProfileHandler] Button error: {ex.Message}");
                return null;
            }
        }

        private static string TryGetElement(Il2CppYgomSystem.ElementSystem.ElementObjectManager eom, string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            try { return ElementReader.GetElementText(eom, label); }
            catch { return null; }
        }

        private static string TryGetPlayerName(Il2CppYgomSystem.ElementSystem.ElementObjectManager eom)
        {
            try
            {
                var go = eom?.gameObject;
                if (go == null) return null;

                var nameGroup = go.GetComponentInChildren<PlatformPlayerNameGroup>(true);
                if (nameGroup != null)
                    return nameGroup.GetYmdPlayerName();
            }
            catch (Exception ex)
            {
                Log.Write($"[Profile] Name lookup error: {ex.Message}");
            }
            return null;
        }

        private static string FormatPcode(long pcode)
        {
            try
            {
                return Il2CppYgomSystem.Extension.NumberExtension.ToPcodeFormatString(pcode);
            }
            catch
            {
                // Manual fallback for XXX-XXX-XXX format
                string s = pcode.ToString();
                if (s.Length == 9)
                    return $"{s[..3]}-{s[3..6]}-{s[6..]}";
                return s;
            }
        }
    }
}
