using System.Text.RegularExpressions;

namespace PromoScanner.Core;

/// <summary>
/// Ürün tipi sınıflandırma (taxonomy), boyut çıkarma, özellik etiketleri.
/// </summary>
public static partial class SmartProductMatcher
{
    // -- Urun tip taxonomy --
    private static readonly (string[] Keywords, string Type)[] Taxonomy =
    [
        // Hibrit
        (["powerbank defter","defter powerbank"], "defter_powerbank"),
        (["powerbank organizer"], "defter_powerbank"),
        // Kalem
        (["kalemtras","kalem tras"], "kalemtras"),
        (["kalemlik"], "kalemlik"),
        (["fosforlu kalem"], "kalem_fosforlu"),
        (["kursun kalem"], "kalem_kursun"),
        (["tukenmez kalem","tikenmez kalem"], "kalem_tukenmez"),
        (["roller kalem","jel kalem","jell kalem"], "kalem_tukenmez"),
        (["dolma kalem"], "kalem_dolma"),
        (["dokunmatik kalem","touchpen","touch kalem"], "kalem_dokunmatik"),
        (["tohumlu kalem"], "kalem_tohumlu"),
        (["kalem kutu","kalem seti","kalem set"], "kalem_set"),
        (["ikili kalem","ciftli kalem","uclu kalem"], "kalem_set"),
        (["kursun"], "kalem_kursun"),
        (["tukenmez","tikenmez"], "kalem_tukenmez"),
        (["roller"], "kalem_tukenmez"),
        (["fosforlu"], "kalem_fosforlu"),
        (["kalem"], "kalem"),
        // Defter / ajanda
        (["organizer ajanda"], "defter_organizer"),
        (["organizer"], "defter_organizer"),
        (["sekreterlik","sekreter bloknot"], "defter_organizer"),
        (["haftalik ajanda","gunluk ajanda"], "defter_ajanda"),
        (["ajanda"], "defter_ajanda"),
        (["yapiskanli notluk","yapiskanli not"], "notluk_yapiskanli"),
        (["not defteri","bloknot","notluk"], "defter_not"),
        (["spiralli defter","spiralli"], "defter"),
        (["defter"], "defter"),
        // Canta
        (["laptop.*canta","bilgisayar.*canta"], "canta_laptop"),
        (["sirt canta","sirt cantasi"], "canta_sirt"),
        (["bel cantasi","freebag","bel canta"], "canta_bel"),
        (["evrak canta"], "canta_evrak"),
        (["ham bez","bez canta"], "canta_bez"),
        (["imperteks canta"], "canta_imperteks"),
        (["elyaf canta"], "canta_elyaf"),
        (["tela canta"], "canta_tela"),
        (["canta"], "canta"),
        // Icecek
        (["french press"], "termos_press"),
        (["termos bardak","termos kupa"], "termos_bardak"),
        (["su sisesi","su matara"], "su_sisesi"),
        (["cam matara"], "matara_cam"),
        (["termos"], "termos"),
        (["matara"], "matara"),
        (["kupa bardak","kupa"], "kupa"),
        (["bardak altligi","bardak altl"], "bardak_altligi"),
        // Teknoloji
        (["powerbank","mobil sarj"], "powerbank"),
        (["usb bellek","flash bellek"], "usb_bellek"),
        (["mouse pad","mousepad"], "mousepad"),
        (["telefon stand","tablet stand"], "telefon_stand"),
        (["wireless sarj","kablosuz sarj","sarj istasyonu","sarj standi","hizli sarj"], "sarj"),
        (["sarj kablo","sarj set","kablo set"], "sarj_kablo"),
        (["bluetooth hoparl","hoparlor"], "hoparlor"),
        // Giyim
        (["polo yaka"], "tisort_polo"),
        (["bisiklet yaka"], "tisort_bisiklet"),
        (["tisort","tshirt","t-shirt"], "tisort"),
        (["sapka","bere","kep"], "sapka"),
        // Ofis
        (["duvar saati"], "saat_duvar"),
        (["masa saati"], "saat_masa"),
        (["saat"], "saat"),
        (["masa sumen","sumen"], "masa_sumeni"),
        (["masa seti","kristal masa","masa takim"], "masa_seti"),
        (["hesap makine"], "hesap_makinesi"),
        (["gemici takvim","duvar takvim"], "takvim"),
        // Kirtasiye
        (["sinav seti"], "sinav_seti"),
        (["boyama seti","boyama"], "boyama_seti"),
        (["silgi"], "silgi"),
        (["cetvel"], "cetvel"),
        (["makas"], "makas"),
        // Aksesuar
        (["vip set kutusu"], "vip_kutu"),
        (["vip set"], "vip_set"),
        (["hediyelik set"], "hediye_set"),
        (["anahtarlik"], "anahtarlik"),
        (["rozet"], "rozet"),
        (["kartvizitlik"], "kartvizitlik"),
        (["magnet"], "magnet"),
        (["semsiye"], "semsiye"),
        (["cakmak"], "cakmak"),
        (["caki"], "caki"),
        (["el feneri","fener"], "el_feneri"),
        // Diger
        (["piknik minder","etkinlik minder"], "piknik_minderi"),
        (["atki","ataci"], "atki"),
    ];

