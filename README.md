# PromoScanner

> ⚠️ **ARCHIVED** — Bu proje arşivlenmiş durumda olup, aktif bakım görmemektedir. Hedef sitelerin HTML DOM yapıları zamanla değişeceği için canlı (live) web kazıma işlemleri hata verebilir. Sistemin mimarisini incelemek ve test etmek için **Mock Mode** kullanmanız önerilir.

Türkiye'deki B2B promosyon ürün tedarikçilerinin e-ticaret sitelerinden otomatik ürün bilgisi toplayan, verileri analiz eden ve gelişmiş algoritmalarla **"Hangi ürün, hangi sitede en F/P (Fiyat/Performans)"** karşılaştırması yapan **.NET 8 + Playwright** tabanlı modüler web crawler & analiz aracıdır.

## 📌 Arşivlenmiş Proje Kullanma

### Live Mode (Canlı Kazıma - Tavsiye Edilmez)
Hedef sitelerin DOM yapıları değişmiş olabilir. Canlı kazıma işlemlerinde hatalar yaşayabilirsiniz.

### Mock Mode (Önerilir - Test & Mimarı İncelemek İçin)
Sistem mimarisini test etmek ve raporlama pipeline'ını görmek için:

1. **appsettings.json'da aşağıdakini ayarlayın:**
   ```json
   {
     "RunMode": "Mock",
     "MockHtmlFolder": "mock_html_samples"
   }
   ```

2. **Mock HTML örneklerinden veri çekileceği için hiçbir canlı HTTP isteği yapılmaz.**
3. **SmartProductMatcher ve raporlama (Excel/CSV) işlemleri tam işlev görecektir.**

## 🚀 Öne Çıkan Özellikler

*   **Akıllı Ürün Eşleştirme (`SmartProductMatcher`)**: Farklı sitelerdeki aynı veya benzer ürünleri isim, kategori, materyal (ahşap, metal, plastik vb.) ve kapasite (ml, cm, mAh, gb) analizleriyle gruplar. Sınıflandırılamayan ürünleri otomatik olarak `diger` kategorisine atar.
*   **Gerçek F/P (Toplam Maliyet) Analizi**: Fiyat karşılaştırmalarını sadece birim fiyat üzerinden değil, `(Maksimum Birim Fiyat × Minimum Sipariş Adedi) - (Minimum Birim Fiyat × Minimum Sipariş Adedi)` algoritması ile **kullanıcının cebinde kalacak net tutara** göre yapar.
*   **KDV Normalizasyonu**: "Fiyat + KDV" satan siteler ile "KDV Dahil" satan siteleri elma-elma kıyaslayabilmek için dinamik KDV oranı (`KdvRate`) hesaplaması uygular.
*   **Teklif Alternatifleri Motoru**: Fiyatını gizleyen ve "Teklif İsteyin" diyen sitelerdeki ürünlerin, diğer sitelerde **direkt fiyatlı satılan alternatiflerini** otomatik olarak bularak listeler.
*   **Tamamen O(1) ve Thread-Safe Mimari**: Görev kuyrukları (`CrawlQueue`), seed urlleri ve sayfa önbellekleri O(1) karmaşıklığıyla çalışır. `ConcurrentBag` kullanarak paralel taramaya (`MaxConcurrency`) tam uyumludur.
*   **Çift Yönlü Loglama**: Süreç boyunca konsola basılan tüm metrik ve uyarıları anlık olarak `TeeTextWriter` üzerinden lokal `run.log` dosyasına yedekler.
*   **Zengin Excel Çıktısı (ClosedXML)**: Elde edilen verileri *Özet, Tüm Ürünler, Karşılaştırma, En İyi Fırsatlar, Teklif Gereken* ve *Teklif Alternatifleri* olmak üzere 6 sekmeli, koşullu biçimlendirmeli (Örn: %50 üzeri fiyat farklarında sarı uyarı) zengin bir Excel dosyasına aktarır.

## 📊 Veri Akış Mimarisi (Data Flow)

```
┌──────────────────────────────────────────────────────────────┐
│ 1. CRAWLER ENGINE                                            │
│    • urls.txt veya Mock HTML'den seed URL'leri oku           │
│    • Sayfa kütüphanesi (cache) ile hızlı erişim             │
│    • Kara liste (blacklist) yönetimi                         │
└──────────────────┬───────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│ 2. SCRAPER REGISTRY + SITE-SPECIFIC SCRAPERS                │
│    • ISiteScraper arayüzü impl. (8 site için)               │
│    • JavaScript JS extraction + DOM parsing                  │
│    • Fiyat, min. sipariş, kategori çıkarma                  │
└──────────────────┬───────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│ 3. RESULT ROWS                                               │
│    • Store, URL, Ürün Adı, Fiyat, KDV, Min Qty              │
│    • Boş/Hatalı fiyatlar filtrelenmiş                        │
└──────────────────┬───────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│ 4. SMART PRODUCT MATCHER                                     │
│    • Ürün adı normalizasyonu (Türkçe char support)           │
│    • SEO temizleme                                           │
│    • Complete-linkage clustering (threshold: appsettings)    │
│    • Material uyumluluk ve premium kontrol                   │
│    • F/P (Fiyat/Performans) karşılaştırması                 │
└──────────────────┬───────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│ 5. REPORT ORCHESTRATOR (Excel + CSV)                         │
│    • Özet, Tüm Ürünler, Karşılaştırma, En İyi Fırsatlar     │
│    • Koşullu biçimlendirme (ClosedXML)                       │
│    • run.log: Konsol çıktısının arşivi (TeeTextWriter)       │
└──────────────────────────────────────────────────────────────┘
```

