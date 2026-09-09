using System.Text;
using System.Text.RegularExpressions;
using static KittenTTSSharp.EnglishNumbers;

namespace KittenTTSSharp;

/// <summary>The Python synthesis pipeline's default clean_text behavior, retaining punctuation.</summary>
public static class TextPreprocessor
{
    private static readonly Dictionary<string, string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["km"] = "kilometers", ["kg"] = "kilograms", ["mg"] = "milligrams", ["ml"] = "milliliters",
        ["gb"] = "gigabytes", ["mb"] = "megabytes", ["kb"] = "kilobytes", ["tb"] = "terabytes",
        ["hz"] = "hertz", ["khz"] = "kilohertz", ["mhz"] = "megahertz", ["ghz"] = "gigahertz",
        ["mph"] = "miles per hour", ["kph"] = "kilometers per hour", ["ms"] = "milliseconds",
        ["ns"] = "nanoseconds", ["µs"] = "microseconds", ["°c"] = "degrees Celsius",
        ["c°"] = "degrees Celsius", ["°f"] = "degrees Fahrenheit", ["f°"] = "degrees Fahrenheit"
    };

    public static string Process(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Normalize(NormalizationForm.FormC);
        text = Sub(text, @"<[^>]+>", _ => "");
        text = Sub(text, @"https?://\S+|www\.\S+", _ => "");
        text = Sub(text, @"\b[\w.+-]+@[\w-]+\.[a-z]{2,}\b", _ => "", true);
        foreach (var (pattern, replacement) in new (string, string)[]
        {
            (@"\bcan't\b", "cannot"), (@"\bwon't\b", "will not"), (@"\bshan't\b", "shall not"),
            (@"\bain't\b", "is not"), (@"\blet's\b", "let us"), (@"\b(\w+)n't\b", "$1 not"),
            (@"\b(\w+)'re\b", "$1 are"), (@"\b(\w+)'ve\b", "$1 have"), (@"\b(\w+)'ll\b", "$1 will"),
            (@"\b(\w+)'d\b", "$1 would"), (@"\b(\w+)'m\b", "$1 am"), (@"\bit's\b", "it is")
        }) text = Sub(text, pattern, m => m.Result(replacement), true);
        text = Sub(text, @"\b([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,3})\b",
            m => string.Join(" dot ", m.Groups.Cast<Group>().Skip(1).Select(g => Digits(g.Value))));
        text = Sub(text, @"(?<![0-9])(-)\.([0-9])", m => "-0." + m.Groups[2].Value);
        text = Sub(text, @"(?<![0-9])\.([0-9])", m => "0." + m.Groups[1].Value);
        text = Sub(text, CurrencyPattern, Currency);
        text = Sub(text, PercentPattern, m => Number(TrimDecimal(m.Groups[1].Value)) + " percent");
        text = Sub(text, @"(?<![a-zA-Z0-9])(-?[0-9]+(?:\.[0-9]+)?)[eE]([+-]?[0-9]+)(?![a-zA-Z0-9])",
            m => Number(m.Groups[1].Value) + " times ten to the " + Integer(Parse(m.Groups[2].Value)));
        text = Sub(text, @"\b([0-9]{1,2}):([0-9]{2})(?::([0-9]{2}))?\s*(am|pm)?\b", m =>
        {
            var minutes = Parse(m.Groups[2].Value);
            var suffix = m.Groups[4].Success ? " " + m.Groups[4].Value.ToLowerInvariant() : "";
            return Integer(Parse(m.Groups[1].Value)) + (minutes == 0 ? suffix.Length == 0 ? " hundred" : "" :
                (minutes < 10 ? " oh " : " ") + Integer(minutes)) + suffix;
        }, true);
        text = Sub(text, OrdinalPattern, m => Ordinal(Parse(m.Groups[1].Value)), true);
        text = Sub(text, @"([0-9]+(?:\.[0-9]+)?)\s*(km|kg|mg|ml|gb|mb|kb|tb|hz|khz|mhz|ghz|mph|kph|°[cCfF]|[cCfF]°|ms|ns|µs)\b",
            m => Number(TrimDecimal(m.Groups[1].Value)) + " " + Units[m.Groups[2].Value], true);
        text = Sub(text, @"(?<![a-zA-Z])([0-9]+(?:\.[0-9]+)?)\s*([KMBT])(?![a-zA-Z0-9])",
            m => Number(m.Groups[1].Value) + " " + Scale(m.Groups[2].Value));
        text = Sub(text, @"\b([0-9]+)\s*/\s*([0-9]+)\b", m =>
        {
            var numerator = Parse(m.Groups[1].Value);
            var denominator = Parse(m.Groups[2].Value);
            if (denominator == 0) return m.Value;
            var word = denominator == 2 ? numerator == 1 ? "half" : "halves" :
                denominator == 4 ? numerator == 1 ? "quarter" : "quarters" : Ordinal(denominator) + (numerator == 1 ? "" : "s");
            return Integer(numerator) + " " + word;
        });
        text = Sub(text, @"\b([0-9]{1,3})0s\b", m =>
        {
            string[] decades = ["hundreds", "tens", "twenties", "thirties", "forties", "fifties", "sixties", "seventies", "eighties", "nineties"];
            var value = Parse(m.Groups[1].Value);
            return (value < 10 ? "" : Integer(value / 10) + " ") + decades[(int)(value % 10)];
        });
        foreach (var pattern in new[]
        {
            @"(?<!\d-)(?<!\d)\b([0-9]{1,2})-([0-9]{3})-([0-9]{3})-([0-9]{4})\b(?!-\d)",
            @"(?<!\d-)(?<!\d)\b([0-9]{3})-([0-9]{3})-([0-9]{4})\b(?!-\d)",
            @"(?<!\d-)\b([0-9]{3})-([0-9]{4})\b(?!-\d)"
        }) text = Sub(text, pattern, m => string.Join(' ', m.Groups.Cast<Group>().Skip(1).Select(g => Digits(g.Value))));
        text = Sub(text, RangePattern, m => Integer(Parse(m.Groups[1].Value)) + " to " + Integer(Parse(m.Groups[2].Value)));
        text = Sub(text, ModelPattern, m => m.Groups[1].Value + " " + m.Groups[2].Value);
        text = Sub(text, NumberPattern, m => Number(m.Value));
        return Sub(text.ToLowerInvariant(), @"\s+", _ => " ").Trim();
    }

    private static string TrimDecimal(string value)
    {
        if (!value.Contains('.')) return value;
        value = value.TrimEnd('0');
        return value.EndsWith('.') ? value + "0" : value;
    }

    private static string Sub(string text, string pattern, MatchEvaluator replace, bool ignoreCase = false) =>
        Regex.Replace(text, pattern, replace, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(5));
}
