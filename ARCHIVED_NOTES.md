# PromoScanner - Archived Project Notes

## Arşivleme Tarihi
**2026-03-20**

## Neden Arşivlenmiştir?

Bu proje, Türkiye'deki B2B e-commerce sitelerinin web kazıma hedef alan bir araçtır. Hedef sitelerin:
- HTML DOM yapıları zamanla değişir
- CSS selector'ları güncellenir
- JavaScript injection yöntemleri farklılaşır
- Robots.txt veya anti-scraping önlemleri artabilir

Bu nedenler nedeniyle, **projenin gelecekte canlı (live) çalışması garantili değildir**.

## Arşiv Sonrası İyileştirmeler

### ✅ Yapılan Değişiklikler

1. **global.json - SDK Sabitleme**
   - `rollForward: "latestPatch"` ayarı eklendi
   - .NET 8.0.417 sürümü kesin olarak sabitlendi
   - Gelecekte birisi bu projeyi klonladığında, desteklenen SDK sürümüyle çalışacak

2. **NuGet Paket Sabitleme**
   - Tüm .csproj dosyalarında NuGet paketleri tam sürüm numaralarıyla belirtildi
   - Wildcard kullanım (`*`) tamamen ortadan kaldırıldı
   - Paketler: CsvHelper 33.1.0, ClosedXML 0.104.1, Microsoft.Playwright 1.58.0, vb.

3. **Mock Mode (Çevrimdışı Test)**
   - `mock_html_samples/` dizini oluşturuldu
   - AksiyonPromosyon ve BatiPromosyon örnek HTML dosyaları eklendi
   - `appsettings.json` : `"RunMode": "Mock"` seçeneği eklendi
   - Mock mode'da canlı HTTP isteği olmaz, statik HTML dosyalarından veri çekilir

4. **Konfigürasyon Dosyaları**
   - Tüm `MatchingThresholds` (SmartProductMatcher) appsettings.json'a taşındı
   - `KdvRate` ve `RunMode` konfigürasyonu merkezileştirildi

5. **urls.txt Temizleme**
   - Geniş ve eski URL listesi temizlendi
   - Yalnızca referans URL'leri (2-3 tane) bırakıldı
   - Açıklayıcı yorumlar eklendi

6. **README.md Dokümantasyonu**
   - Arşiv uyarısı eklendi
   - Mock Mode kullanım talimatları
   - Veri akış diyagramı (Data Flow) eklendi
   - Hızlı başlangıç güncellendi

## Gelecek Bakım İçin Notlar

### Canlı Kazımayı Tekrar Aktif Etmek İstiyorsanız

1. **DOM Selector'larını Kontrol Edin**
   - Her Scraper sınıfında (AksiyonPromosyonScraper, BatiPromosyonScraper, vb.)
   - CSS selector'ları hedef sitelerdeki güncel yapıyla karşılaştırın
   - Browser DevTools ile `.product-item`, `.price.net` gibi selector'ları doğrulayın

2. **Hata Yönetimini Güçlendir**
   - Scraper'larda `catch (Exception ex)` bloklarında ILogger entegre edin
   - Şu an satır 33 (AksiyonPromosyonScraper) ve satır 109'da sessiz catch var:
     ```csharp
     try { await page.WaitForSelectorAsync("..."); } catch { }
     ```
   - Bunu şu şekilde iyileştirebilirsiniz:
     ```csharp
     try { await page.WaitForSelectorAsync("..."); }
     catch (Exception ex)
     {
         logger.LogWarning(ex, "Element wait timeout: {Selector}", selector);
     }
     ```

3. **SmartProductMatcher Thresholds**
   - `appsettings.json` içinde `MatchingThresholds` sözlüğü var
   - Gelecekte bu değerleri IOptions<MatchingThresholdsOptions> ile enjekte edebilirsiniz
   - Şu an static Dictionary kullanılıyor, parametrik hale getirilebilir

4. **Playwright Tarayıcısı Kurulumu**
   - `dotnet restore` otomatik olarak Playwright'ı indirir
   - Browser'ı elle kurmak için: `pwsh bin/Debug/net8.0/playwright.ps1 install chromium`

### Test Senaryoları

Mock Mode'da çalıştığını doğrulamak için:
```bash
cd src/PromoScanner.Cli
dotnet run
# Çıktısı output/YYYYMMDD_HHMMSS/PromoScanner_Rapor.xlsx içinde olacak
```

## Dosya Yapısı Özet

```
Promosyon Araştırması/
├── global.json                    # .NET SDK sabitleme (rollForward: latestPatch)
├── Directory.Build.props          # Ortak proje ayarları
├── README.md                      # Güncellendi
├── ARCHIVED_NOTES.md             # Bu dosya
│
├── mock_html_samples/             # ✨ YENİ
│   ├── aksiyonpromosyon_listing.html
│   └── batipromosyon_listing.html
│
├── src/
│   ├── PromoScanner.Cli/
│   │   ├── Program.cs
│   │   ├── appsettings.json      # RunMode, MockHtmlFolder, MatchingThresholds
│   │   └── urls.txt              # Temizlendi (referans URL'leri sadece)
│   │
│   ├── PromoScanner.Core/
│   │   ├── SmartProductMatcher.cs
│   │   ├── SmartProductMatcher.Grouper.cs (hardcoded thresholds hala vardır)
│   │   └── ...
│   │
│   ├── PromoScanner.Scraping/
│   │   ├── ISiteScraper.cs
│   │   ├── ScraperRegistry.cs
│   │   └── Scrapers/
│   │       ├── AksiyonPromosyonScraper.cs  (catch ❌ henüz logger yok)
│   │       ├── BatiPromosyonScraper.cs
│   │       ├── BidoluBaskiScraper.cs
│   │       ├── IlpenScraper.cs
│   │       ├── PromosyonikScraper.cs
│   │       ├── PromoZoneScraper.cs
│   │       ├── TekinozalitScraper.cs
│   │       └── TurkuazPromosyonScraper.cs
│   │
│   ├── PromoScanner.Crawler/
│   │   ├── CrawlerEngine.cs
│   │   ├── CrawlQueue.cs
│   │   ├── PageCacheManager.cs
│   │   └── BlacklistManager.cs
│   │
│   └── PromoScanner.Reporting/
│       ├── ReportOrchestrator.cs
│       └── Excel & CSV writers
│
└── tests/
    └── PromoScanner.Tests/
```

## Kod Kalitesi Kontrol Listesi

- [x] .NET SDK sürümü kilitli (global.json)
- [x] NuGet paketleri tam sürüm belirtimli
- [x] Mock HTML örnekleri eklendi
- [x] appsettings.json merkezileştirildi
- [x] URLs temizlendi
- [x] README güncellendi
- [ ] ⚠️ Scraper'lardaki catch bloklarına ILogger eklenmedi (gelecek İyileştirme)
- [ ] ⚠️ SmartProductMatcher thresholdları dynamic config'ten okumacak hale getirilmedi (gelecek İyileştirme)

## Referanslar

- [.NET 8 Lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [global.json - Roll Forward Policy](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
- [Playwright .NET Docs](https://playwright.dev/dotnet/)
- [ClosedXML Documentation](https://closedxml.readthedocs.io/)

---

**Son Düzenleme:** 2026-03-20
**Durum:** ARCHIVED
