using System.Globalization;
using System.Text;
using GameLauncher.models;

namespace GameLauncher.Service;

public static class LocalGameSearch
{
    public static bool Matches(Game game, string search, bool allowFuzzy = true)
    {
        string wanted = Key(search);
        if (wanted.Length == 0 || wanted.Length > 128) return false;
        foreach (string name in new[] { game.Name }.Concat(game.Aliases ?? new()).Concat(game.Tags ?? new()))
        {
            string key = Key(name);
            if (key.Contains(wanted, StringComparison.Ordinal)) return true;
            string abbreviation = string.Concat(name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0])).ToLowerInvariant();
            if (wanted.Length >= 2 && wanted == abbreviation) return true;
            if (allowFuzzy && wanted.Length >= 5 && Math.Abs(key.Length - wanted.Length) <= 2 && Distance(key, wanted) <= (wanted.Length >= 9 ? 2 : 1)) return true;
        }
        return false;
    }
    public static string Key(string value) => new string((value ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
        .Take(256).Select(char.ToLowerInvariant).ToArray());
    private static int Distance(string left, string right)
    {
        int[] row = Enumerable.Range(0, right.Length + 1).ToArray();
        int[]? twoRowsBack = null;
        for (int i = 1; i <= left.Length; i++)
        {
            int[] previousRow = row.ToArray();
            int diagonal = row[0]; row[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int previous = row[j]; row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (left[i - 1] == right[j - 1] ? 0 : 1)); diagonal = previous;
                if (i > 1 && j > 1 && left[i - 1] == right[j - 2] && left[i - 2] == right[j - 1]) row[j] = Math.Min(row[j], twoRowsBack![j - 2] + 1);
            }
            twoRowsBack = previousRow;
        }
        return row[^1];
    }
}
