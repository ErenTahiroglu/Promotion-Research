# Promotion-Research (PromoScanner)

Promosyon sitelerinden ürün bilgisi çekmek için .NET + Playwright tabanlı tarayıcı.

## Ne yapar?
- `urls.txt` içindeki sayfaları gezer
- Ürün adı / kategori / fiyat / para birimi / (varsa) adet bazlı fiyat listesi gibi alanları toplar
- Çıktıları CSV’ye yazar
- Hangi URL gezildi / atlandı / hata aldı bilgisini loglar

## Gereksinimler
- .NET SDK (global.json ile sabitlenir)
- Playwright tarayıcıları (ilk kurulumda otomatik/manuel indirilebilir)
- Windows / Linux / macOS (Codespaces dahil)

## Hızlı Başlangıç (Windows)
1) URL listeni oluştur:
- `src/PromoScanner.Cli/urls.txt` dosyasına her satıra bir URL yaz.
