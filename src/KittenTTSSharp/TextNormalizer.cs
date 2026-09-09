using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static KittenTTSSharp.EnglishNumbers;

namespace KittenTTSSharp;

/// <summary>Offsets are zero-based UTF-16 string indices with exclusive end positions.</summary>
public sealed record NormalizedSpan(int OriginalStartChar, int OriginalEndChar, int NormalizedStartChar, int NormalizedEndChar, string Reason);
public sealed record NormalizedTextResult(string Text, IReadOnlyList<NormalizedSpan> Spans);

public static class TextNormalizer
{
    private const string MonthsPattern = @"\b(Jan\.?|January|Feb\.?|February|Mar\.?|March|Apr\.?|April|May|Jun\.?|June|Jul\.?|July|Aug\.?|August|Sep\.?|Sept\.?|September|Oct\.?|October|Nov\.?|November|Dec\.?|December)";
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dr"] = "Doctor", ["prof"] = "Professor", ["mr"] = "Mister", ["mrs"] = "Misses", ["ms"] = "Ms",
        ["fig"] = "Figure", ["figs"] = "Figures", ["pp"] = "pages", ["p"] = "page", ["ch"] = "chapter", ["sec"] = "section"
    };

    public static string Normalize(string text, string locale = "en-US") => NormalizeWithSpans(text, locale).Text;

    public static NormalizedTextResult NormalizeWithSpans(string text, string locale = "en-US")
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!string.Equals(locale, "en-US", StringComparison.OrdinalIgnoreCase) && !string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only en-US text normalization is currently supported.");
        var state = new NormalizationState(text);
        state.Sub(@"<[^>]+>", _ => " ", "other");
        state.Sub(@"https?://\S+|www\.\S+", m => Spell(Regex.Replace(m.Value, @"^https?://", "", RegexOptions.IgnoreCase)), "url");
        state.Sub(@"\b[\w.+-]+@[\w-]+\.[a-z]{2,}\b", m => Spell(m.Value), "url", true);
        state.Sub(MonthsPattern + @"\s+([0-9]{1,2})(?:st|nd|rd|th)?(?:,)?\s+([0-9]{4})\b",
            m => $"{Month(m.Groups[1].Value)} {Ordinal(Parse(m.Groups[2].Value))}, {Year(int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))}", "date", true);
        state.Sub(MonthsPattern + @"\s+([0-9]{4})\b", m => $"{Month(m.Groups[1].Value)} {Year(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))}", "date", true);
        state.Sub(@"\b([0-9]{1,2}):([0-9]{2})(?::([0-9]{2}))?\s*(a\.?m\.?|p\.?m\.?)?\b", ReadTime, "time", true);
        state.Sub(CurrencyPattern, Currency, "currency");
        state.Sub(PercentPattern, m => Number(m.Groups[1].Value) + " percent", "number");
        state.Sub(OrdinalPattern, m => Ordinal(Parse(m.Groups[1].Value)), "ordinal", true);
        state.Sub(@"\bet\s+al\.", _ => "et al", "citation", true);
        state.Sub(@"\b(Dr|Prof|Mr|Mrs|Ms|Fig|Figs|pp|p|ch|sec)\.", m => Abbreviations[m.Groups[1].Value], "abbreviation", true);
        state.Sub(@"\b[vV]?[0-9]+(?:\.[0-9]+){2,}\b", m => Version(m.Value), "number");
        state.Sub(RangePattern, m =>
        {
            var left = Parse(m.Groups[1].Value);
            var right = Parse(m.Groups[2].Value);
            var years = left >= 1900 && left <= 2099 && right >= 1900 && right <= 2099;
            return Number(m.Groups[1].Value, years) + " to " + Number(m.Groups[2].Value, years);
        }, "number");
        state.Sub(ModelPattern, m => m.Groups[1].Value + " " + Version(m.Groups[2].Value), "number");
        state.Sub(NumberPattern, m => Number(m.Value, years: true), "number");
        state.Sub(@"[^\w\s.,?!;:\-\u2014\u2013\u2026]", _ => " ", "punctuation");
        state.Sub(@"\s+", _ => " ", "internal");
        return state.Finish();
    }

    private static string Month(string raw)
    {
        var key = raw[..3].ToLowerInvariant();
        string[] keys = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
        return CultureInfo.GetCultureInfo("en-US").DateTimeFormat.GetMonthName(Array.IndexOf(keys, key) + 1);
    }

    private static string Spell(string text) => string.Join(' ', text.ToLowerInvariant().Select(c => c switch
    {
        >= '0' and <= '9' => Digits(c.ToString()), '.' => "dot", '-' => "dash", '_' => "underscore", '@' => "at",
        '/' => "slash", '?' => "question mark", '&' => "and", '=' => "equals", _ => char.IsLetter(c) ? c.ToString() : ""
    }).Where(s => s.Length > 0));

    private static string ReadTime(Match m)
    {
        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var suffix = m.Groups[4].Success ? (char.ToLowerInvariant(m.Groups[4].Value[0]) == 'a' ? " a m" : " p m") : "";
        if (suffix.Length > 0 && hour > 12) hour -= 12;
        var result = Integer(hour) + (minute == 0 ? "" : minute < 10 ? " oh " + Integer(minute) : " " + Integer(minute));
        if (m.Groups[3].Success) result += " and " + Integer(Parse(m.Groups[3].Value)) + " seconds";
        return result + suffix;
    }

    private sealed class NormalizationState
    {
        private string text;
        private List<(int Start, int End)?> origins = [];
        private readonly List<NormalizedSpan> spans = [];

        public NormalizationState(string original)
        {
            var builder = new StringBuilder();
            var elements = StringInfo.GetTextElementEnumerator(original);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                var normalized = element.Normalize(NormalizationForm.FormC);
                builder.Append(normalized);
                for (var i = 0; i < normalized.Length; i++) origins.Add((elements.ElementIndex, elements.ElementIndex + element.Length));
            }
            text = builder.ToString();
        }

        public void Sub(string pattern, MatchEvaluator replace, string reason, bool ignoreCase = false)
        {
            var replacements = Regex.Matches(text, pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(5))
                .Select(m => (Start: m.Index, End: m.Index + m.Length, Text: replace(m), Before: m.Value)).Where(r => r.Text != r.Before).ToArray();
            if (replacements.Length == 0) return;
            int MapPosition(int position)
            {
                var shift = 0;
                foreach (var r in replacements)
                {
                    if (position >= r.End) shift += r.Text.Length - (r.End - r.Start);
                    else if (position > r.Start) return r.Start + shift + r.Text.Length;
                    else break;
                }
                return position + shift;
            }
            for (var i = 0; i < spans.Count; i++) spans[i] = spans[i] with
            {
                NormalizedStartChar = MapPosition(spans[i].NormalizedStartChar), NormalizedEndChar = MapPosition(spans[i].NormalizedEndChar)
            };
            var builder = new StringBuilder();
            var mapped = new List<(int Start, int End)?>();
            var cursor = 0;
            foreach (var r in replacements)
            {
                builder.Append(text[cursor..r.Start]);
                mapped.AddRange(origins.GetRange(cursor, r.Start - cursor));
                var source = origins.GetRange(r.Start, r.End - r.Start).Where(p => p.HasValue).Select(p => p!.Value).ToArray();
                if (source.Length > 0 && reason != "internal")
                    spans.Add(new(source.Min(p => p.Start), source.Max(p => p.End), builder.Length, builder.Length + r.Text.Length, reason));
                builder.Append(r.Text);
                mapped.AddRange(Enumerable.Repeat<(int, int)?>(null, r.Text.Length));
                cursor = r.End;
            }
            builder.Append(text[cursor..]);
            mapped.AddRange(origins.GetRange(cursor, origins.Count - cursor));
            text = builder.ToString();
            origins = mapped;
        }

        public NormalizedTextResult Finish()
        {
            var leading = text.Length - text.TrimStart().Length;
            text = text.Trim();
            var result = spans.Select(s => s with
            {
                NormalizedStartChar = Math.Clamp(s.NormalizedStartChar - leading, 0, text.Length),
                NormalizedEndChar = Math.Clamp(s.NormalizedEndChar - leading, 0, text.Length)
            }).OrderBy(s => s.OriginalStartChar).ThenBy(s => s.OriginalEndChar).ThenBy(s => s.Reason, StringComparer.Ordinal).ToArray();
            return new(text, Array.AsReadOnly(result));
        }
    }
}
