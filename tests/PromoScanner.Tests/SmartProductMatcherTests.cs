using PromoScanner.Core;
using Xunit;

namespace PromoScanner.Tests;

public class SmartProductMatcherTests
{
    // ── Normalize ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Şişe", "sise")]
    [InlineData("Çanta", "canta")]
    [InlineData("Güneş Gözlüğü", "gunes gozlugu")]
    [InlineData("KALEM", "kalem")]
    [InlineData("", "")]
    public void Normalize_ConvertsTurkishChars(string input, string expected)
    {
        Assert.Equal(expected, SmartProductMatcher.Normalize(input));
    }

    // ── GroupSimilarProducts ─────────────────────────────────────────────────
    [Fact]
    public void GroupSimilarProducts_GroupsSameTypeFromDifferentStores()
    {
        var products = new List<ResultRow>
        {
            new() { Store = "site1.com", ProductName = "Metal Tükenmez Kalem", Price = 15m, Currency = "TRY" },
            new() { Store = "site2.com", ProductName = "Metal Tükenmez Kalem Premium", Price = 18m, Currency = "TRY" },
        };

        var groups = SmartProductMatcher.GroupSimilarProducts(products);

        Assert.NotEmpty(groups);
        Assert.True(groups[0].SiteCount >= 2);
    }

    [Fact]
    public void GroupSimilarProducts_DoesNotGroupDifferentTypes()
    {
        var products = new List<ResultRow>
        {
            new() { Store = "site1.com", ProductName = "Termos Bardak 500ml", Price = 100m, Currency = "TRY" },
            new() { Store = "site2.com", ProductName = "Sırt Çantası Polyester", Price = 200m, Currency = "TRY" },
        };

        var groups = SmartProductMatcher.GroupSimilarProducts(products);

        // Farklı tipler gruplara ayrılmalı, aynı grupta olmamalı
        foreach (var g in groups)
            Assert.True(g.SiteCount < 2 || g.Category != "termos bardak");
    }

    [Fact]
    public void GroupSimilarProducts_HandlesEmptyList()
    {
        var groups = SmartProductMatcher.GroupSimilarProducts([]);
        Assert.Empty(groups);
    }

    [Fact]
    public void GroupSimilarProducts_FiltersNoPriceProducts()
    {
        var products = new List<ResultRow>
        {
            new() { Store = "site1.com", ProductName = "Kalem", Price = null, Currency = "TRY" },
            new() { Store = "site2.com", ProductName = "Kalem", Price = 0m, Currency = "TRY" },
        };

        var groups = SmartProductMatcher.GroupSimilarProducts(products);
        Assert.Empty(groups);
    }
}
