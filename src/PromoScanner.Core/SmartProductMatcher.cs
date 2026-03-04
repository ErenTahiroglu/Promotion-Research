using System.Text;
using System.Text.RegularExpressions;

namespace PromoScanner.Core;

/// <summary>
/// Ürün adı normalizasyonu ve SEO temizleme.
/// </summary>
public static partial class SmartProductMatcher
{
    // -- Stop words --
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "promosyon","urun","urunleri","urunler","uretim","ozel","stoklu",
        "ekonomik","kalite","model","ve","ile","bir","bu","en","de","da",
        "icin","veya","ama","her","den","dan","nin","nun","adet",
        "cm","mm","ml","gr","gsm","watt","mah","gb","tb","mb","lt",
        "li","lik","lu","luk","luk","nun","nin","den","dan",
        "olarak","olan","yeni","eski","tam","kisa","uzun","kucuk","buyuk",
        "orta","mini","maxi","super","ultra","pro","plus","max","lite",
        "standart","klasik","premium","deluxe","basic","the","and",
        "100","200","300","400","500","ile","isim","logo","baski",
        "imalat","imalati","toptan","ucuz","indirimli","fiyatli",
        "baskili","logolu","markaniz","kurumsal","firmalar","firmaniz",
        "ideal","cesitleri","cesitli","farkli","renk","renkli","secenekli",
        // SEO spam kelimeler
        "reklamini","yaparken","satis","stratejinize","katki","saglar",
        "markanizi","tanitir","hatirlatir","vurgular","sunar","tanitim",
        "gorsellik","sikligi","etkili","arac","kampanya","tercih",
        "ettigi","edilen","ettiginiz","kurumunuzun","kurumunuz",
        "olusturulan","olusturulmus","kolay","tasinabilir","estetik",
        "pratik","hafif","kullanisli","sik","fonksiyonel","guclu",
        "konforlu","tasima","cozumleri","cozumu","gunluk","kullanim",
        "uygun","yuksek","kaliteli","dayanikli","modern","zarif",
        "orijinal","cesit","alternatif","her","yerde","yudumda",
        "guvenligi","bolmeli","bolmesi","baskisi","egitim",
    };

    // -- SEO cumlelerini temizleme --
    [GeneratedRegex(@"\b\d{5}\s*-\s*")]
    private static partial Regex SeoPattern1();

    [GeneratedRegex(@"ile\s+markan[ıi]z.*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeoPattern2();

    [GeneratedRegex(@"markan[ıi]z[ıi]n?\s+rekl.*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeoPattern3();

    [GeneratedRegex(@"i[cç]in\s+promosyon\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeoPattern4();

    [GeneratedRegex(@"\bpromosyon\s+(urun(ler)?i?(nde)?|arac[ıi])\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeoPattern5();

    [GeneratedRegex(@"\blog[oa]\s+bask[ıi]l[ıi]\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeoPattern6();

    [GeneratedRegex(@"\b(etkili|pratik|kolay|hafif|kullanisli|sik|fonksiyonel)\s+(promosyon|urun)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeoPattern7();

    private static readonly Func<Regex>[] SeoPatterns =
    [
        SeoPattern1, SeoPattern2, SeoPattern3, SeoPattern4,
        SeoPattern5, SeoPattern6, SeoPattern7,
    ];

    // -- Turkce normalize --
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                '\u00e7' or '\u00c7' => 'c',
                '\u011f' or '\u011e' => 'g',
                '\u0131' or '\u0130' => 'i',
                '\u00f6' or '\u00d6' => 'o',
                '\u015f' or '\u015e' => 's',
                '\u00fc' or '\u00dc' => 'u',
                '\u00e2' => 'a',
                '\u00ee' => 'i',
                '\u00fb' => 'u',
                _ => c,
            });
        }
        return sb.ToString();
    }

    // -- SEO temizleme --
    private static string CleanSeo(string norm)
    {
        foreach (var patFactory in SeoPatterns)
            norm = patFactory().Replace(norm, " ");
        return norm.Trim();
    }

    // -- Token cikarma --
    private static HashSet<string> ExtractTokens(string norm)
    {
        string cleaned = norm;
        foreach (var (pat, _, _) in SizePatterns)
            cleaned = pat.Replace(cleaned, " ");

        return Regex.Split(cleaned, @"[\s\-\/\(\)\[\]%,\.&+*]+")
            .Select(t => t.Trim())
            .Where(t => t.Length > 2 && !StopWords.Contains(t) && !Regex.IsMatch(t, @"^\d+$"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}