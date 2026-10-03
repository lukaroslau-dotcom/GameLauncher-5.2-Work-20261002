namespace GameLauncher.Constants
{
    public static class GameStatus
    {
        public const string Installed = "Installed";
        public const string WaitingForUpdates = "Waiting for Updates";
        public const string Finished = "Finished";
        public const string Deleted = "Deleted";
        public const string Wishlist = "Wishlist";

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Installed;

            string value = status.Trim();
            if (string.Equals(value, Wishlist, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Wunschliste", StringComparison.OrdinalIgnoreCase)) return Wishlist;

            if (string.Equals(value, Installed, StringComparison.OrdinalIgnoreCase))
                return Installed;

            if (string.Equals(value, WaitingForUpdates, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "WaitingForUpdates", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Waiting", StringComparison.OrdinalIgnoreCase))
                return WaitingForUpdates;

            if (string.Equals(value, Finished, StringComparison.OrdinalIgnoreCase))
                return Finished;

            if (string.Equals(value, Deleted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Gelöscht", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Trash", StringComparison.OrdinalIgnoreCase))
                return Deleted;

            return Installed;
        }
    }
}
