using System;
using Il2CppYgomGame.Room;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    public class RoomHandler : IMenuHandler
    {
        private static int _lastMembers = -1;
        private static int _lastSpecters = -1;

        // Flat per-seat layout: index 2*i = left seat of table i, 2*i+1 = right seat.
        private static long[] _seatPcodes = System.Array.Empty<long>();
        private static string[] _seatNames = System.Array.Empty<string>();

        public bool CanHandle(string viewControllerName) => viewControllerName == "Room";

        public bool OnScreenEntered(string viewControllerName)
        {
            _lastMembers = -1;
            _lastSpecters = -1;
            _seatPcodes = System.Array.Empty<long>();
            _seatNames = System.Array.Empty<string>();
            Speech.AnnounceScreen(BuildScreenAnnouncement());
            return true;
        }

        public static void Poll()
        {
            try
            {
                if (HandlerRegistry.Current is not RoomHandler)
                {
                    _lastMembers = -1;
                    _lastSpecters = -1;
                    _seatPcodes = System.Array.Empty<long>();
                    _seatNames = System.Array.Empty<string>();
                    return;
                }

                var vc = ScreenDetector.GetFocusVC<RoomViewController>();
                var info = vc?.roomBehaviour?.roomInfo;
                if (info == null) return;

                ForceDisableCommentButtons(vc);

                int members = info.memberNum;
                int max = info.memberMax;
                int specters = info.specterNum;

                if (_lastMembers < 0)
                {
                    _lastMembers = members;
                    _lastSpecters = specters;
                    return;
                }

                if (members != _lastMembers)
                {
                    string verb = members > _lastMembers ? "joined" : "left";
                    _lastMembers = members;
                    Speech.SayImmediate(max > 0
                        ? $"Player {verb}, {members} of {max} members"
                        : $"Player {verb}, {members} members");
                }

                if (specters != _lastSpecters)
                {
                    int diff = specters - _lastSpecters;
                    _lastSpecters = specters;
                    Speech.SayImmediate(diff > 0
                        ? $"Spectator joined, {specters} watching"
                        : $"Spectator left, {specters} watching");
                }

                var tables = vc.roomBehaviour?.tableDataList;
                if (tables == null) return;
                int seatCount = tables.Count * 2;

                // Snapshot without announcing on the first poll (or after table-count change).
                if (_seatPcodes.Length != seatCount)
                {
                    _seatPcodes = new long[seatCount];
                    _seatNames = new string[seatCount];
                    for (int i = 0; i < tables.Count; i++)
                    {
                        var t = tables[i];
                        var (lp, ln) = ReadSeat(t?.members, 0);
                        var (rp, rn) = ReadSeat(t?.members, 1);
                        _seatPcodes[2 * i] = lp; _seatNames[2 * i] = ln;
                        _seatPcodes[2 * i + 1] = rp; _seatNames[2 * i + 1] = rn;
                    }
                    return;
                }

                for (int i = 0; i < tables.Count; i++)
                {
                    var t = tables[i];
                    if (t == null) continue;
                    AnnounceSeatChange(i + 1, "left",  2 * i,     t.members, 0);
                    AnnounceSeatChange(i + 1, "right", 2 * i + 1, t.members, 1);
                }
            }
            catch { }
        }

        private static (long pcode, string name) ReadSeat(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RoomViewController.RoomBehaviour.MemberData> members,
            int slot)
        {
            try
            {
                if (members == null || slot < 0 || slot >= members.Length) return (0, null);
                var m = members[slot];
                if (m == null) return (0, null);
                long pcode = m.pcode;
                if (pcode <= 0) return (0, null);
                string name = m.name?.Trim();
                return (pcode, string.IsNullOrWhiteSpace(name) ? null : name);
            }
            catch { return (0, null); }
        }

        private static void AnnounceSeatChange(
            int tableNum, string side, int stateIdx,
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RoomViewController.RoomBehaviour.MemberData> members,
            int slot)
        {
            var (pcode, name) = ReadSeat(members, slot);
            if (pcode == _seatPcodes[stateIdx]) return;

            if (pcode > 0)
                Speech.SayImmediate($"{name ?? "A player"} sat at Table {tableNum}, {side} seat");
            else
                Speech.SayImmediate($"{_seatNames[stateIdx] ?? "A player"} left Table {tableNum}, {side} seat");

            _seatPcodes[stateIdx] = pcode;
            _seatNames[stateIdx] = name;
        }

        private static string BuildScreenAnnouncement()
        {
            string header = ScreenDetector.ReadGameHeaderText();
            string fallback = !string.IsNullOrWhiteSpace(header) ? header : "Duel Room";

            try
            {
                var info = ScreenDetector.GetFocusVC<RoomViewController>()?.roomBehaviour?.roomInfo;
                if (info == null) return fallback;

                string name = info.roomName?.Trim();
                string regulation = info.regulation?.Trim();
                int members = info.memberNum;
                int max = info.memberMax;
                int specters = info.specterNum;

                string result = !string.IsNullOrWhiteSpace(name) ? name : fallback;
                if (!string.IsNullOrWhiteSpace(regulation))
                    result += $", {regulation}";
                if (max > 0)
                    result += $", {members} of {max} members";
                if (specters > 0)
                    result += $", {specters} watching";

                return result;
            }
            catch (Exception ex)
            {
                Log.Write($"[RoomHandler] Screen error: {ex.Message}");
                return fallback;
            }
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                var vc = ScreenDetector.GetFocusVC<RoomViewController>();
                if (vc != null && TryGetTableIndex(vc, button, out int tableIdx))
                    return HandleTableRow(vc, tableIdx, button.name);

                if (button.name == "ButtonDeck")
                    return HandleDeckButton(button);

                return HandleMenuButton(button);
            }
            catch (Exception ex)
            {
                Log.Write($"[RoomHandler] Button error: {ex.Message}");
                return null;
            }
        }

        // Keep ButtonCommentLeft/Right deactivated: their picker dialog (Table Comments
        // ActionSheet) has a game-side off-by-one between visible entry and click target,
        // so we hide the entry point. Reading others' comments via "says X" still works.
        private static void ForceDisableCommentButtons(RoomViewController vc)
        {
            try
            {
                if (vc == null) return;
                var sbs = vc.gameObject.GetComponentsInChildren<SelectionButton>(true);
                if (sbs == null) return;
                foreach (var sb in sbs)
                {
                    if (sb == null || sb.WasCollected) continue;
                    if ((sb.name == "ButtonCommentLeft" || sb.name == "ButtonCommentRight")
                        && sb.gameObject.activeSelf)
                    {
                        sb.gameObject.SetActive(false);
                    }
                }
            }
            catch (Exception ex) { Log.Write($"[RoomHandler] ForceDisableCommentButtons: {ex.Message}"); }
        }

        private static bool TryGetTableIndex(RoomViewController vc, SelectionButton button, out int idx)
        {
            idx = -1;
            try
            {
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
            catch { }
            return false;
        }

        private static string HandleTableRow(RoomViewController vc, int idx, string buttonName)
        {
            var rb = vc.roomBehaviour;
            var list = rb?.tableDataList;
            if (list == null || idx < 0 || idx >= list.Count) return null;

            var table = list[idx];
            if (table == null) return null;

            int total = list.Count;
            int tableNum = idx + 1;
            var comments = rb._tableComments;

            if (buttonName == "ButtonLeave")
                return $"Leave Table {tableNum}";

            string result = $"Table {tableNum}";

            var (leftName, leftComment) = GetSeatInfo(table.members, 0, comments);
            var (rightName, rightComment) = GetSeatInfo(table.members, 1, comments);

            if (leftName == null && rightName == null)
            {
                result += ", empty";
            }
            else
            {
                if (leftName != null)
                {
                    result += $", left: {leftName}";
                    if (leftComment != null) result += $", says {leftComment}";
                }
                else result += ", left empty";

                if (rightName != null)
                {
                    result += $", right: {rightName}";
                    if (rightComment != null) result += $", says {rightComment}";
                }
                else result += ", right empty";
            }

            string status = StatusText(table.status);
            if (!string.IsNullOrEmpty(status))
                result += $", {status}";

            try
            {
                if (table.myPlayerEntry)
                    result += ", you are here";
            }
            catch { }

            if (total > 1)
                result += $", {tableNum} of {total}";

            return result;
        }

        private static (string name, string comment) GetSeatInfo(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RoomViewController.RoomBehaviour.MemberData> members,
            int slot,
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray comments)
        {
            try
            {
                if (members == null || slot < 0 || slot >= members.Length) return (null, null);
                var m = members[slot];
                if (m == null || m.pcode <= 0) return (null, null);

                string name = m.name?.Trim();
                if (string.IsNullOrWhiteSpace(name)) name = null;

                string comment = null;
                int cid = m.commentID;
                if (comments != null && cid > 0 && cid < comments.Length)
                {
                    string c = comments[cid]?.Trim();
                    if (!string.IsNullOrWhiteSpace(c)) comment = c;
                }
                return (name, comment);
            }
            catch { return (null, null); }
        }

        private static string StatusText(RoomViewController.RoomBehaviour.TableStatus s) => s switch
        {
            RoomViewController.RoomBehaviour.TableStatus.WAIT => "waiting",
            RoomViewController.RoomBehaviour.TableStatus.READY_LEFT => "left player ready",
            RoomViewController.RoomBehaviour.TableStatus.READY_RIGHT => "right player ready",
            RoomViewController.RoomBehaviour.TableStatus.DUEL => "dueling",
            RoomViewController.RoomBehaviour.TableStatus.SPECTATE => "spectator only",
            _ => null,
        };

        private static string HandleDeckButton(SelectionButton button)
        {
            string text = TextExtractor.ExtractFirst(button.gameObject);
            if (string.IsNullOrWhiteSpace(text)) return "Deck";
            return $"Deck, {text}";
        }

        private static string HandleMenuButton(SelectionButton button)
        {
            string text = TextExtractor.ExtractFirst(button.gameObject);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var (idx, total) = TransformSearch.GetButtonIndexAmongAncestors(button);
            if (total > 1 && idx > 0)
                return $"{text}, {idx} of {total}";
            return text;
        }
    }
}
