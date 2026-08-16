namespace BlindDuel
{
    /// <summary>
    /// Tracks current navigation context: which screen is focused, current menu, last dialog.
    /// </summary>
    public static class NavigationState
    {
        public static Menu CurrentMenu { get; set; } = Menu.None;
        public static string LastFocusVCName { get; set; } = "";
        public static string LastDialogTitle { get; set; } = "";
        public static bool IsInDuel { get; set; }
        public static bool DialogJustAnnounced { get; set; }
        public static bool ScreenJustAnnounced { get; set; }
    }
}
