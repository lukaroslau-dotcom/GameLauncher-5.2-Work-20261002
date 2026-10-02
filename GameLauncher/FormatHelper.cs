namespace GameLauncher
{
    public static class FormatHelper
    {
        public static string FormatPlaytime(TimeSpan playtime)
        {
            int hours = (int)playtime.TotalHours;
            int minutes = playtime.Minutes;
            return $"{hours} Std. {minutes} Min.";
        }

        public static string FormatLastPlayed(DateTime? lastPlayed)
        {
            if (!lastPlayed.HasValue)
                return "Noch nie gespielt";

            DateTime value = lastPlayed.Value;
            DateTime today = DateTime.Now.Date;

            if (value.Date == today)
                return "Heute";

            if (value.Date == today.AddDays(-1))
                return "Gestern";

            return value.ToString("dd.MM.yyyy HH:mm");
        }
    }
}
