using PromoScanner.Scraping;
using Xunit;

namespace PromoScanner.Tests;

public class ScraperHelpersTests
{
    // ── NormalizeUrl ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("https://example.com/page#hash", "https://example.com/page")]
    [InlineData("https://example.com/page/", "https://example.com/page")]
    [InlineData("  https://example.com/page  ", "https://example.com/page")]
    [InlineData("https://a.com/", "https://a.com")]  // Trailing slash her zaman silinir
    public void NormalizeUrl_RemovesHashAndTrailingSlash(string input, string expected)
    {
        Assert.Equal(expected, ScraperHelpers.NormalizeUrl(input));
    }

    // ── ParsePrice ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData("199,99 TL", 199.99, "TRY", true)]
    [InlineData("1.299,50 TL", 1299.50, "TRY", true)]
    [InlineData("49.99 TL", 49.99, "TRY", true)]
    [InlineData("15 TL", 15, "TRY", true)]
    [InlineData("₺ 99,00", 99.00, "TRY", true)]
    public void ParsePrice_ExtractsCorrectValues(string text, double expectedPrice, string expectedCurrency, bool expectedValid)
    {
        var (price, currency, _, _, valid) = ScraperHelpers.ParsePrice(text);
        Assert.Equal(expectedValid, valid);
        Assert.Equal((decimal)expectedPrice, price);
        Assert.Equal(expectedCurrency, currency);
    }

    [Theory]
    [InlineData("Teklif alınız")]
    [InlineData("Fiyat icin iletisim")]
    [InlineData("Bizi arayin")]
    public void ParsePrice_DetectsQuoteRequired(string text)
    {
        var (_, _, requiresQuote, _, _) = ScraperHelpers.ParsePrice(text);
        Assert.True(requiresQuote);
    }

    [Theory]
    [InlineData("199,99 + KDV", true)]
    [InlineData("199,99 +kdv TL", true)]
    [InlineData("199,99 TL", false)]
    public void ParsePrice_DetectsKDV(string text, bool expectedKdv)
    {
        var (_, _, _, hasKdv, _) = ScraperHelpers.ParsePrice(text);
        Assert.Equal(expectedKdv, hasKdv);
    }

    [Theory]
    [InlineData("0,05 TL", false)]   // 0.05 < 0.10 minimum eşik
    [InlineData("", false)]
    [InlineData("500 ml", false)]     // Ölçü birimi, fiyat değil
    public void ParsePrice_EdgeCases(string text, bool expectedValid)
    {
        var (_, _, _, _, valid) = ScraperHelpers.ParsePrice(text);
        Assert.Equal(expectedValid, valid);
    }

    // ── IsNavigationLink ─────────────────────────────────────────────────────
    [Theory]
    [InlineData("https://site.com/iletisim", "İletişim", true)]
    [InlineData("https://site.com/sepet", "Sepet", true)]
    [InlineData("https://site.com/urun/kalem-123", "Promosyon Kalem", false)]
    public void IsNavigationLink_DetectsCorrectly(string url, string text, bool expected)
    {
        Assert.Equal(expected, ScraperHelpers.IsNavigationLink(url, text));
    }

    // ── LooksLikeFileDownload ────────────────────────────────────────────────
    [Theory]
    [InlineData("https://site.com/file.pdf", true)]
    [InlineData("https://site.com/file.xlsx", true)]
    [InlineData("https://site.com/page", false)]
    public void LooksLikeFileDownload_DetectsCorrectly(string url, bool expected)
    {
        Assert.Equal(expected, ScraperHelpers.LooksLikeFileDownload(new Uri(url)));
    }

    // ── CleanProductName ─────────────────────────────────────────────────────
    [Fact]
    public void CleanProductName_RemovesPriceAndQuantity()
    {
        var result = ScraperHelpers.CleanProductName("Promosyon Kalem 100 adet 49,99 TL +KDV (5)");
        Assert.DoesNotContain("adet", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("49,99", result);
        Assert.DoesNotContain("KDV", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(5)", result);
    }
}
