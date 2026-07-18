using System;
using System.Collections.Generic;
using Il2CppYgomGame.Room;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class RoomEntryHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) => viewControllerName == "RoomEntry";

        public bool OnScreenEntered(string viewControllerName)
        {
            string header = ScreenDetector.ReadGameHeaderText();
            string announcement = !string.IsNullOrWhiteSpace(header) ? header : "Room List";

            try
            {
                int count = ScreenDetector.GetFocusVC<RoomEntryViewController>()?.dataList?.Count ?? -1;
                if (count == 0)
                    announcement += ", no rooms available";
                else if (count > 0)
                    announcement += $", {count} rooms";
            }
            catch (Exception ex) { Log.Write($"[RoomEntryHandler] Screen error: {ex.Message}"); }

            Speech.AnnounceScreen(announcement);
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                if (button.name == "ButtonReload") return "Reload room list";
                if (button.name == "ButtonFilter") return "Filter rooms";

                var vc = ScreenDetector.GetFocusVC<RoomEntryViewController>();
                if (vc == null) return null;

                if (TryGetDataIndex(vc, button, out int idx))
                    return FormatRoom(vc, idx);
            }
            catch (Exception ex) { Log.Write($"[RoomEntryHandler] Button error: {ex.Message}"); }
            return null;
        }

        private static bool TryGetDataIndex(RoomEntryViewController vc, SelectionButton button, out int idx)
        {
            idx = -1;
            var isv = vc.isv;
            if (isv == null) return false;

            return TransformSearch.TryResolveByAncestor<int>(button.transform, 8,
                go =>
                {
                    try
                    {
                        int candidate = isv.GetDataIndexByEntity(go);
                        return candidate >= 0 ? (true, candidate) : (false, 0);
                    }
                    catch { return (false, 0); }
                },
                out idx);
        }

        private static string FormatRoom(RoomEntryViewController vc, int idx)
        {
            var list = vc.dataList;
            if (list == null || idx < 0 || idx >= list.Count) return null;

            var data = list[idx];
            if (data == null) return null;

            var parts = new List<string>(5);
            string name = data.name?.Trim();
            parts.Add(string.IsNullOrEmpty(name) ? "Unnamed room" : name);

            string regulation = data.regulation?.Trim();
            if (!string.IsNullOrEmpty(regulation))
                parts.Add(regulation);

            if (data.memberMax > 0)
                parts.Add($"{data.memberNum} of {data.memberMax} members");

            string endDate = data.endDate?.Trim();
            if (!string.IsNullOrEmpty(endDate))
                parts.Add(endDate.StartsWith("Until", StringComparison.OrdinalIgnoreCase) ? endDate : $"until {endDate}");

            int total = list.Count;
            if (total > 1)
                parts.Add($"{idx + 1} of {total}");

            return string.Join(", ", parts);
        }
    }
}
