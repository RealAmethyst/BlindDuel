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
                int count = GetVC()?.dataList?.Count ?? -1;
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

                var vc = GetVC();
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

            Transform t = button.transform;
            for (int i = 0; i < 8 && t != null; i++)
            {
                try
                {
                    int candidate = isv.GetDataIndexByEntity(t.gameObject);
                    if (candidate >= 0) { idx = candidate; return true; }
                }
                catch { }
                t = t.parent;
            }
            return false;
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

        private static RoomEntryViewController GetVC()
        {
            try { return ScreenDetector.GetFocusVC()?.TryCast<RoomEntryViewController>(); }
            catch { return null; }
        }
    }
}
