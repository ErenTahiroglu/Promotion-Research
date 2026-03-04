# PromoScanner

Türkiye'deki B2B promosyon ürün tedarikçilerinin e-ticaret sitelerinden otomatik ürün bilgisi toplayan, verileri analiz eden ve gelişmiş algoritmalarla **"Hangi ürün, hangi sitede en F/P (Fiyat/Performans)"** karşılaştırması yapan **.NET 8 + Playwright** tabanlı modüler web crawler & analiz aracıdır.

## 🚀 Öne Çıkan Özellikler

*   **Akıllı Ürün Eşleştirme (`SmartProductMatcher`)**: Farklı sitelerdeki aynı veya benzer ürünleri isim, kategori, materyal (ahşap, metal, plastik vb.) ve kapasite (ml, cm, mAh, gb) analizleriyle gruplar. Sınıflandırılamayan ürünleri otomatik olarak `diger` kategorisine atar.
*   **Gerçek F/P (Toplam Maliyet) Analizi**: Fiyat karşılaştırmalarını sadece birim fiyat üzerinden değil, `(Maksimum Birim Fiyat × Minimum Sipariş Adedi) - (Minimum Birim Fiyat × Minimum Sipariş Adedi)` algoritması ile **kullanıcının cebinde kalacak net tutara** göre yapar.
*   **KDV Normalizasyonu**: "Fiyat + KDV" satan siteler ile "KDV Dahil" satan siteleri elma-elma kıyaslayabilmek için dinamik KDV oranı (`KdvRate`) hesaplaması uygular.
*   **Teklif Alternatifleri Motoru**: Fiyatını gizleyen ve "Teklif İsteyin" diyen sitelerdeki ürünlerin, diğer sitelerde **direkt fiyatlı satılan alternatiflerini** otomatik olarak bularak listeler.
*   **Tamamen O(1) ve Thread-Safe Mimari**: Görev kuyrukları (`CrawlQueue`), seed urlleri ve sayfa önbellekleri O(1) karmaşıklığıyla çalışır. `ConcurrentBag` kullanarak paralel taramaya (`MaxConcurrency`) tam uyumludur.
*   **Çift Yönlü Loglama**: Süreç boyunca konsola basılan tüm metrik ve uyarıları anlık olarak `TeeTextWriter` üzerinden lokal `run.log` dosyasına yedekler.
*   **Zengin Excel Çıktısı (ClosedXML)**: Elde edilen verileri *Özet, Tüm Ürünler, Karşılaştırma, En İyi Fırsatlar, Teklif Gereken* ve *Teklif Alternatifleri* olmak üzere 6 sekmeli, koşullu biçimlendirmeli (Örn: %50 üzeri fiyat farklarında sarı uyarı) zengin bir Excel dosyasına aktarır.

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

1.  **Depoyu Klonlayın ve Derleyin:**
    ```bash
    git clone <PROJE_URL>
    cd PromoScanner
    dotnet build
    ```

2.  **Playwright Tarayıcılarını Kurun (İlk Sefer İçin):**
    ```bash
    pwsh bin/Debug/net8.0/playwright.ps1 install chromium
    ```

3.  **Tarama Hedeflerini (Seed URLs) Belirleyin:**
    `src/PromoScanner.Cli/urls.txt` dosyası içerisine, taranmasını istediğiniz tedarikçi kategori veya ürün sayfalarını alt alta ekleyin.

4.  **Uygulamayı Çalıştırın:**
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