    // -- Boyut/kapasite kaliplari --
    private static readonly (Regex Pattern, string TypeHint, bool IsDrink)[] SizePatterns =
    [
        (new Regex(@"(\d+[,.]?\d*)\s*x\s*(\d+[,.]?\d*)\s*(?:x\s*\d+[,.]?\d*\s*)?(?:cm)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "dim", false),
        (new Regex(@"(?<!\d)(\d+(?:\.\d{3})*)\s*ml\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "ml", true),
        (new Regex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(?:lt|litre)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "lt", true),
        (new Regex(@"(?<!\d)(\d+(?:\.\d{3})*)\s*mah\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "mah", false),
        (new Regex(@"(?<!\d)(\d+)\s*gb\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "gb", false),
        (new Regex(@"(?<!\d)(\d+)\s*(?:gr|gram|gsm|g)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "gr", false),
        (new Regex(@"[o0]\s*(\d+(?:[.,]\d+)?)\s*(?:mm|cm)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), "dia", false),
    ];

    // -- Ozellik etiketleri --
    private static readonly string[] FeatureKeywords =
    [
        "metal","plastik","bambu","deri","termo","ahsap","bez","kumas",
        "spiral","spiralli","tarihsiz","tohumlu","dokunmatik","touch",
        "naturel","kapakli","silgili","isikli","wireless","kablosuz",
        "geri donusum","donusumlu","lastikli","sert","yumusak",
        "kilitli","cep","cam","porselen","otomatik","fiber","rubber",
        "kraft","mantar","dijital","sublimasyon","polo","bisiklet",
        "polyester","pamuk","bambu","filtreli","pipetli",
    ];

    // -- Urun tipi tespit --
    private static string DetermineType(string norm)
    {
        foreach (var (keywords, type) in Taxonomy)
            foreach (var kw in keywords)
            {
                if (kw.Contains(".*"))
                {
                    if (Regex.IsMatch(norm, kw)) return type;
                }
                else if (norm.Contains(kw))
                    return type;
            }
        return "diger"; // Sınıflandırılamayan ürünler
    }

    // -- Boyut anahtari --
    private static (string? key, bool isDrink) ExtractSizeKey(string norm)
    {
        foreach (var (pat, hint, isDrink) in SizePatterns)
        {
            var m = pat.Match(norm);
            if (m.Success)
            {
                string raw = m.Groups[1].Value.Replace(".", "");
                string unit = hint switch
                {
                    "ml" => "ml", "lt" => "lt", "mah" => "mah",
                    "gb" => "gb", "gr" => "gr", "dia" => "mm", "dim" => "cm",
                    _ => ""
                };
                if (hint == "dim")
                    return (m.Value.Trim(), false);
                return ($"{raw} {unit}".Trim(), isDrink);
            }
        }
        return (null, false);
    }

    // -- Ozellik cikarma --
    private static List<string> ExtractFeatures(string norm)
        => FeatureKeywords.Where(f => norm.Contains(f)).ToList();

    // -- Goruntu kategorisi --
    private static string DisplayCategory(string t) => t switch
    {
        "kalem_kursun" => "kursun kalem", "kalem_tukenmez" => "tukenmez kalem",
        "kalem_dolma" => "dolma kalem", "kalem_dokunmatik" => "dokunmatik kalem",
        "kalem_fosforlu" => "fosforlu kalem", "kalem_tohumlu" => "tohumlu kalem",
        "kalem_set" => "kalem seti", "kalem" => "kalem",
        "kalemlik" => "kalemlik", "kalemtras" => "kalemtras",
        "defter_ajanda" => "ajanda", "defter_not" => "not defteri",
        "defter_organizer" => "organizer", "defter" => "defter",
        "defter_powerbank" => "powerbank defter", "notluk_yapiskanli" => "yapiskanli notluk",
        "canta_sirt" => "sirt cantasi", "canta_bez" => "bez canta",
        "canta_evrak" => "evrak cantasi", "canta_bel" => "bel cantasi",
        "canta_imperteks" => "imperteks canta", "canta_elyaf" => "elyaf canta",
        "canta_tela" => "tela canta", "canta_laptop" => "laptop cantasi",
        "canta" => "canta", "termos" => "termos", "termos_bardak" => "termos bardak",
        "termos_press" => "french press termos", "matara" => "matara",
        "matara_cam" => "cam matara", "kupa" => "kupa bardak",
        "su_sisesi" => "su sisesi", "bardak_altligi" => "bardak altligi",
        "powerbank" => "powerbank", "usb_bellek" => "USB bellek",
        "mousepad" => "mouse pad", "telefon_stand" => "telefon standi",
        "sarj" => "sarj cihazi", "sarj_kablo" => "sarj kablosu",
        "hoparlor" => "hoparlor", "tisort" => "tisort",
        "tisort_polo" => "polo yaka tisort", "tisort_bisiklet" => "bisiklet yaka tisort",
        "sapka" => "sapka", "saat_duvar" => "duvar saati",
        "saat_masa" => "masa saati", "saat" => "saat",
        "masa_sumeni" => "masa sumeni", "masa_seti" => "masa seti",
        "hesap_makinesi" => "hesap makinesi", "takvim" => "takvim",
        "sinav_seti" => "sinav seti", "boyama_seti" => "boyama seti",
        "silgi" => "silgi", "cetvel" => "cetvel", "makas" => "makas",
        "vip_set" => "VIP set", "vip_kutu" => "VIP kutusu",
        "hediye_set" => "hediyelik set", "anahtarlik" => "anahtarlik",
        "rozet" => "rozet", "kartvizitlik" => "kartvizitlik",
        "magnet" => "magnet", "semsiye" => "semsiye",
        "cakmak" => "cakmak", "caki" => "caki", "el_feneri" => "el feneri",
        "piknik_minderi" => "piknik minderi", "atki" => "atki",
        _ => t,
    };
}
