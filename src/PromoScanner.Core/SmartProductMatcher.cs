using System.Text;
using System.Text.RegularExpressions;

namespace PromoScanner.Core;

/// <summary>
/// Farklı sitelerdeki benzer promosyon ürünlerini akıllıca eşleştiren algoritma.
///
/// Pipeline:
///   1. Türkçe normalize  (ş→s, ç→c, vs.)
///   2. Taxonomy ile ürün tipi belirleme
///   3. Boyut/kapasite çıkarma  (14x21 cm, 500ml, 10000mAh…)
///   4. Anlamlı token seti çıkarma  (stop word temizleme, boyutları dışla)
///   5. Complete-linkage kümeleme — Jaccard benzerliği + materyal çelişki cezası
///   6. Boyut uyumsuzluğu veya fiyat oranı > 20x → skor = 0
///   7. Her site'den en ucuz temsilci → SmartProductGroup
/// </summary>
public static class SmartProductMatcher
{
    // ── Stop word listesi ─────────────────────────────────────────────────────
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "promosyon","urun","urunleri","urunler","uretim","ozel","stoklu",
        "ekonomik","kalite","model","ve","ile","bir","bu","en","de","da",
        "icin","veya","ama","ile","her","den","dan","nin","nun","adet",
        "cm","mm","ml","gr","gsm","watt","mah","gb","tb","mb","lt",
        "li","lik","lu","luk","lu","lü","luk","nun","nin","den","dan",
        "olarak","olan","yeni","eski","tam","kisa","uzun","kucuk","buyuk",
        "orta","mini","maxi","super","ultra","pro","plus","max","lite",
        "standart","klasik","premium","deluxe","basic","the","and",
        "100","200","300","400","500","ile","isim","logo","baski",
        "imalat","imalati","toptan","ucuz","indirimli","fiyatli"
    };

    // ── Ürün tip taxonomy ─────────────────────────────────────────────────────
    // Uzun/spesifik kalıplar önce — kısalar sona
    private static readonly (string[] Keywords, string Type)[] Taxonomy =
    {
        // Kalem alt tipleri (önce)
        (new[]{"kursun kalem"},                     "kalem_kursun"),
        (new[]{"tukenmez kalem","tikenmez kalem"},   "kalem_tukenmez"),
        (new[]{"roller kalem","jell kalem"},         "kalem_tukenmez"),
        (new[]{"kalem kutu","kalem seti","kalem set"}, "kalem_set"),
        (new[]{"ikili kalem","ciftli kalem","cifli kalem"}, "kalem_set"),
        (new[]{"kursun"},                            "kalem_kursun"),
        (new[]{"tukenmez","tikenmez"},               "kalem_tukenmez"),
        (new[]{"roller","jell"},                     "kalem_tukenmez"),
        (new[]{"kalemlik"},                          "kalemlik"),
        (new[]{"kalem"},                             "kalem"),

        // Defter alt tipleri
        (new[]{"ajanda"},                            "defter_ajanda"),
        (new[]{"not defteri","bloknot","notluk"},    "defter_not"),
        (new[]{"spiralli defter","spiralli"},        "defter"),
        (new[]{"defter"},                            "defter"),

        // Çanta alt tipleri
        (new[]{"sirt canta","sirt cantasi"},         "canta_sirt"),
        (new[]{"ham bez","bez canta"},               "canta_bez"),
        (new[]{"evrak canta"},                       "canta_evrak"),
        (new[]{"bel cantasi"},                       "canta_bel"),
        (new[]{"canta"},                             "canta"),

        // İçecek kapları
        (new[]{"su sisesi"},                         "su_sisesi"),
        (new[]{"termos"},                            "termos"),
        (new[]{"matara"},                            "termos"),
        (new[]{"kupa bardak","kupa","bardak"},       "kupa"),

        // Teknoloji
        (new[]{"powerbank"},                         "powerbank"),
        (new[]{"usb bellek","flash bellek"},         "usb_bellek"),
        (new[]{"mouse pad","mousepad"},              "mousepad"),
        (new[]{"telefon stand","tablet stand"},      "telefon_stand"),
        (new[]{"sarj istasyonu","kablosuz sarj"},    "sarj"),

        // Giyim
        (new[]{"polo yaka","tisort","tshirt","t-shirt"}, "tisort"),
        (new[]{"sweatshirt","kazak"},                "sweatshirt"),
        (new[]{"sapka","bere","kep"},                "sapka"),

        // Ofis/masa
        (new[]{"duvar saati"},                       "saat_duvar"),
        (new[]{"masa saati"},                        "saat_masa"),
        (new[]{"saat"},                              "saat"),

        // Diğer belirli ürünler
        (new[]{"anahtarlik"},                        "anahtarlik"),
        (new[]{"rozet"},                             "rozet"),
        (new[]{"kartvizitlik"},                      "kartvizitlik"),
        (new[]{"mousepad"},                          "mousepad"),
        (new[]{"silgi"},                             "silgi"),
        (new[]{"cetvel"},                            "cetvel"),
        (new[]{"makas"},                             "makas"),
        (new[]{"kalemtraş"},                         "kalemtras"),
        (new[]{"ajanda"},                            "defter_ajanda"),
    };

    // ── Boyut/kapasite kalıpları ───────────────────────────────────────────────
    private static readonly (Regex Pattern, string TypeHint)[] SizePatterns =
    {
        (new Regex(@"(\d+[,.]?\d*)\s*x\s*(\d+[,.]?\d*)\s*(?:x\s*\d+[,.]?\d*\s*)?(?:cm\b)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "dim"),
        (new Regex(@"(\d+)\s*(?:ml|cl)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "ml"),
        (new Regex(@"(\d+(?:[.,]\d+)?)\s*(?:lt|litre|liter)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "lt"),
        (new Regex(@"(\d+(?:[.,]\d+)?)\s*(?:gr|gram|gsm|g)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "gr"),
        (new Regex(@"(\d+)\s*mah\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "mah"),
        (new Regex(@"(\d+)\s*gb\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "gb"),
        (new Regex(@"[ø∅]\s*(\d+(?:[.,]\d+)?)\s*(?:mm|cm)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "dia"),
        (new Regex(@"(\d+)\s*(?:cm)\s*(?:cap|ø)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "dia"),
    };

    // ── Materyal grupları (çelişki tespiti) ───────────────────────────────────
    private static readonly string[][] MaterialGroups =
    {
        new[] { "metal", "celik", "paslanmaz", "aluminyum", "demir" },
        new[] { "plastik", "pvc", "abs" },
        new[] { "bambu", "ahsap", "agac" },
        new[] { "deri", "pu deri", "suni deri" },
        new[] { "kumas", "tekstil", "gabardin", "pamuk" },
        new[] { "kauçuk", "silikon" },
    };

    // ── Ürün tipine göre Jaccard eşiği ────────────────────────────────────────
    private static readonly Dictionary<string, double> Thresholds = new()
    {
        { "kalem",          0.35 },
        { "kalem_kursun",   0.28 },
        { "kalem_tukenmez", 0.28 },
        { "kalem_set",      0.35 },
        { "defter",         0.28 },
        { "defter_ajanda",  0.25 },
        { "defter_not",     0.28 },
        { "canta",          0.40 },
        { "canta_bez",      0.32 },
        { "canta_sirt",     0.32 },
        { "termos",         0.30 },
        { "kupa",           0.30 },
        { "telefon_stand",  0.28 },
        { "saat",           0.28 },
        { "saat_duvar",     0.28 },
        { "tisort",         0.28 },
        { "anahtarlik",     0.35 },
        { "powerbank",      0.30 },
    };

    // ═══════════════════════════════════════════════════════════════════════════
    public static List<SmartProductGroup> GroupSimilarProducts(List<ResultRow> products)
    {
        // 1. Fiyatlı ürünleri annotate et
        var annotated = products
            .Where(p => p.Price.HasValue && p.Price > 0 && !string.IsNullOrWhiteSpace(p.ProductName))
            .Select(p => new AnnotatedProduct(p))
            .Where(a => a.ProductType != null)   // taxonomy'de eşleşmeyenleri atla
            .ToList();

        // 2. Ürün tipine göre bucket
        var groups = new List<SmartProductGroup>();

        foreach (var bucket in annotated.GroupBy(a => a.ProductType!))
        {
            var items = bucket.ToList();

            // 2+ farklı site yoksa karşılaştırma anlamsız
            if (items.Select(p => p.Row.Store).Distinct().Count() < 2)
                continue;

            // 3. Complete-linkage kümeleme
            var clusters = ClusterCompleteLinkage(items, bucket.Key);

            // 4. Her kümeyi gruba dönüştür
            foreach (var cluster in clusters)
            {
                var g = BuildGroup(cluster, bucket.Key);
                if (g != null) groups.Add(g);
            }
        }

        return groups
            .OrderByDescending(g => g.SiteCount)
            .ThenByDescending(g => g.PriceDifference ?? 0)
            .ToList();
    }

    // ── Complete-linkage kümeleme ─────────────────────────────────────────────
    private static List<List<AnnotatedProduct>> ClusterCompleteLinkage(
        List<AnnotatedProduct> items, string productType)
    {
        double threshold = Thresholds.GetValueOrDefault(productType, 0.30);
        var clusters = new List<List<AnnotatedProduct>>();

        foreach (var item in items)
        {
            // Bu item'in girebileceği en iyi cluster'ı bul
            // Complete-linkage: cluster'daki TÜM üyelerle skor >= threshold olmalı
            List<AnnotatedProduct>? bestCluster = null;
            double bestMinScore = 0;

            foreach (var cluster in clusters)
            {
                double minScore = cluster.Min(member => Similarity(item, member));
                if (minScore >= threshold && minScore > bestMinScore)
                {
                    bestMinScore = minScore;
                    bestCluster = cluster;
                }
            }

            if (bestCluster != null)
                bestCluster.Add(item);
            else
                clusters.Add(new List<AnnotatedProduct> { item });
        }

        return clusters;
    }

    // ── İki ürün arasındaki benzerlik skoru [0-1] ─────────────────────────────
    private static double Similarity(AnnotatedProduct a, AnnotatedProduct b)
    {
        // Boyut uyumsuzluğu → hemen 0
        if (!SizesCompatible(a.SizeKey, b.SizeKey))
            return 0.0;

        // Aşırı fiyat farkı → muhtemelen farklı kalite / farklı ürün
        decimal priceRatio = b.Price > 0 && a.Price > 0
            ? Math.Max(a.Price, b.Price) / Math.Min(a.Price, b.Price)
            : 1m;
        if (priceRatio > 20m)
            return 0.0;

        if (a.Tokens.Count == 0 || b.Tokens.Count == 0)
            return 0.0;

        // Jaccard
        int inter = a.Tokens.Intersect(b.Tokens).Count();
        int union = a.Tokens.Union(b.Tokens).Count();
        double jaccard = union > 0 ? (double)inter / union : 0.0;

        // Materyal çelişki cezası (metal vs plastik vs bambu)
        double penalty = HasMaterialConflict(a.NormalizedName, b.NormalizedName) ? 0.55 : 1.0;

        return jaccard * penalty;
    }

    // ── Boyut uyumluluğu (±25% tolerans) ─────────────────────────────────────
    private static bool SizesCompatible(string? sizeA, string? sizeB)
    {
        if (string.IsNullOrEmpty(sizeA) || string.IsNullOrEmpty(sizeB))
            return true; // Boyut yoksa uyumlu say

        if (sizeA == sizeB) return true;

        // Sayısal değerleri karşılaştır
        var numsA = Regex.Matches(sizeA, @"\d+(?:[.,]\d+)?")
                         .Select(m => double.Parse(m.Value.Replace(',', '.'),
                             System.Globalization.CultureInfo.InvariantCulture))
                         .ToArray();
        var numsB = Regex.Matches(sizeB, @"\d+(?:[.,]\d+)?")
                         .Select(m => double.Parse(m.Value.Replace(',', '.'),
                             System.Globalization.CultureInfo.InvariantCulture))
                         .ToArray();

        if (numsA.Length == 0 || numsB.Length == 0 || numsA.Length != numsB.Length)
            return false;

        for (int i = 0; i < numsA.Length; i++)
        {
            double ratio = Math.Max(numsA[i], numsB[i]) / Math.Max(0.001, Math.Min(numsA[i], numsB[i]));
            if (ratio > 1.25) return false;
        }
        return true;
    }

    // ── Materyal çelişkisi ────────────────────────────────────────────────────
    private static bool HasMaterialConflict(string normA, string normB)
    {
        foreach (var group in MaterialGroups)
        {
            bool aInGroup = group.Any(m => normA.Contains(m));
            if (!aInGroup) continue;
            foreach (var other in MaterialGroups)
            {
                if (ReferenceEquals(other, group)) continue;
                if (other.Any(m => normB.Contains(m))) return true;
            }
        }
        return false;
    }

    // ── Grup oluştur ──────────────────────────────────────────────────────────
    private static SmartProductGroup? BuildGroup(List<AnnotatedProduct> cluster, string productType)
    {
        if (cluster.Count == 0) return null;

        // Her site'den en ucuz ürünü seç
        var byStore = cluster
            .GroupBy(p => p.Row.Store)
            .Select(g => g.OrderBy(p => p.Price).First())
            .ToList();

        if (byStore.Count < 2) return null; // Tek siteyse grup oluşturma

        var prices = byStore.Select(p => p.Price).ToList();
        var minItem = byStore.OrderBy(p => p.Price).First();
        var maxItem = byStore.OrderByDescending(p => p.Price).First();

        // Ortak boyut (birden fazla ürün varsa en sık görüleni al)
        string? commonSize = cluster
            .Select(p => p.SizeKey)
            .Where(s => !string.IsNullOrEmpty(s))
            .GroupBy(s => s!)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        // Ortak özellik etiketleri
        var allFeatures = cluster.SelectMany(p => p.FeatureTags).Distinct().OrderBy(f => f);
        var commonFeatures = cluster.Count > 1
            ? cluster[0].FeatureTags
                .Intersect(cluster[1].FeatureTags, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : cluster[0].FeatureTags.ToList();
        // Eğer 3+ ürün varsa sadece hepsinde ortak olanları al
        for (int i = 2; i < cluster.Count; i++)
            commonFeatures = commonFeatures.Intersect(cluster[i].FeatureTags, StringComparer.OrdinalIgnoreCase).ToList();

        return new SmartProductGroup
        {
            Category = DisplayCategory(productType),
            Capacity = commonSize ?? "",
            KeyFeatures = string.Join(", ", commonFeatures.Take(5)),
            ProductCount = cluster.Count,
            SiteCount = byStore.Count,
            MinPrice = minItem.Price,
            MinPriceStore = minItem.Row.Store,
            MinPriceUrl = minItem.Row.Url,
            MaxPrice = maxItem.Price,
            MaxPriceStore = maxItem.Row.Store,
            PriceDifference = maxItem.Price - minItem.Price,
            AvgPrice = Math.Round(prices.Average(), 2),
            MinOrderQty = byStore.Min(p => p.Row.MinOrderQty),
            AllProductNames = string.Join(" | ", byStore.Select(p => p.Row.ProductName)),
            AllStores = string.Join(", ", byStore.Select(p => p.Row.Store)),
            AllUrls = string.Join(" | ", byStore.Select(p => p.Row.Url)),
        };
    }

    // ── Görüntü kategorisi ────────────────────────────────────────────────────
    private static string DisplayCategory(string productType) => productType switch
    {
        "kalem_kursun" => "kurşun kalem",
        "kalem_tukenmez" => "tükenmez kalem",
        "kalem_set" => "kalem seti",
        "kalem" => "kalem",
        "kalemlik" => "kalemlik",
        "defter_ajanda" => "ajanda",
        "defter_not" => "not defteri",
        "defter" => "defter",
        "canta_sirt" => "sırt çantası",
        "canta_bez" => "bez çanta",
        "canta_evrak" => "evrak çantası",
        "canta_bel" => "bel çantası",
        "canta" => "çanta",
        "termos" => "termos",
        "kupa" => "kupa bardak",
        "su_sisesi" => "su şişesi",
        "powerbank" => "powerbank",
        "usb_bellek" => "USB bellek",
        "mousepad" => "mouse pad",
        "telefon_stand" => "telefon standı",
        "sarj" => "şarj cihazı",
        "tisort" => "tişört",
        "sweatshirt" => "sweatshirt",
        "sapka" => "şapka",
        "anahtarlik" => "anahtarlık",
        "saat_duvar" => "duvar saati",
        "saat_masa" => "masa saati",
        "saat" => "saat",
        "rozet" => "rozet",
        "kartvizitlik" => "kartvizitlik",
        "silgi" => "silgi",
        "cetvel" => "cetvel",
        _ => productType,
    };

    // ── Türkçe normalize ──────────────────────────────────────────────────────
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' => 'u',
                'â' => 'a',
                'î' => 'i',
                'û' => 'u',
                _ => c,
            });
        }
        return sb.ToString();
    }

    // ── Ürün tipi ─────────────────────────────────────────────────────────────
    private static string? DetermineType(string norm)
    {
        foreach (var (keywords, type) in Taxonomy)
            foreach (var kw in keywords)
                if (norm.Contains(kw))
                    return type;
        return null;
    }

    // ── Boyut/kapasite anahtarı ───────────────────────────────────────────────
    private static string? ExtractSizeKey(string norm)
    {
        foreach (var (pat, _) in SizePatterns)
        {
            var m = pat.Match(norm);
            if (m.Success) return m.Value.Trim();
        }
        return null;
    }

    // ── Anlamlı token seti ────────────────────────────────────────────────────
    private static HashSet<string> ExtractTokens(string norm)
    {
        // Boyut/sayı bilgisini çıkar — token kümesini kirletmesin
        string cleaned = norm;
        foreach (var (pat, _) in SizePatterns)
            cleaned = pat.Replace(cleaned, " ");

        return Regex.Split(cleaned, @"[\s\-\/\(\)\[\]%,\.&+*]+")
            .Select(t => t.Trim())
            .Where(t => t.Length > 2 && !StopWords.Contains(t) && !Regex.IsMatch(t, @"^\d+$"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Özellik etiketleri ────────────────────────────────────────────────────
    private static readonly string[] FeatureKeywords =
    {
        "metal","plastik","bambu","deri","termo","ahsap","bez","kumas",
        "spiral","spiralli","tarihsiz","tohumlu","dokunmatik","touch",
        "naturel","kapakli","silgili","isikli","wireless","kablosuz",
        "geri donusum","donusumlu","jell","lastikli","sert","yumusak",
        "kilitli","ciftli","renkli","cep","boy",
    };

    private static List<string> ExtractFeatures(string norm)
        => FeatureKeywords.Where(f => norm.Contains(f)).ToList();

    // ── İç veri yapısı ───────────────────────────────────────────────────────
    private sealed class AnnotatedProduct
    {
        public ResultRow Row { get; }
        public string NormalizedName { get; }
        public string? ProductType { get; }
        public string? SizeKey { get; }
        public HashSet<string> Tokens { get; }
        public List<string> FeatureTags { get; }
        public decimal Price { get; }

        public AnnotatedProduct(ResultRow row)
        {
            Row = row;
            NormalizedName = Normalize(row.ProductName);
            ProductType = DetermineType(NormalizedName);
            SizeKey = ExtractSizeKey(NormalizedName);
            Tokens = ExtractTokens(NormalizedName);
            FeatureTags = ExtractFeatures(NormalizedName);
            Price = row.Price ?? 0m;
        }
    }
}