using System.Text.RegularExpressions;

namespace PromoScanner.Core;

/// <summary>
/// Benzerlik hesaplama, materyal çakışma, kümeleme ve grup oluşturma.
/// </summary>
public static partial class SmartProductMatcher
{
    // -- Materyal gruplari --
    private static readonly string[][] MaterialGroups =
    [
        ["metal", "celik", "paslanmaz", "aluminyum", "demir"],
        ["plastik", "pvc", "abs"],
        ["bambu", "ahsap", "agac"],
        ["deri", "pu deri", "suni deri"],
        ["kumas", "tekstil", "gabardin", "pamuk", "penye"],
        ["kaucuk", "silikon", "rubber"],
        ["cam", "kristal", "porselen", "seramik"],
        ["kraft", "karton"],
        ["mantar"],
    ];

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
        {"diger",0.40}, // Sınıflandırılamayan: daha sıkı eşik
    };

    // ===== ANA METOT =====
    public static List<SmartProductGroup> GroupSimilarProducts(
        List<ResultRow> products, decimal kdvRate = 0.20m)
    {
        var annotated = products
            .Where(p => p.Price.HasValue && p.Price > 0 && !string.IsNullOrWhiteSpace(p.ProductName))
            .Select(p => new AnnotatedProduct(p, kdvRate))
            .ToList();

        var groups = new List<SmartProductGroup>();

        foreach (var bucket in annotated.GroupBy(a => a.ProductType))
        {
            var items = bucket.ToList();

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
                clusters.Add([item]);
        }

        return clusters;
    }

    // -- Benzerlik [0-1] --
    private static double Similarity(AnnotatedProduct a, AnnotatedProduct b, string productType)
    {
        if (!SizesCompatible(a.SizeKey, b.SizeKey, a.IsDrinkSize || b.IsDrinkSize))
            return 0.0;

        decimal priceRatio = b.ComparablePrice > 0 && a.ComparablePrice > 0
            ? Math.Max(a.ComparablePrice, b.ComparablePrice) / Math.Min(a.ComparablePrice, b.ComparablePrice) : 1m;
        if (priceRatio > 5m) return 0.0;

        if (a.Tokens.Count == 0 || b.Tokens.Count == 0) return 0.0;

        int inter = a.Tokens.Intersect(b.Tokens).Count();
        int union = a.Tokens.Union(b.Tokens).Count();
        double jaccard = union > 0 ? (double)inter / union : 0.0;

        double penalty = 1.0;
        if (HasMaterialConflict(a.NormalizedName, b.NormalizedName))
            penalty = 0.40;
        else if (HasPremiumMismatch(a.NormalizedName, b.NormalizedName))
            penalty = 0.60;

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
            return true;
        if (sizeA == sizeB) return true;

        var numsA = ExtractNumbers(sizeA);
        var numsB = ExtractNumbers(sizeB);
        if (numsA.Length == 0 || numsB.Length == 0 || numsA.Length != numsB.Length)
            return false;

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
            .Select(m =>
            {
                var val = m.Value;
                if (val.Contains(','))
                    return double.Parse(
                        val.Replace(".", "").Replace(',', '.'),
                        System.Globalization.CultureInfo.InvariantCulture);

                if (Regex.IsMatch(val, @"\.\d{3}$"))
                    return double.Parse(
                        val.Replace(".", ""),
                        System.Globalization.CultureInfo.InvariantCulture);

                return double.Parse(val, System.Globalization.CultureInfo.InvariantCulture);
            })
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

        if (byStore.Count == 0) return null;

        var prices = byStore.Select(p => p.ComparablePrice).ToList();
        var minItem = byStore.OrderBy(p => p.ComparablePrice).First();
        var maxItem = byStore.OrderByDescending(p => p.ComparablePrice).First();

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

    // -- Ic veri yapisi --
    private sealed class AnnotatedProduct
    {
        public ResultRow Row { get; }
        public string NormalizedName { get; }
        public string ProductType { get; }
        public string? SizeKey { get; }
        public bool IsDrinkSize { get; }
        public HashSet<string> Tokens { get; }
        public List<string> FeatureTags { get; }
        public decimal Price { get; }
        /// <summary>KDV dahil normalize edilmiş fiyat (karşılaştırma için).</summary>
        public decimal ComparablePrice { get; }

        public AnnotatedProduct(ResultRow row, decimal kdvRate)
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
            // KDV normalizasyonu: +KDV ise KDV ekle, değilse olduğu gibi
            ComparablePrice = row.HasKDV ? Price * (1 + kdvRate) : Price;
        }
    }
}
