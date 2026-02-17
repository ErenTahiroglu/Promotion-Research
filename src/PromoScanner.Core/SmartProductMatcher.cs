using System.Text.RegularExpressions;

namespace PromoScanner.Core;

public static class SmartProductMatcher
{
    public static ProductFeatures ExtractFeatures(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return new ProductFeatures();

        var lower = productName.ToLowerInvariant();
        var features = new ProductFeatures { OriginalName = productName };

        var capacityMatch = Regex.Match(lower, @"(\d+)[.,]?(\d*)\s*(mah|gb|mb|ml|lt|kg|gr)");
        if (capacityMatch.Success)
        {
            var number = capacityMatch.Groups[1].Value +
                         (capacityMatch.Groups[2].Success ? capacityMatch.Groups[2].Value : "");
            features.Capacity = $"{number}{capacityMatch.Groups[3].Value}";
        }

        var categories = new Dictionary<string, string[]>
        {
            ["powerbank"] = new[] { "powerbank", "sarj cihazi", "mobil sarj" },
            ["usb"] = new[] { "usb bellek", "flash disk", "usb disk" },
            ["kalem"] = new[] { "kalem", "tukenmez", "kursun kalem", "roller" },
            ["kupa"] = new[] { "kupa", "bardak", "fincan", "mug" },
            ["defter"] = new[] { "defter", "ajanda", "notebook" },
            ["canta"] = new[] { "canta", "torba", "poset", "sirt canta" },
            ["termos"] = new[] { "termos", "matara", "suluk" },
            ["tisort"] = new[] { "sapka", "bone", "bere", "tisort" },
            ["takvim"] = new[] { "takvim", "calendar" },
            ["mousepad"] = new[] { "mouse pad", "mousepad", "fare alti" }
        };

        foreach (var cat in categories)
        {
            if (cat.Value.Any(k => lower.Contains(k)))
            {
                features.Category = cat.Key;
                break;
            }
        }

        var propertyKeywords = new Dictionary<string, string[]>
        {
            ["wireless"] = new[] { "wireless", "kablosuz", "wi-fi" },
            ["magsafe"] = new[] { "magsafe", "magnetic" },
            ["led"] = new[] { "led", "isikli", "light" },
            ["solar"] = new[] { "solar", "gunes enerjili" },
            ["dijital"] = new[] { "dijital", "digital", "lcd" },
            ["hizli_sarj"] = new[] { "hizli sarj", "fast charge", "quick charge", "pd" },
            ["dahili_kablo"] = new[] { "dahili kablo", "built-in cable" },
            ["ekolojik"] = new[] { "ekolojik", "eco", "bambu", "ahsap" }
        };

        foreach (var prop in propertyKeywords)
            if (prop.Value.Any(k => lower.Contains(k)))
                features.Properties.Add(prop.Key);

        foreach (var brand in new[] { "stanley", "eccotech", "samsung", "apple", "xiaomi", "anker" })
            if (lower.Contains(brand)) { features.Brand = brand; break; }

        foreach (var color in new[] { "siyah", "beyaz", "kirmizi", "mavi", "yesil", "sari", "gri", "pembe", "turuncu" })
            if (lower.Contains(color)) { features.Color = color; break; }

        return features;
    }

    public static bool AreSimilar(ProductFeatures f1, ProductFeatures f2, double threshold = 0.6)
    {
        if (f1.Category != f2.Category) return false;

        double score = 0;
        int totalChecks = 0;

        if (!string.IsNullOrEmpty(f1.Capacity) && !string.IsNullOrEmpty(f2.Capacity))
        {
            totalChecks += 2;
            if (f1.Capacity == f2.Capacity) score += 2;
        }

        if (!string.IsNullOrEmpty(f1.Brand) || !string.IsNullOrEmpty(f2.Brand))
        {
            totalChecks++;
            if (f1.Brand == f2.Brand) score++;
        }

        if (f1.Properties.Any() || f2.Properties.Any())
        {
            var common = f1.Properties.Intersect(f2.Properties).Count();
            var total = f1.Properties.Union(f2.Properties).Count();
            if (total > 0) { totalChecks++; score += (double)common / total; }
        }

        if (!string.IsNullOrEmpty(f1.Color) && !string.IsNullOrEmpty(f2.Color))
        {
            totalChecks++;
            if (f1.Color == f2.Color) score += 0.5;
        }

        return totalChecks > 0 && (score / totalChecks) >= threshold;
    }

    public static List<SmartProductGroup> GroupSimilarProducts(List<ResultRow> products)
    {
        var groups = new List<SmartProductGroup>();
        var processed = new HashSet<string>();

        foreach (var product in products)
        {
            if (processed.Contains(product.Url)) continue;

            var f1 = ExtractFeatures(product.ProductName);
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
                var f2 = ExtractFeatures(other.ProductName);
                if (AreSimilar(f1, f2))
                {
                    group.Products.Add(other);
                    processed.Add(other.Url);
                }
            }

            group.SiteCount = group.Products.Select(p => p.Store).Distinct().Count();
            group.ProductCount = group.Products.Count;

            var priced = group.Products.Where(p => p.Price.HasValue).ToList();
            if (priced.Any())
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