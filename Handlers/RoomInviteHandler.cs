using System;
using Il2CppYgomGame.Friend;
using Il2CppYgomGame.Room;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class RoomInviteHandler : IMenuHandler
    {
        private static int _lastSelectedCount = -1;
        private static int _lastMax = -1;

        // dataList is populated async; at screen entry an empty list might just be
        // loading. Wait this many frames (~1.5s at 60fps) before announcing "empty".
        private const int EmptyGraceFrames = 90;
        private static int _emptyFrameCount;
        private static bool _emptyAnnounced;

        public bool CanHandle(string viewControllerName) => viewControllerName == "RoomInvite";

        public bool OnScreenEntered(string viewControllerName)
        {
            _lastSelectedCount = -1;
            _lastMax = -1;
            _emptyFrameCount = 0;
            _emptyAnnounced = false;

            string header = ScreenDetector.ReadGameHeaderText();
            string announcement = !string.IsNullOrWhiteSpace(header) ? header : "Invite Friends";

            try
            {
                var vc = ScreenDetector.GetFocusVC<RoomInviteViewController>();
                if (vc != null && vc.maxInvite > 0)
                {
                    announcement += $", {vc.currentInvite} of {vc.maxInvite} selected";
                    _lastSelectedCount = vc.currentInvite;
                    _lastMax = vc.maxInvite;
                }
            }
            catch (Exception ex) { Log.Write($"[RoomInviteHandler] Screen error: {ex.Message}"); }

            Speech.AnnounceScreen(announcement);
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                var vc = ScreenDetector.GetFocusVC<RoomInviteViewController>();
                if (vc == null) return null;

                if (TryGetFocusedRow(vc, button, out var widget, out var rowGo))
                    return FormatFriendRow(vc, widget, rowGo);
            }
            catch (Exception ex)
            {
                Log.Write($"[RoomInviteHandler] Button error: {ex.Message}");
            }
            return null;
        }

        // Toggling a friend row updates currentInvite without firing a screen change,
        // so we re-announce the new count here when it shifts.
        public static void Poll()
        {
            try
            {
                if (HandlerRegistry.Current is not RoomInviteHandler)
                {
                    _lastSelectedCount = -1;
                    _emptyFrameCount = 0;
                    _emptyAnnounced = false;
                    return;
                }

                var vc = ScreenDetector.GetFocusVC<RoomInviteViewController>();
                if (vc == null) return;

                var list = vc.dataList;
                bool listEmpty = (list == null || list.Count == 0);

                if (listEmpty)
                {
                    if (!_emptyAnnounced)
                    {
                        _emptyFrameCount++;
                        if (_emptyFrameCount >= EmptyGraceFrames)
                        {
                            Speech.SayQueued("No friends available to invite");
                            _emptyAnnounced = true;
                        }
                    }
                    return;
                }

                _emptyFrameCount = 0;

                int count = vc.currentInvite;
                int max = vc.maxInvite;

                if (_lastSelectedCount < 0)
                {
                    _lastSelectedCount = count;
                    _lastMax = max;
                }
                else if (count != _lastSelectedCount || max != _lastMax)
                {
                    _lastSelectedCount = count;
                    _lastMax = max;
                    Speech.SayImmediate(max > 0 ? $"{count} of {max} selected" : $"{count} selected");
                }
            }
            catch { }
        }

        private static bool TryGetFocusedRow(
            RoomInviteViewController vc, SelectionButton button,
            out FriendWidget widget, out GameObject rowGo)
        {
            widget = null;
            rowGo = null;

            var map = vc.m_EntityWidgetMap;
            if (map == null) return false;

            bool found = TransformSearch.TryResolveByAncestor<(FriendWidget widget, GameObject rowGo)>(
                button.transform, 6,
                go => map.TryGetValue(go, out var w) ? (true, (w, go)) : (false, default),
                out var result);

            if (found)
            {
                widget = result.widget;
                rowGo = result.rowGo;
            }
            return found;
        }

        private static string FormatFriendRow(
            RoomInviteViewController vc, FriendWidget widget, GameObject rowGo)
        {
            string name = null;
            try
            {
                var group = widget.platformPlayerNameGroup;
                if (group != null) name = group.GetYmdPlayerName();
            }
            catch { }
            if (string.IsNullOrWhiteSpace(name)) return null;

            int idx = -1;
            try { idx = vc.isv?.GetDataIndexByEntity(rowGo) ?? -1; } catch { }

            bool? online = null;
            bool? selected = null;
            int total = 0;
            try
            {
                var list = vc.dataList;
                if (list != null)
                {
                    total = list.Count;
                    if (idx >= 0 && idx < total)
                    {
                        var data = list[idx];
                        online = data.isOnline;
                        selected = data.isSelected;
                    }
                }
            }
            catch { }

            string result = name;
            if (online.HasValue)
                result += online.Value ? ", online" : ", offline";
            if (selected == true)
                result += ", selected";
            if (idx >= 0 && total > 1)
                result += $", {idx + 1} of {total}";

            return result;
        }
    }
}
