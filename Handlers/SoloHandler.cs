using System;
using Il2CppYgomGame.Solo;
using Il2CppYgomSystem.ElementSystem;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class SoloHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "SoloMode" or "SoloGate" or "SoloSelectChapter";

        public bool OnScreenEntered(string viewControllerName)
        {
            try
            {
                if (viewControllerName == "SoloSelectChapter")
                {
                    // Chapter map: announce gate name
                    var chapterVC = ScreenDetector.GetFocusVC<SoloSelectChapterViewController>();
                    if (chapterVC != null)
                    {
                        int gateId = chapterVC.m_GateId;
                        if (SoloState.Gates.TryGetValue(gateId, out var gate) && !string.IsNullOrEmpty(gate.Name))
                        {
                            Speech.AnnounceScreen(gate.Name);
                            return true;
                        }
                    }
                }

                // SoloMode / SoloGate: use header text, fall back to title scan
                string header = ScreenDetector.ReadGameHeaderText();
                if (!string.IsNullOrEmpty(header))
                {
                    Speech.AnnounceScreen(header);
                    return true;
                }

                // SoloMode has no header — use the VC title text
                var vc = ScreenDetector.GetFocusVC();
                if (vc != null)
                {
                    string title = ScreenDetector.FindScreenTitle(vc);
                    if (!string.IsNullOrEmpty(title))
                    {
                        Speech.AnnounceScreen(title);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write($"[SoloHandler] OnScreenEntered: {ex.Message}");
            }
            return false;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            // SoloGate: gate list buttons
            var gateVC = ScreenDetector.GetFocusVC<SoloGateViewController>();
            if (gateVC != null)
                return ReadGateButton(button, gateVC);

            // SoloSelectChapter: chapter map nodes
            var chapterVC = ScreenDetector.GetFocusVC<SoloSelectChapterViewController>();
            if (chapterVC != null)
                return ReadChapterButton(button, chapterVC);

            // SoloMode portal: gate previews and category buttons
            var soloVC = ScreenDetector.GetFocusVC<SoloModeViewController>();
            if (soloVC != null)
                return ReadPortalButton(button);

            return null;
        }

        /// <summary>
        /// Read gate data from the VC's gate manager, using EOM or TextExtractor for the gate name.
        /// </summary>
        private static string ReadGateButton(SelectionButton button, SoloGateViewController gateVC)
        {
            try
            {
                var gateManager = gateVC.m_GateManager;
                if (gateManager?.masterDataDic == null) return null;

                // Get gate name — try EOM first, fall back to TextExtractor
                string gateName = ReadGateNameFromEom(button);
                if (string.IsNullOrEmpty(gateName))
                    gateName = ReadGateNameFromText(button);
                if (string.IsNullOrEmpty(gateName)) return null;

                // Look up gate data by name
                SoloGateUtil.Data gateData = FindGateDataByName(gateManager, gateName);

                string result = gateName;

                // Status from game data
                if (gateData != null)
                {
                    if (gateData.IsComplete)
                        result += ", Complete";
                    else if (gateData.IsClear)
                        result += ", Clear";
                    else if (!gateData.isUnlocked)
                        result += ", Locked";
                }

                // Overview from game data
                string overview = gateData?.strOverview;
                if (string.IsNullOrEmpty(overview))
                {
                    var cached = SoloState.FindGateByName(gateName);
                    if (cached.HasValue)
                        overview = cached.Value.Overview;
                }
                if (!string.IsNullOrEmpty(overview))
                    result += $"\n{TextUtil.StripTags(overview).Trim()}";

                // Index from mainDataList or subDataList
                var (index, total) = FindGateIndex(gateData?.gateID ?? -1, gateVC);
                if (total > 0)
                    result += $"\n{index} of {total}";

                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"[SoloHandler] Gate button: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Read portal gate buttons (Recommended, Last Played) and category buttons (Stories, etc.).
        /// Gate buttons get overview from SoloState; category buttons return null for generic fallback.
        /// </summary>
        private static string ReadPortalButton(SelectionButton button)
        {
            try
            {
                // Get gate name from EOM or text
                string gateName = ReadGateNameFromEom(button);
                if (string.IsNullOrEmpty(gateName))
                    gateName = ReadGateNameFromText(button);
                if (string.IsNullOrEmpty(gateName)) return null;

                // Look up gate data — if not a gate, return null for generic fallback
                var gateInfo = SoloState.FindGateByName(gateName);
                if (!gateInfo.HasValue) return null;

                var info = gateInfo.Value;
                string result = gateName;

                if (info.IsComplete)
                    result += ", Complete";
                else if (info.IsClear)
                    result += ", Clear";

                if (!string.IsNullOrEmpty(info.Overview))
                    result += $"\n{TextUtil.StripTags(info.Overview).Trim()}";

                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"[SoloHandler] Portal button: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Try reading gate name from the button's parent EOM using the game's element label.
        /// </summary>
        private static string ReadGateNameFromEom(SelectionButton button)
        {
            try
            {
                var eom = button.GetComponentInParent<ElementObjectManager>();
                if (eom == null)
                {
                    // Also check the button's own GO (button might be the widget root)
                    eom = button.GetComponent<ElementObjectManager>();
                }
                if (eom == null) return null;

                string label = SoloGateUtil.TXT_GATENAME_LABEL;
                if (string.IsNullOrEmpty(label)) return null;

                return ElementReader.GetElementText(eom, label);
            }
            catch (Exception ex)
            {
                Log.Write($"[SoloHandler] EOM read: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fallback: read gate name from button's text hierarchy.
        /// Uses the last extracted text (gate name is typically the last label in the widget).
        /// </summary>
        private static string ReadGateNameFromText(SelectionButton button)
        {
            try
            {
                var texts = TextExtractor.ExtractAll(button.gameObject, new TextSearchOptions { FilterBanned = false });
                if (texts.Count == 0) return null;

                // Gate name is typically the last text in the widget
                return texts[^1].Text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Find gate Data by name in the gate manager's master dictionary.
        /// </summary>
        private static SoloGateUtil.Data FindGateDataByName(SoloGateUtil.GateManager gateManager, string name)
        {
            if (gateManager?.masterDataDic == null || string.IsNullOrEmpty(name)) return null;

            foreach (var entry in gateManager.masterDataDic)
            {
                if (entry.Value.StrName == name)
                    return entry.Value;
            }
            return null;
        }

        /// <summary>
        /// Find gate index in the VC's main or sub data list.
        /// </summary>
        private static (int index, int total) FindGateIndex(int gateId, SoloGateViewController gateVC)
        {
            if (gateId < 0) return (0, 0);

            // Check sub-gates first (if a gate is expanded)
            var subList = gateVC.subDataList;
            if (subList != null)
            {
                for (int i = 0; i < subList.Count; i++)
                {
                    if (subList[i] == gateId)
                        return (i + 1, subList.Count);
                }
            }

            // Check main gate list
            var mainList = gateVC.mainDataList;
            if (mainList != null)
            {
                for (int i = 0; i < mainList.Count; i++)
                {
                    if (mainList[i] == gateId)
                        return (i + 1, mainList.Count);
                }
            }

            return (0, 0);
        }

        /// <summary>
        /// Read chapter data from the VC's chapter map by matching button to chapter.go.
        /// </summary>
        private static string ReadChapterButton(SelectionButton button, SoloSelectChapterViewController chapterVC)
        {
            try
            {
                var chapterMap = chapterVC.chapterMap;
                if (chapterMap?.chapterDataDic == null) return null;

                // Find chapter whose go contains this button
                SoloSelectChapterViewController.Chapter matchedChapter = null;
                int chapterIndex = 0;
                int chapterCount = chapterMap.chapterDataDic.Count;
                int idx = 0;

                foreach (var entry in chapterMap.chapterDataDic)
                {
                    idx++;
                    var chapter = entry.Value;
                    if (chapter?.go == null) continue;

                    if (button.transform.IsChildOf(chapter.go.transform))
                    {
                        matchedChapter = chapter;
                        chapterIndex = idx;
                        break;
                    }
                }

                if (matchedChapter == null) return null;

                // Chapter name
                string result = matchedChapter.strChapter;
                if (string.IsNullOrEmpty(result))
                    result = "Chapter";

                // Chapter type from DialogType
                string typeName = matchedChapter.dType switch
                {
                    SoloModeUtil.DialogType.DUEL => "Duel",
                    SoloModeUtil.DialogType.SCENARIO => "Scenario",
                    SoloModeUtil.DialogType.LOCK => "Locked",
                    SoloModeUtil.DialogType.REWARD => "Reward",
                    SoloModeUtil.DialogType.TUTORIAL => "Tutorial",
                    _ => null
                };
                if (!string.IsNullOrEmpty(typeName))
                    result += $", {typeName}";

                // Duel level
                var duelChapter = matchedChapter.TryCast<SoloSelectChapterViewController.ChapterDuel>();
                if (duelChapter != null && duelChapter.level > 0)
                    result += $", Level {duelChapter.level}";

                // Status
                string statusText = matchedChapter.status switch
                {
                    SoloModeUtil.ChapterStatus.UNOPEN => "Locked",
                    SoloModeUtil.ChapterStatus.OPEN => "Available",
                    SoloModeUtil.ChapterStatus.RENTAL_CLEAR => "Loaner Clear",
                    SoloModeUtil.ChapterStatus.MYDECK_CLEAR => "My Deck Clear",
                    SoloModeUtil.ChapterStatus.COMPLETE => "Complete",
                    _ => null
                };
                if (!string.IsNullOrEmpty(statusText))
                    result += $", {statusText}";

                // Description
                string desc = matchedChapter.strExplanation;
                if (!string.IsNullOrEmpty(desc))
                    result += $"\n{TextUtil.StripTags(desc).Trim()}";

                // Index
                if (chapterCount > 0)
                    result += $"\n{chapterIndex} of {chapterCount}";

                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"[SoloHandler] Chapter button: {ex.Message}");
                return null;
            }
        }
    }
}
