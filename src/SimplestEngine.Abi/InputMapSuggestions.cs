namespace SimplestEngine.Abi;

/// <summary>
/// Mirrors Pandemonium / Godot 3 <c>InputMap::suggest_actions</c> used when code
/// queries an action that was never registered (<c>InputDefault::is_action_pressed</c>).
/// </summary>
public static class InputMapSuggestions
{
    /// <summary>
    /// Builds the same human-readable message Godot prints: unknown action plus an
    /// optional "Did you mean …?" when a registered name is similar enough (≥0.4).
    /// </summary>
    public static string FormatMissingActionMessage(string requested, IEnumerable<string> knownActionNames)
    {
        var msg = $"The InputMap action \"{requested}\" doesn't exist.";
        string? best = null;
        double bestScore = 0;
        foreach (var s in knownActionNames)
        {
            if (string.IsNullOrEmpty(s)) continue;
            var score = Similarity(requested, s);
            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }
        if (bestScore >= 0.4 && best is not null)
            msg += $" Did you mean \"{best}\"?";
        return msg;
    }

    /// <summary>Normalised Levenshtein similarity in [0,1], same spirit as Godot's <c>String::similarity</c>.</summary>
    public static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        int d = Levenshtein(a, b);
        int m = Math.Max(a.Length, b.Length);
        return m == 0 ? 1.0 : 1.0 - (double)d / m;
    }

    private static int Levenshtein(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;
        var row = new int[m + 1];
        for (int j = 0; j <= m; j++) row[j] = j;
        for (int i = 1; i <= n; i++)
        {
            int prevDiag = row[0];
            row[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int temp = row[j];
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), prevDiag + cost);
                prevDiag = temp;
            }
        }
        return row[m];
    }
}
