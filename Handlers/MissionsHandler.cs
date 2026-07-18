using System;
using Il2CppInterop.Runtime;
using Il2CppYgomGame.Mission;
using Il2CppYgomGame.Utility;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class MissionsHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "Mission" or "MissionMenu";

        public bool OnScreenEntered(string viewControllerName)
        {
            string header = ScreenDetector.ReadGameHeaderText() ?? "Missions";

            try
            {
                var rootContext = GetRootContext();
                var tab = rootContext?.currentTab;
                string tabName = tab?._tabNameText_k__BackingField;
                if (!string.IsNullOrEmpty(tabName))
                    header += $", {tabName}";
            }
            catch { }

            Speech.AnnounceScreen(header);
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                // Tab buttons — "Button" inside "TabTemplate(Clone)" or "TabTemplateStarting(Clone)"
                if (button.name == "Button" && IsTabButton(button))
                    return HandleTab(button);

                // Goal buttons inside mission panels — "Locator" inside "GoalHolderN"
                if (button.name == "Locator" && IsGoalButton(button))
                    return HandleGoal(button);

                // Receive All button
                if (IsReceiveAllButton(button))
                    return "Receive All";

                return null;
            }
            catch (Exception ex)
            {
                Log.Write($"[Missions] ERROR: {ex.Message}");
                return null;
            }
        }

        // --- Identification helpers ---

        private static bool IsTabButton(SelectionButton button)
        {
            var parent = button.transform.parent;
            return parent != null && parent.name.StartsWith("TabTemplate");
        }

        private static bool IsGoalButton(SelectionButton button)
        {
            var parent = button.transform.parent;
            return parent != null && parent.name.StartsWith("GoalHolder");
        }

        private static bool IsReceiveAllButton(SelectionButton button)
        {
            Transform current = button.transform;
            for (int i = 0; i < 4 && current != null; i++)
            {
                if (current.name.Contains("BulkRecieve"))
                    return true;
                current = current.parent;
            }
            return false;
        }

        // --- Tabs ---

        private string HandleTab(SelectionButton button)
        {
            try
            {
                // Read tab name from the TMP text on the tab widget
                string tabName = ReadTabText(button.transform.parent);
                if (string.IsNullOrEmpty(tabName))
                    return null;

                // Get index: tab templates are siblings in the scroll content
                var tabTemplate = button.transform.parent;
                var container = tabTemplate?.parent;
                if (container == null) return tabName;

                int index = 0;
                int total = 0;
                for (int i = 0; i < container.childCount; i++)
                {
                    var child = container.GetChild(i);
                    if (!child.gameObject.activeInHierarchy) continue;
                    if (!child.name.StartsWith("TabTemplate")) continue;
                    total++;
                    if (child == tabTemplate)
                        index = total;
                }

                // Mission count from rootContext
                string missionInfo = "";
                try
                {
                    var rootContext = GetRootContext();
                    var tabContexts = rootContext?.tabs;
                    if (tabContexts != null)
                    {
                        // Match by tab name
                        for (int i = 0; i < tabContexts.Count; i++)
                        {
                            var tc = tabContexts[i];
                            if (tc?._tabNameText_k__BackingField == tabName)
                            {
                                var missions = tc.m_Missions;
                                if (missions != null && missions.Count > 0)
                                    missionInfo = $", {missions.Count} missions";
                                break;
                            }
                        }
                    }
                }
                catch { }

                string result = $"{tabName}{missionInfo}";
                if (total > 1)
                    result += $"\n{index} of {total}";
                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"[Missions] Tab ERROR: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Read tab name from ImageOn or ImageOff TMP text inside the tab template.
        /// </summary>
        private static string ReadTabText(Transform tabTemplate)
        {
            if (tabTemplate == null) return null;

            // Try ImageOff first (the inactive label — visible text for non-selected tabs)
            // Then ImageOn (for the selected tab)
            foreach (string childName in new[] { "ImageOff", "ImageOn" })
            {
                var image = tabTemplate.Find(childName);
                if (image == null) continue;

                var textTMP = image.Find("TextTMP");
                if (textTMP == null) continue;

                var tmp = textTMP.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                string text = tmp?.text?.Trim();
                if (!string.IsNullOrEmpty(text) && text != "OK")
                    return text;
            }
            return null;
        }

        // --- Goals (buttons within mission panels) ---

        private string HandleGoal(SelectionButton button)
        {
            var rootContext = GetRootContext();
            var currentTab = rootContext?.currentTab;
            if (currentTab == null) return null;

            // Find which mission panel this goal belongs to via m_PanelWidgetsMap
            var content = GetResidentContent();
            var panelMap = content?.m_PanelWidgetsMap;
            if (panelMap == null || panelMap.Count == 0) return null;

            MissionPanelWidget panelWidget = null;
            GameObject panelRoot = null;

            // Use IsChildOf to find which panel contains this button
            var enumerator = panelMap.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var entry = enumerator.Current;
                if (entry.Key != null && button.transform.IsChildOf(entry.Key.transform))
                {
                    panelWidget = entry.Value;
                    panelRoot = entry.Key;
                    break;
                }
            }

            if (panelWidget == null) return null;

            // Look up MissionContext by missionId
            int missionId = panelWidget.missionId;
            var missionMap = currentTab.m_MissionMap;
            MissionContext mission = null;
            missionMap?.TryGetValue(missionId, out mission);
            if (mission == null) return null;

            // Mission name from cache
            string name = mission.m_MissionNameCache;
            if (string.IsNullOrEmpty(name)) return null;

            // Append progress from mission data: "Duel in Solo Mode (3/3)"
            try
            {
                var goals = mission._goals_k__BackingField;
                if (goals != null && goals.Length > 0)
                {
                    int requirement = goals[0].requirement;
                    if (requirement > 0)
                        name += $" ({mission.progress}/{requirement})";
                }
            }
            catch { }

            string result = name;

            // Goal status + reward
            try
            {
                string holderName = button.transform.parent?.name;
                var goals = mission._goals_k__BackingField;
                if (goals != null && holderName != null && holderName.StartsWith("GoalHolder"))
                {
                    if (int.TryParse(holderName.Substring("GoalHolder".Length), out int slotIdx)
                        && slotIdx < goals.Length)
                    {
                        var goal = goals[slotIdx];
                        if (goal != null)
                        {
                            var goalType = goal.GetGoalType(mission);
                            string statusText = goalType switch
                            {
                                MissionGoalWidget.GoalType.Complete => "Received",
                                MissionGoalWidget.GoalType.Recievable => "Reward available",
                                MissionGoalWidget.GoalType.InProgress => "In progress",
                                _ => ""
                            };
                            if (!string.IsNullOrEmpty(statusText))
                                result += $"\n{statusText}";

                            // Reward: amount + item name from game's ItemUtil
                            if (goal.itemCount > 0)
                            {
                                string itemName = GetRewardName(goal);
                                result += $"\nReward: {goal.itemCount} {itemName}".TrimEnd();
                            }
                        }
                    }
                }
            }
            catch { }

            // Time remaining — recursive find from panel root
            try
            {
                if (panelRoot != null)
                {
                    string timeText = FindTMPText(panelRoot.transform, "LimitDateTextTMP");
                    if (!string.IsNullOrEmpty(timeText))
                        result += $"\n{timeText}";
                }
            }
            catch { }

            // Mission index among missions in current tab
            try
            {
                var missions = currentTab.m_Missions;
                if (missions != null)
                {
                    int total = missions.Count;
                    for (int i = 0; i < total; i++)
                    {
                        if (missions[i]?.missionId == missionId)
                        {
                            result += $"\n{i + 1} of {total}";
                            break;
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Get the reward item name using the game's native ItemUtil.
        /// Uses GetItemCategoryName first (e.g., "Gems"), falls back to GetItemName.
        /// </summary>
        private static string GetRewardName(GoalContext goal)
        {
            try
            {
                // Try specific item name first (e.g., "GEM", "Legacy Pack")
                string itemName = ItemUtil.GetItemName(
                    goal.isPeriod, goal.itemCategory, goal.itemId);
                if (!string.IsNullOrEmpty(itemName))
                    return itemName;

                // Fall back to category name (e.g., "Consumables", "Cards")
                string categoryName = ItemUtil.GetItemCategoryName(
                    goal.isPeriod, goal.itemCategory, goal.itemId);
                if (!string.IsNullOrEmpty(categoryName))
                    return categoryName;
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Recursively find a TextMeshProUGUI by gameObject name within a transform hierarchy.
        /// </summary>
        private static string FindTMPText(Transform root, string targetName)
        {
            var tmps = root.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>();
            foreach (var tmp in tmps)
            {
                if (tmp.gameObject.name == targetName)
                {
                    string text = tmp.text?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
            }
            return null;
        }

        // --- VC / Context access ---

        private static ResidentMissionContent GetResidentContent()
        {
            try
            {
                var vc = ScreenDetector.GetFocusVC<MissionViewController>();
                if (vc == null) return null;

                var contentMap = vc.m_ContentMap;
                if (contentMap == null) return null;

                IMissionContent missionContent;
                if (contentMap.TryGetValue(MissionViewController.MissionContentType.Mission, out missionContent))
                    return missionContent?.TryCast<ResidentMissionContent>();
            }
            catch { }
            return null;
        }

        private static MissionRootContext GetRootContext()
        {
            return GetResidentContent()?.m_RootContext;
        }
    }
}
