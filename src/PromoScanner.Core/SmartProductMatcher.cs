using System.Text.RegularExpressions;

namespace PromoScanner.Core;

public static class SmartProductMatcher
{
    // Türkçe karakter normalizasyonu
    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.ToLowerInvariant()
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u')
            .Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Replace('İ', 'i').Replace('Ğ', 'g').Replace('Ü', 'u')
            .Replace('Ş', 's').Replace('Ö', 'o').Replace('Ç', 'c');
    }

    public static ProductFeatures ExtractFeatures(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return new ProductFeatures();

        var lower = Normalize(productName);
        var features = new ProductFeatures { OriginalName = productName };

        // Kapasite / boyut
        var capacityMatch = Regex.Match(lower, @"(\d+)[.,]?(\d*)\s*(mah|gb|mb|ml|lt|kg|gr|cm|mm|li|lu)");
        if (capacityMatch.Success)
        {
            var number = capacityMatch.Groups[1].Value +
                         (capacityMatch.Groups[2].Success && capacityMatch.Groups[2].Value != "" ? capacityMatch.Groups[2].Value : "");
            features.Capacity = $"{number}{capacityMatch.Groups[3].Value}";
        }

        // Genis kategori eslestirme - her anahtar kelime grubu ayni kategoriye dusuyor
        var categories = new Dictionary<string, string[]>
        {
            ["powerbank"] = new[] { "powerbank", "sarj cihazi", "mobil sarj", "power bank" },
            ["usb"] = new[] { "usb bellek", "flash disk", "usb disk", "flash bellek" },
            ["kalem"] = new[] { "kalem", "tukenmez", "kursun kalem", "roller", "fosforlu", "markör", "marker", "pen" },
            ["kupa"] = new[] { "kupa", "bardak", "fincan", "mug", "termos bardak" },
            ["defter"] = new[] { "defter", "ajanda", "notebook", "bloknot", "not defteri", "tarihsiz" },
            ["canta"] = new[] { "canta", "torba", "poset", "sirt canta", "bez canta", "laptop canta", "evrak" },
            ["termos"] = new[] { "termos", "matara", "suluk", "su sisesi" },
            ["tisort"] = new[] { "tisort", "t-shirt", "polo", "gomlek", "sweatshirt" },
            ["sapka"] = new[] { "sapka", "bone", "bere", "kasket" },
            ["takvim"] = new[] { "takvim", "calendar", "masa takvim", "duvar takvim" },
            ["mousepad"] = new[] { "mouse pad", "mousepad", "fare alti", "mouse alti" },
            ["anahtarlik"] = new[] { "anahtarlik", "keychain", "key holder" },
            ["rozet"] = new[] { "rozet", "badge", "pin" },
            ["bardak"] = new[] { "bardak", "kupa", "mug", "fincan" },
            ["telefon"] = new[] { "telefon stand", "telefon tutucu", "selfie cubuk" },
            ["kalem_kutu"] = new[] { "kalemlik", "kalem kutu", "masaustu" },
        };

        foreach (var cat in categories)
        {
            if (cat.Value.Any(k => lower.Contains(k)))
            {
                features.Category = cat.Key;
                break;
            }
        }

        // Ozellikler
        var propertyKeywords = new Dictionary<string, string[]>
        {
            ["wireless"] = new[] { "wireless", "kablosuz", "wi-fi", "bluetooth" },
            ["magsafe"] = new[] { "magsafe", "magnetic", "manyetik" },
            ["led"] = new[] { "led", "isikli", "light", "aydinlatmali" },
            ["solar"] = new[] { "solar", "gunes enerjili" },
            ["dijital"] = new[] { "dijital", "digital", "lcd", "ekranli" },
            ["hizli_sarj"] = new[] { "hizli sarj", "fast charge", "quick charge", "pd" },
            ["ekolojik"] = new[] { "ekolojik", "eco", "bambu", "ahsap", "geri donusum", "tohumlu" },
            ["metal"] = new[] { "metal", "aluminyum", "celik", "paslanmaz" },
            ["plastik"] = new[] { "plastik", "abs", "pp" },
            ["spiral"] = new[] { "spiral", "ringli", "spiralli" },
            ["sertkapak"] = new[] { "sert kapak", "sertkapak", "hardcover", "karton kapak" },
        };

        foreach (var prop in propertyKeywords)
            if (prop.Value.Any(k => lower.Contains(k)))
                features.Properties.Add(prop.Key);

        // Marka
        var brands = new[] { "stanley", "eccotech", "samsung", "apple", "xiaomi", "anker", "moleskine", "pensan", "kores" };
        foreach (var brand in brands)
            if (lower.Contains(brand)) { features.Brand = brand; break; }

        // Renk
        var colors = new[] { "siyah", "beyaz", "kirmizi", "mavi", "yesil", "sari", "gri", "pembe", "turuncu", "lacivert", "mor" };
        foreach (var color in colors)
            if (lower.Contains(color)) { features.Color = color; break; }

        return features;
    }

    // Iki urun isminin kelime bazli benzerligi (Jaccard)
    private static double WordSimilarity(string a, string b)
    {
        var wordsA = Normalize(a).Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Where(w => w.Length > 2).ToHashSet();
        var wordsB = Normalize(b).Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Where(w => w.Length > 2).ToHashSet();

        if (wordsA.Count == 0 || wordsB.Count == 0) return 0;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();
        return (double)intersection / union;
    }

    public static bool AreSimilar(ProductFeatures f1, ProductFeatures f2, double threshold = 0.55)
    {
        // Kategori bos olan urunleri eslestirme
        if (string.IsNullOrEmpty(f1.Category) || string.IsNullOrEmpty(f2.Category))
            return false;

        // Farkli kategoriler kesinlikle eslesmiyor
        if (f1.Category != f2.Category)
            return false;

        double score = 0;
        int totalChecks = 0;

        // Kapasite eslesmesi - cok onemli (agirlik 3)
        if (!string.IsNullOrEmpty(f1.Capacity) && !string.IsNullOrEmpty(f2.Capacity))
        {
            totalChecks += 3;
            if (f1.Capacity == f2.Capacity) score += 3;
        }

        // Marka eslesmesi (agirlik 2)
        if (!string.IsNullOrEmpty(f1.Brand) || !string.IsNullOrEmpty(f2.Brand))
        {
            totalChecks += 2;
            if (f1.Brand == f2.Brand) score += 2;
        }

        // Ozellik eslesmesi (agirlik 1)
        if (f1.Properties.Count > 0 || f2.Properties.Count > 0)
        {
            var common = f1.Properties.Intersect(f2.Properties).Count();
            var total = f1.Properties.Union(f2.Properties).Count();
            if (total > 0)
            {
                totalChecks++;
                score += (double)common / total;
            }
        }

        // Kelime benzerligi - her zaman hesapla (agirlik 2)
        var wordSim = WordSimilarity(f1.OriginalName, f2.OriginalName);
        totalChecks += 2;
        score += wordSim * 2;

        if (totalChecks == 0) return false;

        double similarity = score / totalChecks;
        return similarity >= threshold;
    }

    public static List<SmartProductGroup> GroupSimilarProducts(List<ResultRow> products)
    {
        var groups = new List<SmartProductGroup>();
        var processed = new HashSet<string>();

        // Once ozellikleri cikar
        var featuresMap = products.ToDictionary(p => p.Url, p => ExtractFeatures(p.ProductName));

        foreach (var product in products)
        {
            if (processed.Contains(product.Url)) continue;

            var f1 = featuresMap[product.Url];

            // Kategorisi olmayan urunleri tek basina grupla
            if (string.IsNullOrEmpty(f1.Category))
            {
                processed.Add(product.Url);
                continue;
            }

            var group = new SmartProductGroup
            {
                Category = f1.Category,
                Capacity = f1.Capacity,
                KeyFeatures = string.Join(", ", f1.Properties),
                Products = new List<ResultRow> { product }
            };
            processed.Add(product.Url);

            foreach (var other in products)
            {
                if (processed.Contains(other.Url)) continue;
                // Ayni siteden urunleri gruplama (farkli site karsilastirmasi istiyoruz)
                // Ama ayni kategoriden ayni sitede de olabilir, o yuzden bu kismi kaldirdik
                var f2 = featuresMap[other.Url];
                if (AreSimilar(f1, f2))
                {
                    group.Products.Add(other);
                    processed.Add(other.Url);
                }
            }

            group.SiteCount = group.Products.Select(p => p.Store).Distinct().Count();
            group.ProductCount = group.Products.Count;

            var priced = group.Products.Where(p => p.Price.HasValue).ToList();
            if (priced.Count > 0)
            {
                var minP = priced.OrderBy(p => p.Price).First();
                var maxP = priced.OrderByDescending(p => p.Price).First();
                group.MinPrice = minP.Price;
                group.MinPriceStore = minP.Store;
                group.MinPriceUrl = minP.Url;
                group.MaxPrice = maxP.Price;
                group.MaxPriceStore = maxP.Store;
                group.AvgPrice = priced.Average(p => p.Price);
                group.PriceDifference = group.MaxPrice - group.MinPrice;
            }

            group.AllProductNames = string.Join(" | ", group.Products.Select(p => p.ProductName).Take(3));
            group.AllStores = string.Join(", ", group.Products.Select(p => p.Store).Distinct());
            groups.Add(group);
        }

        return groups
            .OrderByDescending(g => g.SiteCount)
            .ThenByDescending(g => g.PriceDifference ?? 0)
            .ThenBy(g => g.MinPrice ?? 999999)
            .ToList();
    }
}