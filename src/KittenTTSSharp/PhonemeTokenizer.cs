using System.Globalization;
using System.Text;

namespace KittenTTSSharp;

internal static class PhonemeTokenizer
{
    // Keep duplicates and their final indices: this is the model's trained vocabulary.
    private const string Symbols = "$;:,.!?¡¿—…\"«»\"\" " +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
        "ɑɐɒæɓʙβɔɕçɗɖðʤəɘɚɛɜɝɞɟʄɡɠɢʛɦɧħɥʜɨɪʝɭɬɫɮʟɱɯɰŋɳɲɴøɵɸθœɶʘɹɺɾɻʀʁɽʂʃʈʧʉʊʋⱱʌɣɤʍχʎʏʑʐʒʔʡʕʢǀǁǂǃˈˌːˑʼʴʰʱʲʷˠˤ˞↓↑→↗↘'̩'ᵻ";
    private static readonly Dictionary<char, long> Vocabulary = Symbols
        .Select((symbol, index) => (symbol, index)).GroupBy(v => v.symbol)
        .ToDictionary(g => g.Key, g => (long)g.Last().index);

    public static long[] Encode(string phonemes)
    {
        // Python's Unicode \w includes letters/numbers/underscore but excludes combining marks.
        var tokens = new List<string>();
        var word = new StringBuilder();
        foreach (var rune in phonemes.EnumerateRunes())
        {
            var isWord = Rune.IsLetter(rune) || rune.Value == '_' || Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
            if (isWord) { word.Append(rune.ToString()); continue; }
            if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); }
            if (!Rune.IsWhiteSpace(rune)) tokens.Add(rune.ToString());
        }
        if (word.Length > 0) tokens.Add(word.ToString());
        var spaced = string.Join(' ', tokens);
        var result = new List<long>(spaced.Length + 3) { 0 };
        foreach (var symbol in spaced)
            if (Vocabulary.TryGetValue(symbol, out var id)) result.Add(id);
        result.Add(10);
        result.Add(0);
        return result.ToArray();
    }
}
