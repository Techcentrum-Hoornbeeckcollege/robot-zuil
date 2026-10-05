using System.Globalization;
using System.Text;

namespace Zuil.Core.Text;

/// <summary>
/// Folds a typed-in name to a canonical form for exact matching, so that
/// "Jonathan  NAP", "jonathan nap" and "Jonáthan Nap" all hit the same key.
///
/// Pure and deterministic: the storage layer normalizes attendee names on write
/// and the search endpoint normalizes visitor input on read, so both sides must
/// agree exactly.
/// </summary>
public static class NameNormalizer
{
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Decompose accents, then drop the combining marks: "é" -> "e".
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true; // trims leading whitespace

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '-' or '\'' or '.')
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            if (!char.IsLetterOrDigit(ch))
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
            lastWasSpace = false;
        }

        return sb.ToString().TrimEnd();
    }
}