## 🏗️ Proje Mimarisi

Sistem, yapboz parçaları gibi birbirine "loosely coupled" (gevşek bağlı) modüllerden oluşacak biçimde tasarlanmıştır:

```text
PromoScanner.sln
├── src/
│   ├── PromoScanner.Core/          → Temel modeller (record), konfigürasyon, eşleştirme algoritmaları
│   ├── PromoScanner.Scraping/      → ISiteScraper arayüzü, 8 site-özel Playwright scraper sınıfı
│   ├── PromoScanner.Crawler/       → Crawling motoru, öncelikli kuyruk, cache ve blacklist yöneticileri
│   ├── PromoScanner.Reporting/     → Excel (ClosedXML) ve CSV koordinatörü, dedoop algoritmaları
│   └── PromoScanner.Cli/           → .NET Generic Host, Dependency Injection (DI) ayarlaması
└── tests/
    └── PromoScanner.Tests/         → Modül bazlı xUnit entegrasyon ve birim testleri
```

## 🌐 Desteklenen B2B Tedarikçileri

1.  **Promosyonik**
2.  **PromoZone**
3.  **Batı Promosyon**
4.  **Turkuaz Promosyon**
5.  **Aksiyon Promosyon**
6.  **İlpen**
7.  **Bidolu Baskı**
8.  **Tekinözalit**

(Yeni scraper'lar, `ISiteScraper` arayüzü uygulanarak ve `ScraperRegistry` sınıfına kaydedilerek anında sisteme entegre edilebilir.)

## ⚙️ Gereksinimler

*   [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
*   Playwright Tarayıcıları (Kurulum aşamasında çekilir)

## 🚀 Hızlı Başlangıç

### 1. Depoyu Klonlayın ve Derleyin
```bash
git clone <PROJE_URL>
cd "Promosyon Araştırması"
dotnet restore
dotnet build
```

### 2. Mock Mode'da Test Edin (Tavsiye Edilir)
Hiçbir bağımlılık veya canlı web erişimi olmadan sistemi test edin:

```bash
# appsettings.json'da şu ayarları doğrulayın:
# "RunMode": "Mock",
# "MockHtmlFolder": "mock_html_samples"

dotnet run --project src/PromoScanner.Cli
```

Çıktı: `output/` klasöründe Excel ve CSV dosyaları oluşturulacaktır.

### 3. Playwright Tarayıcılarını Kurun (Sadece Live Mode İçin)
```bash
# Opsiyonel: Canlı kazıma yapmak isterseniz
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
```

### 4. Canlı Kazıma (Live Mode) - Dikkatli Kullanın
```json
{
  "RunMode": "Live",
  "MockHtmlFolder": "mock_html_samples"
}
```

`src/PromoScanner.Cli/urls.txt` dosyasına seed URL'lerini ekleyin ve çalıştırın:
```bash
dotnet run --project src/PromoScanner.Cli
```

## 🛠️ Konfigürasyon (`appsettings.json`)

Tarama davranışı ve hassasiyeti `src/PromoScanner.Cli/appsettings.json` altındaki `Scan` düğümünden değiştirilebilir:

| Ayar Anahtarı | Tip | Varsayılan | Açıklama |
| :--- | :--- | :--- | :--- |
| `Headless` | bool | `true` | Tarayıcı arayüzü gizli çalışır (sunucu mod). |
| `MaxConcurrency` | int | `3` | Aynı anda açılacak sayfa sayısı. |
| `KdvRate` | decimal | `0.20` | Fiyat normalizasyonu için standart %20 KDV oranı. |
| `MaxNewPages` | int | `1500` | Phase 1'de keşfedilecek *hedef* maksimum sayfa limiti. |
| `MaxRefreshPages` | int | `800` | Phase 2'de güncellenecek önbelleklenmiş ürün limiti. |
| `BrowserTimeoutMs` | int | `90000` | Tıkanan sayfaları kapatmak için global zaman aşımı (ms). |
| `PageLoadWaitMs` | int | `500` | DOM'un parse edilmesi için statik bekleme süresi (ms). |

## 📊 Çıktılar

Varsayılan ayarlarda çıktı dosyaları `src/PromoScanner.Cli/out/` dizininde oluşur:
*   `PromoScanner_Rapor.xlsx` (Ana Rapor Düzeni)
*   `results.csv` (Ham Veriler)
*   `smart_comparison.csv`, `best_deals.csv` vb. (Ara Veriler)
*   `run.log` (Terminalde akan logların birebir dosyaya kaydedilmiş versiyonu)
