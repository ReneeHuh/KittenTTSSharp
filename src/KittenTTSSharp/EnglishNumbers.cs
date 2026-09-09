using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace KittenTTSSharp;

internal static class EnglishNumbers
{
    private static readonly string[] Ones = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
    private static readonly string[] Scales = ["", "thousand", "million", "billion", "trillion", "quadrillion", "quintillion", "sextillion", "septillion", "octillion", "nonillion", "decillion"];
    internal const string NumberPattern = @"(?<![a-zA-Z])-?[0-9][0-9,]*(?:\.[0-9]+)?";
    internal const string CurrencyPattern = @"([$€£¥₹₩₿])\s*([0-9,]+(?:\.[0-9]+)?)\s*([KMBT])?(?![a-zA-Z0-9])";
    internal const string PercentPattern = @"(-?[0-9,]+(?:\.[0-9]+)?)\s*%";
    internal const string OrdinalPattern = @"\b([0-9]+)(st|nd|rd|th)\b";
    internal const string RangePattern = @"(?<!\w)([0-9]+)-([0-9]+)(?!\w)";
    internal const string ModelPattern = @"\b([a-zA-Z][a-zA-Z0-9]*)-([0-9][0-9.]*)(?=[^0-9.]|$)";

    internal static BigInteger Parse(string text) => BigInteger.Parse(text.Replace(",", ""), CultureInfo.InvariantCulture);
    internal static string Integer(BigInteger value)
    {
        if (value < 0) return "negative " + Integer(-value);
        if (value == 0) return "zero";
        if (value >= 100 && value <= 1900 && value % 100 == 0 && value % 1000 != 0)
            return Ones[(int)(value / 100)] + " hundred";
        if (value >= BigInteger.Pow(1000, Scales.Length)) return Digits(value.ToString(CultureInfo.InvariantCulture));
        var parts = new List<string>();
        for (var scale = 0; value > 0; scale++, value /= 1000)
        {
            var chunk = (int)(value % 1000);
            if (chunk == 0) continue;
            var words = chunk >= 100 ? Ones[chunk / 100] + " hundred" : "";
            var remainder = chunk % 100;
            if (remainder != 0)
            {
                if (words.Length > 0) words += " ";
                words += remainder < 20 ? Ones[remainder] : Tens[remainder / 10] + (remainder % 10 == 0 ? "" : "-" + Ones[remainder % 10]);
            }
            parts.Add(words + (scale == 0 ? "" : " " + Scales[scale]));
        }
        parts.Reverse();
        return string.Join(' ', parts);
    }

    internal static string Digits(string text) => string.Join(' ', text.Select(c => Ones[c - '0']));

    internal static string Number(string raw, bool years = false)
    {
        raw = raw.Replace(",", "");
        if (raw.StartsWith('-')) return "negative " + Number(raw[1..], years);
        var parts = raw.Split('.');
        if (parts.Length > 1) return Integer(Parse(parts[0].Length == 0 ? "0" : parts[0])) + " point " + Digits(parts[1]);
        var n = Parse(raw);
        return years && n >= 1900 && n <= 2099 ? Year((int)n) : Integer(n);
    }

    internal static string Year(int n) => n switch
    {
        >= 1900 and <= 1999 => "nineteen " + (n % 100 == 0 ? "hundred" : Integer(n % 100)),
        >= 2000 and <= 2009 => "two thousand" + (n % 100 == 0 ? "" : " " + Integer(n % 100)),
        >= 2010 and <= 2099 => "twenty " + Integer(n % 100),
        _ => Integer(n)
    };

    internal static string Ordinal(BigInteger n)
    {
        var words = Integer(n);
        var split = Math.Max(words.LastIndexOf('-'), words.LastIndexOf(' '));
        var last = words[(split + 1)..];
        var ordinal = last switch
        {
            "one" => "first", "two" => "second", "three" => "third", "four" => "fourth", "five" => "fifth",
            "six" => "sixth", "seven" => "seventh", "eight" => "eighth", "nine" => "ninth", "twelve" => "twelfth",
            _ when last.EndsWith('t') => last + "h",
            _ when last.EndsWith('e') => last[..^1] + "th",
            _ => last + "th"
        };
        return words[..(split + 1)] + ordinal;
    }

    internal static string Scale(string suffix) => suffix switch { "K" => "thousand", "M" => "million", "B" => "billion", "T" => "trillion", _ => "" };

    internal static string Currency(Match m)
    {
        var unit = m.Groups[1].Value switch { "$" => "dollar", "€" => "euro", "£" => "pound", "¥" => "yen", "₹" => "rupee", "₩" => "won", "₿" => "bitcoin", _ => "" };
        var raw = m.Groups[2].Value.Replace(",", "");
        if (m.Groups[3].Success) return $"{Number(raw)} {Scale(m.Groups[3].Value)} {unit}s";
        var parts = raw.Split('.');
        var integer = Parse(parts[0]);
        var result = Integer(integer) + " " + unit + (parts.Length > 1 || integer != 1 ? "s" : "");
        if (parts.Length > 1)
        {
            var cents = int.Parse(parts[1].PadRight(2, '0')[..2], CultureInfo.InvariantCulture);
            if (cents > 0) result += " and " + Integer(cents) + (cents == 1 ? " cent" : " cents");
        }
        return result;
    }

    internal static string Version(string raw)
    {
        var prefix = raw.StartsWith('v') || raw.StartsWith('V') ? "v " : "";
        if (prefix.Length > 0) raw = raw[1..];
        var trailing = raw.EndsWith('.') ? "." : "";
        return prefix + string.Join(" point ", raw.TrimEnd('.').Split('.').Select(p => Integer(Parse(p)))) + trailing;
    }
}
