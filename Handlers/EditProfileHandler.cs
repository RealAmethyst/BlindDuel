using System;
using Il2CppYgomGame.Menu;
using Il2CppYgomGame.Utility;
using Il2CppYgomSystem.UI;
using UnityEngine;

namespace BlindDuel
{
    /// <summary>
    /// Edit Profile screen: left-column categories (Player Name, Icon, Icon Frame, Title, Mate,
    /// Wallpaper: Profile, Wallpaper: Home, Collector's file) + middle grid of pickable items.
    /// Grid items are owned by the VC's currentEditing ProfileEdit subclass.
    /// </summary>
    public class EditProfileHandler : IMenuHandler
    {
        public bool CanHandle(string viewControllerName) =>
            viewControllerName is "ProfileEditView" or "ProfileEditViewController" or "ProfileEdit";

        public bool OnScreenEntered(string viewControllerName)
        {
            string header = ScreenDetector.ReadGameHeaderText();
            Speech.AnnounceScreen(!string.IsNullOrWhiteSpace(header) ? header : "Edit Profile");
            return true;
        }

        public string OnButtonFocused(SelectionButton button)
        {
            try
            {
                var vc = ScreenDetector.GetFocusVC<ProfileEditViewController>();
                if (vc == null) return null;

                // Left-column category button? (inside SideMenu hierarchy)
                if (button.transform.GetComponentInParent<ProfileEditViewController.SideMenu>() != null)
                    return FormatCategory(button);

                // Middle-grid item cell: ask the currently-active ProfileEdit
                var editing = vc.currentEditing;
                if (editing != null)
                {
                    string grid = FormatGridItem(button, editing);
                    if (!string.IsNullOrEmpty(grid)) return grid;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Write($"[EditProfile] {ex.Message}");
                return null;
            }
        }

        // --- Left-column categories ---

        private static string FormatCategory(SelectionButton button)
        {
            string name = TextExtractor.ExtractFirst(button.gameObject);
            if (string.IsNullOrWhiteSpace(name)) return null;

            var (idx, total) = TransformSearch.GetButtonIndexAmongAncestors(button);
            if (total > 1 && idx > 0)
                return $"{name}, {idx} of {total}";
            return name;
        }

        // --- Middle-grid item cells ---

        private static string FormatGridItem(SelectionButton button, ProfileEditViewController.ProfileEdit editing)
        {
            var query = ResolveGridItem(button, editing);
            if (query == null) return null;

            int id = query.Value.id;
            var category = MapCategory(editing.sideMenuType);

            string name = null;
            try
            {
                if (category != ItemUtil.Category.NONE)
                    name = ItemUtil.GetItemName(false, (int)category, id, null);
            }
            catch { }

            if (string.IsNullOrWhiteSpace(name))
                name = TextExtractor.ExtractFirst(button.gameObject);
            if (string.IsNullOrWhiteSpace(name)) return null;

            string result = name;
            if (query.Value.currentlySet)
                result += ", currently set";
            if (query.Value.total > 0 && query.Value.index > 0)
                result += $", {query.Value.index} of {query.Value.total}";
            return result;
        }

        private struct GridQuery
        {
            public int id;
            public int index;
            public int total;
            public bool currentlySet;
        }

        private static GridQuery? ResolveGridItem(SelectionButton button, ProfileEditViewController.ProfileEdit editing)
        {
            // Try each known subclass — they all have m_InfinityScroll + an items list +
            // a selectedItemId property, but the list field names differ.

            var avatar = editing.TryCast<ProfileEditViewController.ProfileAvatarEdit>();
            if (avatar != null)
                return Query(button, avatar.m_InfinityScroll, avatar.itemIdList?.Count ?? 0,
                    i => UnboxInt(avatar.itemIdList, i), avatar.selectedItemId);

            var image = editing.TryCast<ProfileEditViewController.ProfileEditImage>();
            if (image != null)
                return Query(button, image.m_InfinityScroll, image.itemList?.Count ?? 0,
                    i => UnboxInt(image.itemList, i), image.selectedItemId);

            var cardfile = editing.TryCast<ProfileEditViewController.ProfileCardFileEdit>();
            if (cardfile != null)
                return Query(button, cardfile.m_InfinityScroll, cardfile.itemIdList?.Count ?? 0,
                    i => UnboxInt(cardfile.itemIdList, i), cardfile.selectedItemId);

            var wallpaper = editing.TryCast<ProfileEditViewController.ProfileHomeWallPaperEdit>();
            if (wallpaper != null)
                return Query(button, wallpaper.m_InfinityScroll, wallpaper.itemIdList?.Count ?? 0,
                    i => UnboxInt(wallpaper.itemIdList, i), wallpaper.selectedItemId);

            var tag = editing.TryCast<ProfileEditViewController.ProfileEditTag>();
            if (tag != null)
                return Query(button, tag.m_InfinityScroll, tag.tagList?.Count ?? 0,
                    i => UnboxInt(tag.tagList, i), tag.selectedItemId);

            return null;
        }

        private static GridQuery? Query(
            SelectionButton button,
            Il2CppYgomSystem.UI.InfinityScroll.InfinityScrollView scroll,
            int total,
            Func<int, int> idAt,
            int selectedItemId)
        {
            if (scroll == null || total == 0) return null;

            if (!TransformSearch.TryResolveByAncestor<int>(button.transform, 6,
                    go =>
                    {
                        int idx = scroll.GetDataIndexByEntity(go);
                        return idx >= 0 && idx < total ? (true, idx) : (false, 0);
                    },
                    out int dataIndex))
                return null;

            int id = idAt(dataIndex);
            return new GridQuery
            {
                id = id,
                index = dataIndex + 1,
                total = total,
                currentlySet = id == selectedItemId
            };
        }

        private static int UnboxInt(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Object> list, int index)
        {
            try
            {
                if (list == null || index < 0 || index >= list.Count) return -1;
                var obj = list[index];
                if (obj == null) return -1;
                string s = obj.ToString();
                return int.TryParse(s, out int v) ? v : -1;
            }
            catch { return -1; }
        }

        private static ItemUtil.Category MapCategory(ProfileEditViewController.SideMenuType type)
        {
            return type switch
            {
                ProfileEditViewController.SideMenuType.ICON => ItemUtil.Category.ICON,
                ProfileEditViewController.SideMenuType.ICONFRAME => ItemUtil.Category.ICON_FRAME,
                ProfileEditViewController.SideMenuType.TAG => ItemUtil.Category.PROFILE_TAG,
                ProfileEditViewController.SideMenuType.MATE => ItemUtil.Category.AVATAR,
                ProfileEditViewController.SideMenuType.MATEBASE => ItemUtil.Category.AVATAR,
                ProfileEditViewController.SideMenuType.MATE_DECK => ItemUtil.Category.AVATAR,
                ProfileEditViewController.SideMenuType.WALLPAPER_PROFILE => ItemUtil.Category.WALLPAPER,
                ProfileEditViewController.SideMenuType.WALLPAPER_HOME => ItemUtil.Category.AVATAR_HOME,
                ProfileEditViewController.SideMenuType.CARDFILE => ItemUtil.Category.CARD_FILE,
                ProfileEditViewController.SideMenuType.DECKCASE => ItemUtil.Category.DECK_CASE,
                ProfileEditViewController.SideMenuType.PROTECTOR => ItemUtil.Category.PROTECTOR,
                ProfileEditViewController.SideMenuType.FIELD => ItemUtil.Category.FIELD,
                ProfileEditViewController.SideMenuType.FIELDPARTS => ItemUtil.Category.FIELD_OBJ,
                _ => ItemUtil.Category.NONE,
            };
        }
    }
}
