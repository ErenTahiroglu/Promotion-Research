# PromoScanner - Requirements & Setup

## 📋 Sistem Gereksinimleri

### .NET Development
- **.NET SDK 8.0.417** veya daha yeni patchlar (8.0.4xx serisi)
  - Kontrol: `dotnet --version`
  - İndir: https://dotnet.microsoft.com/download/dotnet/8.0
  - **global.json** tarafından `rollForward: latestPatch` ile yönetilmektedir

### NuGet Bağımlılıkları
Tüm bağımlılıklar `dotnet restore` ile otomatik indirilecektir:

| Paket | Sürüm | Kullanım |
|-------|-------|----------|
| `CsvHelper` | 33.1.0 | CSV rapor yazma |
| `ClosedXML` | 0.104.1 | Excel rapor yazma |
| `Microsoft.Playwright` | 1.58.0 | Web tarayıcı otomasyonu |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.2 | Logging interface |
| `Microsoft.Extensions.Options` | 10.0.2 | IOptions pattern |
| `Microsoft.Extensions.Hosting` | 10.0.2 | Hosted service |
| `Microsoft.Extensions.Configuration.Json` | 10.0.2 | appsettings.json okuma |
| `AngleSharp` | 1.4.0 | HTML DOM parsing |

### İşletim Sistemi
- **Windows 10/11** (test edilmiştir)
- **macOS** (Playwright destekler, test edilmemiştir)
- **Linux** (Playwright destekler, test edilmemiştir)

### Gerekli Yazılımlar
- Git (repo klonlamak için)
- PowerShell 7+ (Playwright setup script için)
- Terminal / Command Line

---

## 🚀 Kurulum Adımları

### 1. Depoyu Klonla
```bash
git clone https://github.com/yourusername/PromoScanner.git
cd "Promosyon Araştırması"
```

### 2. NuGet Paketlerini Geri Yükle
```bash
dotnet restore
```

### 3. Projeyi Derle
```bash
dotnet build
```

### 4. (Opsiyonel) Playwright Tarayıcı Kurulumu
**Sadece Live Mode kullanacaksanız:**

Windows (PowerShell):
```bash
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
```

macOS/Linux (Bash):
```bash
bash bin/Debug/net8.0/playwright.sh install chromium
```

### 5. Çalıştır

**Mock Mode (Tavsiye Edilir):**
```bash
dotnet run --project src/PromoScanner.Cli
```

**Live Mode (Dikkatli Kullanın):**
- `appsettings.json` içinde `"RunMode": "Live"` yapın
- `src/PromoScanner.Cli/urls.txt` içine seed URL'ler ekleyin
- Çalıştırın

---

## ⚙️ Konfigürasyon (appsettings.json)

### Mock Mode vs Live Mode

```json
{
  "RunMode": "Mock",  // "Mock" veya "Live"
  "MockHtmlFolder": "mock_html_samples",
  "Scan": {
    "Headless": true,           // Tarayıcı başlık barı gizli
    "MaxConcurrency": 3,        // Paralel sayfa sayısı
    "MaxNewPages": 1500,        // Phase 1'de max sayfa
    "MaxRefreshPages": 800,     // Phase 2'de max sayfa
    "KdvRate": 0.20             // %20 KDV varsayılanı
  }
}
```

### Matching Thresholds
Ürünlerin benzerlik algoritmasında kullanılan eşik değerleri:
- `"kalem": 0.30` - Kalem ürünleri için %30 benzerlik eşiği
- `"defter": 0.22` - Defter ürünleri için %22 benzerlik eşiği
- `"diger": 0.40` - Sınıflandırılamayan ürünler için %40 eşiği (katı)

---

## 📂 Çıktı Dosyaları

Çalıştırma sonrası aşağıdaki dosyalar `output/YYYYMMDD_HHMMSS/` klasöründe oluşturulur:

| Dosya | Açıklama |
|-------|----------|
| `PromoScanner_Rapor.xlsx` | Ana Excel raporu (6 sekme) |
| `results.csv` | Ham ürün verileri |
| `smart_comparison.csv` | Akıllı karşılaştırma sonuçları |
| `best_deals.csv` | En iyi F/P fırsatları |
| `run.log` | Konsol çıktısının arşivi |

---

## 🧪 Testler

Birim testler ve entegrasyon testleri:
```bash
dotnet test tests/PromoScanner.Tests/PromoScanner.Tests.csproj
```

---

## 🔧 Sorun Giderme

### Playwright Kurulumu Başarısız
```bash
# Çevresel değişkenleri kontrol et
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD
$env:PLAYWRIGHT_DOWNLOAD_HOST

# Otomatik indir
dotnet tool install -g Microsoft.Playwright.CLI
playwright install chromium
```

### appsettings.json Okuma Hatası
- Dosya `src/PromoScanner.Cli/` klasöründe olmalı
- `.csproj` içinde `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` ayarları kontrol et

### Mock HTML Dosyaları Bulunamıyor
- `mock_html_samples/` dizini proje kökünde olmalı
- `appsettings.json`'da `"MockHtmlFolder": "mock_html_samples"` ayarlanmalı

---

## 📚 Referanslar

- [.NET 8 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8)
- [Playwright .NET Guide](https://playwright.dev/dotnet/)
- [global.json Configuration](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
- [Microsoft.Extensions.Configuration](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration)

---

**Hazırlanış Tarihi:** 2026-03-20
**PromoScanner Durumu:** ARCHIVED
