using System.Text;
using System.Text.RegularExpressions;

namespace PromoScanner.Core;

public static class SmartProductMatcher
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
    private static readonly Regex[] SeoPatterns =
    {
        new(@"\b\d{5}\s*-\s*", RegexOptions.Compiled),  // "32105 - " urun kodu
        new(@"ile\s+markan[ıi]z.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"markan[ıi]z[ıi]n?\s+rekl.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"i[cç]in\s+promosyon\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bpromosyon\s+(urun(ler)?i?(nde)?|arac[ıi])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\blog[oa]\s+bask[ıi]l[ıi]\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(etkili|pratik|kolay|hafif|kullanisli|sik|fonksiyonel)\s+(promosyon|urun)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // -- Urun tip taxonomy --
    private static readonly (string[] Keywords, string Type)[] Taxonomy =
    {
        // Hibrit
        (new[]{"powerbank defter","defter powerbank"}, "defter_powerbank"),
        (new[]{"powerbank organizer"}, "defter_powerbank"),

        // Kalem
        (new[]{"kalemtras","kalem tras"}, "kalemtras"),
        (new[]{"kalemlik"}, "kalemlik"),
        (new[]{"fosforlu kalem"}, "kalem_fosforlu"),
        (new[]{"kursun kalem"}, "kalem_kursun"),
        (new[]{"tukenmez kalem","tikenmez kalem"}, "kalem_tukenmez"),
        (new[]{"roller kalem","jel kalem","jell kalem"}, "kalem_tukenmez"),
        (new[]{"dolma kalem"}, "kalem_dolma"),
        (new[]{"dokunmatik kalem","touchpen","touch kalem"}, "kalem_dokunmatik"),
        (new[]{"tohumlu kalem"}, "kalem_tohumlu"),
        (new[]{"kalem kutu","kalem seti","kalem set"}, "kalem_set"),
        (new[]{"ikili kalem","ciftli kalem","uclu kalem"}, "kalem_set"),
        (new[]{"kursun"}, "kalem_kursun"),
        (new[]{"tukenmez","tikenmez"}, "kalem_tukenmez"),
        (new[]{"roller"}, "kalem_tukenmez"),
        (new[]{"fosforlu"}, "kalem_fosforlu"),
        (new[]{"kalem"}, "kalem"),

        // Defter / ajanda
        (new[]{"organizer ajanda"}, "defter_organizer"),
        (new[]{"organizer"}, "defter_organizer"),
        (new[]{"sekreterlik","sekreter bloknot"}, "defter_organizer"),
        (new[]{"haftalik ajanda","gunluk ajanda"}, "defter_ajanda"),
        (new[]{"ajanda"}, "defter_ajanda"),
        (new[]{"yapiskanli notluk","yapiskanli not"}, "notluk_yapiskanli"),
        (new[]{"not defteri","bloknot","notluk"}, "defter_not"),
        (new[]{"spiralli defter","spiralli"}, "defter"),
        (new[]{"defter"}, "defter"),

        // Canta
        (new[]{"laptop.*canta","bilgisayar.*canta"}, "canta_laptop"),
        (new[]{"sirt canta","sirt cantasi"}, "canta_sirt"),
        (new[]{"bel cantasi","freebag","bel canta"}, "canta_bel"),
        (new[]{"evrak canta"}, "canta_evrak"),
        (new[]{"ham bez","bez canta"}, "canta_bez"),
        (new[]{"imperteks canta"}, "canta_imperteks"),
        (new[]{"elyaf canta"}, "canta_elyaf"),
        (new[]{"tela canta"}, "canta_tela"),
        (new[]{"canta"}, "canta"),

        // Icecek
        (new[]{"french press"}, "termos_press"),
        (new[]{"termos bardak","termos kupa"}, "termos_bardak"),
        (new[]{"su sisesi","su matara"}, "su_sisesi"),
        (new[]{"cam matara"}, "matara_cam"),
        (new[]{"termos"}, "termos"),
        (new[]{"matara"}, "matara"),
        (new[]{"kupa bardak","kupa"}, "kupa"),
        (new[]{"bardak altligi","bardak altl"}, "bardak_altligi"),

        // Teknoloji
        (new[]{"powerbank","mobil sarj"}, "powerbank"),
        (new[]{"usb bellek","flash bellek"}, "usb_bellek"),
        (new[]{"mouse pad","mousepad"}, "mousepad"),
        (new[]{"telefon stand","tablet stand"}, "telefon_stand"),
        (new[]{"wireless sarj","kablosuz sarj","sarj istasyonu","sarj standi","hizli sarj"}, "sarj"),
        (new[]{"sarj kablo","sarj set","kablo set"}, "sarj_kablo"),
        (new[]{"bluetooth hoparl","hoparlor"}, "hoparlor"),

        // Giyim
        (new[]{"polo yaka"}, "tisort_polo"),
        (new[]{"bisiklet yaka"}, "tisort_bisiklet"),
        (new[]{"tisort","tshirt","t-shirt"}, "tisort"),
        (new[]{"sapka","bere","kep"}, "sapka"),

        // Ofis
        (new[]{"duvar saati"}, "saat_duvar"),
        (new[]{"masa saati"}, "saat_masa"),
        (new[]{"saat"}, "saat"),
        (new[]{"masa sumen","sumen"}, "masa_sumeni"),
        (new[]{"masa seti","kristal masa","masa takim"}, "masa_seti"),
        (new[]{"hesap makine"}, "hesap_makinesi"),
        (new[]{"gemici takvim","duvar takvim"}, "takvim"),

        // Kirtasiye
        (new[]{"sinav seti"}, "sinav_seti"),
        (new[]{"boyama seti","boyama"}, "boyama_seti"),
        (new[]{"silgi"}, "silgi"),
        (new[]{"cetvel"}, "cetvel"),
        (new[]{"makas"}, "makas"),

        // Aksesuar
        (new[]{"vip set kutusu"}, "vip_kutu"),
        (new[]{"vip set"}, "vip_set"),
        (new[]{"hediyelik set"}, "hediye_set"),
        (new[]{"anahtarlik"}, "anahtarlik"),
        (new[]{"rozet"}, "rozet"),
        (new[]{"kartvizitlik"}, "kartvizitlik"),
        (new[]{"magnet"}, "magnet"),
        (new[]{"semsiye"}, "semsiye"),
        (new[]{"cakmak"}, "cakmak"),
        (new[]{"caki"}, "caki"),
        (new[]{"el feneri","fener"}, "el_feneri"),

        // Diger
        (new[]{"piknik minder","etkinlik minder"}, "piknik_minderi"),
        (new[]{"atki","ataci"}, "atki"),
    };

    // -- Boyut/kapasite kaliplari --
    // ONEMLI: Turkce binlik ayirici (nokta) destegi
    private static readonly (Regex Pattern, string TypeHint, bool IsDrink)[] SizePatterns =
    {
        // Boyut: 14x21 cm
        (new Regex(@"(\d+[,.]?\d*)\s*x\s*(\d+[,.]?\d*)\s*(?:x\s*\d+[,.]?\d*\s*)?(?:cm)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "dim", false),
        // ml: 500 ml, 1.300 ml, 1000 ml
        // (?<!\d) = sayinin ortasindan yakalamay onler (1000 ml -> 000 ml bug fix)
        (new Regex(@"(?<!\d)(\d+(?:\.\d{3})*)\s*ml\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "ml", true),
        // lt
        (new Regex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(?:lt|litre)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "lt", true),
        // mAh: 10.000 mAh, 10000 mah
        (new Regex(@"(?<!\d)(\d+(?:\.\d{3})*)\s*mah\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "mah", false),
        // GB
        (new Regex(@"(?<!\d)(\d+)\s*gb\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "gb", false),
        // Gram
        (new Regex(@"(?<!\d)(\d+)\s*(?:gr|gram|gsm|g)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "gr", false),
        // Cap/diameter
        (new Regex(@"[o0]\s*(\d+(?:[.,]\d+)?)\s*(?:mm|cm)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "dia", false),
    };

    // -- Materyal gruplari --
    private static readonly string[][] MaterialGroups =
    {
        new[] { "metal", "celik", "paslanmaz", "aluminyum", "demir" },
        new[] { "plastik", "pvc", "abs" },
        new[] { "bambu", "ahsap", "agac" },
        new[] { "deri", "pu deri", "suni deri" },
        new[] { "kumas", "tekstil", "gabardin", "pamuk", "penye" },
        new[] { "kaucuk", "silikon", "rubber" },
        new[] { "cam", "kristal", "porselen", "seramik" },
        new[] { "kraft", "karton" },
        new[] { "mantar" },
    };

    private static readonly HashSet<string> PremiumMaterials = new(StringComparer.OrdinalIgnoreCase)
    {
        "bambu","ahsap","agac","deri","metal","celik","paslanmaz",
        "aluminyum","cam","kristal","porselen","seramik"
    };

    // -- Esikler --
    private static readonly Dictionary<string, double> Thresholds = new()
    {
        {"kalem",0.30},{"kalem_kursun",0.25},{"kalem_tukenmez",0.25},
        {"kalem_dolma",0.25},{"kalem_dokunmatik",0.25},{"kalem_fosforlu",0.25},
        {"kalem_tohumlu",0.25},{"kalem_set",0.28},{"kalemtras",0.35},{"kalemlik",0.28},
        {"defter",0.22},{"defter_ajanda",0.20},{"defter_not",0.22},
        {"defter_organizer",0.22},{"defter_powerbank",0.30},
        {"notluk_yapiskanli",0.22},
        {"canta",0.30},{"canta_sirt",0.25},{"canta_bez",0.25},{"canta_evrak",0.25},
        {"canta_bel",0.25},{"canta_laptop",0.25},{"canta_elyaf",0.25},{"canta_tela",0.25},
        {"termos",0.22},{"termos_bardak",0.22},{"termos_press",0.28},
        {"matara",0.22},{"matara_cam",0.22},{"kupa",0.22},
        {"su_sisesi",0.22},{"bardak_altligi",0.22},
        {"powerbank",0.22},{"usb_bellek",0.22},{"mousepad",0.22},
        {"telefon_stand",0.22},{"sarj",0.22},{"sarj_kablo",0.25},{"hoparlor",0.22},
        {"tisort",0.22},{"tisort_polo",0.20},{"tisort_bisiklet",0.20},{"sapka",0.22},
        {"saat_duvar",0.20},{"saat_masa",0.22},{"saat",0.22},
        {"masa_sumeni",0.22},{"masa_seti",0.25},{"hesap_makinesi",0.22},
        {"takvim",0.22},
        {"sinav_seti",0.20},{"boyama_seti",0.22},
        {"silgi",0.28},{"cetvel",0.28},{"makas",0.25},
        {"anahtarlik",0.25},{"rozet",0.22},{"semsiye",0.22},
        {"cakmak",0.22},{"caki",0.25},{"el_feneri",0.22},
        {"vip_set",0.28},{"vip_kutu",0.25},{"hediye_set",0.25},
        {"magnet",0.22},{"kartvizitlik",0.22},
        {"piknik_minderi",0.22},{"atki",0.22},
    };

    // ===== ANA METOT =====
    public static List<SmartProductGroup> GroupSimilarProducts(List<ResultRow> products)
    {
        var annotated = products
            .Where(p => p.Price.HasValue && p.Price > 0 && !string.IsNullOrWhiteSpace(p.ProductName))
            .Select(p => new AnnotatedProduct(p))
            .Where(a => a.ProductType != null)
            .ToList();

        var groups = new List<SmartProductGroup>();

        foreach (var bucket in annotated.GroupBy(a => a.ProductType!))
        {
            var items = bucket.ToList();
            if (items.Select(p => p.Row.Store).Distinct().Count() < 2)
                continue;

            var clusters = ClusterCompleteLinkage(items, bucket.Key);

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

    // -- Kumeleme --
    private static List<List<AnnotatedProduct>> ClusterCompleteLinkage(
        List<AnnotatedProduct> items, string productType)
    {
        double threshold = Thresholds.GetValueOrDefault(productType, 0.25);
        const int MAX_CLUSTER_SIZE = 80;
        var clusters = new List<List<AnnotatedProduct>>();

        foreach (var item in items)
        {
            List<AnnotatedProduct>? bestCluster = null;
            double bestMinScore = 0;

            foreach (var cluster in clusters)
            {
                if (cluster.Count >= MAX_CLUSTER_SIZE) continue;

                double minScore = cluster.Min(member => Similarity(item, member, productType));
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

    // -- Benzerlik [0-1] --
    private static double Similarity(AnnotatedProduct a, AnnotatedProduct b, string productType)
    {
        // Boyut uyumsuzlugu
        if (!SizesCompatible(a.SizeKey, b.SizeKey, a.IsDrinkSize || b.IsDrinkSize))
            return 0.0;

        // Fiyat orani > 5x
        decimal priceRatio = b.Price > 0 && a.Price > 0
            ? Math.Max(a.Price, b.Price) / Math.Min(a.Price, b.Price) : 1m;
        if (priceRatio > 5m) return 0.0;

        if (a.Tokens.Count == 0 || b.Tokens.Count == 0) return 0.0;

        // Jaccard
        int inter = a.Tokens.Intersect(b.Tokens).Count();
        int union = a.Tokens.Union(b.Tokens).Count();
        double jaccard = union > 0 ? (double)inter / union : 0.0;

        // Materyal cezasi
        double penalty = 1.0;
        if (HasMaterialConflict(a.NormalizedName, b.NormalizedName))
            penalty = 0.40;
        else if (HasPremiumMismatch(a.NormalizedName, b.NormalizedName))
            penalty = 0.60;

        // Ozellik bonus
        double bonus = 0.0;
        if (a.FeatureTags.Count > 0 && b.FeatureTags.Count > 0)
        {
            var common = a.FeatureTags.Intersect(b.FeatureTags, StringComparer.OrdinalIgnoreCase).Count();
            bonus = common * 0.03;
        }

        return Math.Min(1.0, jaccard * penalty + bonus);
    }

    // -- Boyut uyumlulugu --
    private static bool SizesCompatible(string? sizeA, string? sizeB, bool isDrink)
    {
        if (string.IsNullOrEmpty(sizeA) || string.IsNullOrEmpty(sizeB))
            return true; // biri bilinmiyorsa eslesebilir

        if (sizeA == sizeB) return true;

        var numsA = ExtractNumbers(sizeA);
        var numsB = ExtractNumbers(sizeB);

        if (numsA.Length == 0 || numsB.Length == 0 || numsA.Length != numsB.Length)
            return false;

        // Icecek kaplari icin daha siki tolerans: +-15%
        // Diger urunler: +-25%
        double maxRatio = isDrink ? 1.15 : 1.25;

        for (int i = 0; i < numsA.Length; i++)
        {
            double ratio = Math.Max(numsA[i], numsB[i]) / Math.Max(0.001, Math.Min(numsA[i], numsB[i]));
            if (ratio > maxRatio) return false;
        }
        return true;
    }

    private static double[] ExtractNumbers(string s)
        => Regex.Matches(s, @"\d+(?:[.,]\d+)?")
            .Select(m => double.Parse(m.Value.Replace(',', '.').Replace(".", ""),
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

    // -- Materyal celiskisi --
    private static bool HasMaterialConflict(string a, string b)
    {
        foreach (var group in MaterialGroups)
        {
            bool aIn = group.Any(m => a.Contains(m));
            if (!aIn) continue;
            foreach (var other in MaterialGroups)
            {
                if (ReferenceEquals(other, group)) continue;
                if (other.Any(m => b.Contains(m))) return true;
            }
        }
        return false;
    }

    private static bool HasPremiumMismatch(string a, string b)
    {
        bool aPrem = PremiumMaterials.Any(m => a.Contains(m));
        bool bPrem = PremiumMaterials.Any(m => b.Contains(m));
        bool aAny = MaterialGroups.Any(g => g.Any(m => a.Contains(m)));
        bool bAny = MaterialGroups.Any(g => g.Any(m => b.Contains(m)));
        return (aPrem && !bAny) || (bPrem && !aAny);
    }

    // -- Grup olustur --
    private static SmartProductGroup? BuildGroup(List<AnnotatedProduct> cluster, string productType)
    {
        if (cluster.Count == 0) return null;

        var byStore = cluster
            .GroupBy(p => p.Row.Store)
            .Select(g => g.OrderBy(p => p.Price).First())
            .ToList();

        if (byStore.Count < 2) return null;

        var prices = byStore.Select(p => p.Price).ToList();
        var minItem = byStore.OrderBy(p => p.Price).First();
        var maxItem = byStore.OrderByDescending(p => p.Price).First();

        string? commonSize = cluster
            .Select(p => p.SizeKey)
            .Where(s => !string.IsNullOrEmpty(s))
            .GroupBy(s => s!)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        var commonFeatures = cluster.Count > 1
            ? cluster.Skip(1).Aggregate(
                cluster[0].FeatureTags.AsEnumerable(),
                (acc, p) => acc.Intersect(p.FeatureTags, StringComparer.OrdinalIgnoreCase))
                .ToList()
            : cluster[0].FeatureTags.ToList();

        // Site bazli maliyet detayi
        var costParts = byStore.OrderBy(p => p.Price).Select(p =>
        {
            int qty = Math.Max(1, p.Row.MinOrderQty);
            decimal total = p.Price * qty;
            return $"{p.Row.Store}: {p.Price:N2} x {qty} = {total:N2} TL";
        }).ToList();

        int minQty = Math.Max(1, minItem.Row.MinOrderQty);
        int maxQty = Math.Max(1, maxItem.Row.MinOrderQty);

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
            MinPriceMinQty = minQty,
            MinPriceTotalCost = minItem.Price * minQty,
            MaxPrice = maxItem.Price,
            MaxPriceStore = maxItem.Row.Store,
            MaxPriceUrl = maxItem.Row.Url,
            MaxPriceMinQty = maxQty,
            MaxPriceTotalCost = maxItem.Price * maxQty,
            PriceDifference = maxItem.Price - minItem.Price,
            AvgPrice = Math.Round(prices.Average(), 2),
            MinOrderQty = byStore.Min(p => Math.Max(1, p.Row.MinOrderQty)),
            SiteCostBreakdown = string.Join(" | ", costParts),
            AllProductNames = string.Join(" | ", byStore.Select(p => p.Row.ProductName)),
            AllStores = string.Join(", ", byStore.Select(p => p.Row.Store)),
            AllUrls = string.Join(" | ", byStore.Select(p => p.Row.Url)),
        };
    }

    // -- Turkce normalize --
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                '\u00e7' or '\u00c7' => 'c', // cc
                '\u011f' or '\u011e' => 'g', // gg
                '\u0131' or '\u0130' => 'i', // ii
                '\u00f6' or '\u00d6' => 'o', // oo
                '\u015f' or '\u015e' => 's', // ss
                '\u00fc' or '\u00dc' => 'u', // uu
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
        foreach (var pat in SeoPatterns)
            norm = pat.Replace(norm, " ");
        return norm.Trim();
    }

    // -- Urun tipi --
    private static string? DetermineType(string norm)
    {
        foreach (var (keywords, type) in Taxonomy)
            foreach (var kw in keywords)
            {
                if (kw.Contains(".*"))
                {
                    if (Regex.IsMatch(norm, kw)) return type;
                }
                else if (norm.Contains(kw))
                    return type;
            }
        return null;
    }

    // -- Boyut anahtari --
    private static (string? key, bool isDrink) ExtractSizeKey(string norm)
    {
        foreach (var (pat, hint, isDrink) in SizePatterns)
        {
            var m = pat.Match(norm);
            if (m.Success)
            {
                // Turkce binlik: "10.000" -> "10000"
                string raw = m.Groups[1].Value.Replace(".", "");
                string unit = hint switch
                {
                    "ml" => "ml",
                    "lt" => "lt",
                    "mah" => "mah",
                    "gb" => "gb",
                    "gr" => "gr",
                    "dia" => "mm",
                    "dim" => "cm",
                    _ => ""
                };
                // Boyut: tum match'i dondur
                if (hint == "dim")
                    return (m.Value.Trim(), false);

                return ($"{raw} {unit}".Trim(), isDrink);
            }
        }
        return (null, false);
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

    // -- Ozellik etiketleri --
    private static readonly string[] FeatureKeywords =
    {
        "metal","plastik","bambu","deri","termo","ahsap","bez","kumas",
        "spiral","spiralli","tarihsiz","tohumlu","dokunmatik","touch",
        "naturel","kapakli","silgili","isikli","wireless","kablosuz",
        "geri donusum","donusumlu","lastikli","sert","yumusak",
        "kilitli","cep","cam","porselen","otomatik","fiber","rubber",
        "kraft","mantar","dijital","sublimasyon","polo","bisiklet",
        "polyester","pamuk","bambu","filtreli","pipetli",
    };

    private static List<string> ExtractFeatures(string norm)
        => FeatureKeywords.Where(f => norm.Contains(f)).ToList();

    // -- Goruntu kategorisi --
    private static string DisplayCategory(string t) => t switch
    {
        "kalem_kursun" => "kursun kalem",
        "kalem_tukenmez" => "tukenmez kalem",
        "kalem_dolma" => "dolma kalem",
        "kalem_dokunmatik" => "dokunmatik kalem",
        "kalem_fosforlu" => "fosforlu kalem",
        "kalem_tohumlu" => "tohumlu kalem",
        "kalem_set" => "kalem seti",
        "kalem" => "kalem",
        "kalemlik" => "kalemlik",
        "kalemtras" => "kalemtras",
        "defter_ajanda" => "ajanda",
        "defter_not" => "not defteri",
        "defter_organizer" => "organizer",
        "defter" => "defter",
        "defter_powerbank" => "powerbank defter",
        "notluk_yapiskanli" => "yapiskanli notluk",
        "canta_sirt" => "sirt cantasi",
        "canta_bez" => "bez canta",
        "canta_evrak" => "evrak cantasi",
        "canta_bel" => "bel cantasi",
        "canta_imperteks" => "imperteks canta",
        "canta_elyaf" => "elyaf canta",
        "canta_tela" => "tela canta",
        "canta_laptop" => "laptop cantasi",
        "canta" => "canta",
        "termos" => "termos",
        "termos_bardak" => "termos bardak",
        "termos_press" => "french press termos",
        "matara" => "matara",
        "matara_cam" => "cam matara",
        "kupa" => "kupa bardak",
        "su_sisesi" => "su sisesi",
        "bardak_altligi" => "bardak altligi",
        "powerbank" => "powerbank",
        "usb_bellek" => "USB bellek",
        "mousepad" => "mouse pad",
        "telefon_stand" => "telefon standi",
        "sarj" => "sarj cihazi",
        "sarj_kablo" => "sarj kablosu",
        "hoparlor" => "hoparlor",
        "tisort" => "tisort",
        "tisort_polo" => "polo yaka tisort",
        "tisort_bisiklet" => "bisiklet yaka tisort",
        "sapka" => "sapka",
        "saat_duvar" => "duvar saati",
        "saat_masa" => "masa saati",
        "saat" => "saat",
        "masa_sumeni" => "masa sumeni",
        "masa_seti" => "masa seti",
        "hesap_makinesi" => "hesap makinesi",
        "takvim" => "takvim",
        "sinav_seti" => "sinav seti",
        "boyama_seti" => "boyama seti",
        "silgi" => "silgi",
        "cetvel" => "cetvel",
        "makas" => "makas",
        "vip_set" => "VIP set",
        "vip_kutu" => "VIP kutusu",
        "hediye_set" => "hediyelik set",
        "anahtarlik" => "anahtarlik",
        "rozet" => "rozet",
        "kartvizitlik" => "kartvizitlik",
        "magnet" => "magnet",
        "semsiye" => "semsiye",
        "cakmak" => "cakmak",
        "caki" => "caki",
        "el_feneri" => "el feneri",
        "piknik_minderi" => "piknik minderi",
        "atki" => "atki",
        _ => t,
    };

    // -- Ic veri yapisi --
    private sealed class AnnotatedProduct
    {
        public ResultRow Row { get; }
        public string NormalizedName { get; }
        public string? ProductType { get; }
        public string? SizeKey { get; }
        public bool IsDrinkSize { get; }
        public HashSet<string> Tokens { get; }
        public List<string> FeatureTags { get; }
        public decimal Price { get; }

        public AnnotatedProduct(ResultRow row)
        {
            Row = row;
            var norm = Normalize(row.ProductName);
            NormalizedName = CleanSeo(norm);
            ProductType = DetermineType(NormalizedName);
            var (sizeKey, isDrink) = ExtractSizeKey(NormalizedName);
            SizeKey = sizeKey;
            IsDrinkSize = isDrink;
            Tokens = ExtractTokens(NormalizedName);
            FeatureTags = ExtractFeatures(NormalizedName);
            Price = row.Price ?? 0m;
        }
    }
}