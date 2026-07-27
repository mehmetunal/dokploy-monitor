# Trimango Dokploy Monitör

Dokploy'daki **tum projelerin deployment sureclerini tek ekranda** izleyen ASP.NET Core MVC uygulamasi.
Kendisi de Dokploy'a bir servis olarak deploy edilir. **Birden fazla Dokploy sunucusu /
API anahtari** ayni panelden izlenebilir.

Cevapladigi sorular:

- Deploy basladi mi? Hala devam ediyor mu, ne kadar suredir?
- Hata mi verdi, **hangi** hata? (mesaj + tam build logu)
- Kuyrukta ne var, hangi is kacinci sirada?

---

## Mimari

```
src/DokployMonitor.Core             Varliklar, sozlesmeler, pano modelleri (bagimsiz)
src/DokployMonitor.Infrastructure   Dokploy REST istemcisi, EF Core (SQL Server), FluentMigrator semasi, log okuyucu
src/DokployMonitor.Web              MVC ekranlari, SignalR, arka plan servisleri, webhook ucu
tests/DokployMonitor.Tests          xUnit testleri
```

| Konu | Kullanilan | Nerede |
|---|---|---|
| Veritabani semasi | **FluentMigrator** (acilista `MigrateUp`) | `Infrastructure/Persistence/Migrations` |
| Sorgu / kayit | EF Core (SQL Server) | `Infrastructure/Persistence/MonitorDbContext.cs` |
| Yapilandirma ve istek dogrulama | **FluentValidation** (`ValidateOnStart` ile fail-fast) | `*Validator.cs`, `Infrastructure/Validation` |
| Giris ve roller | **ASP.NET Core Identity** (cookie) | `Infrastructure/Identity`, `Controllers/AccountController.cs` |
| Coklu Dokploy sunucusu | Baglanti basina istemci fabrikasi | `Infrastructure/Dokploy/DokployClientFactory.cs` |
| Container loglari | Docker Engine API (unix socket) | `Infrastructure/Docker` |
| Onbellek | `IDistributedCache` — **Memory ya da Redis** (configden) | `Infrastructure/Caching` |
| Arayuz dili | **Veritabani tabanli** `IStringLocalizer` (panelden duzenlenir) | `Infrastructure/Localization` |
| Arayuz temasi | Cerez tabanli, sunucu tarafinda uygulanir | `Services/UiPreferences.cs` |

### Veri nereden geliyor?

| Kanal | Ne icin | Not |
|---|---|---|
| `GET /api/deployment.allCentralized` | Tum organizasyonun deployment'lari **tek istekte** (proje/ortam/servis gomulu) | Birincil kaynak |
| `GET /api/deployment.queueList` | Gercek kuyruk: `waiting` / `active`, sira ve zaman damgalari | Kuyruk gorunumunun tek kaynagi |
| `POST /api/deployment.killProcess`, `application.redeploy`, `compose.redeploy` | Panelden aksiyon | POST'lar yeniden denenmez |
| Generic webhook (Dokploy → bize) | Build biter bitmez anlik bildirim | Payload'da `deploymentId` yok; `buildLink`'ten ID ayikilir |
| `/etc/dokploy/logs` (salt-okunur mount) | Build loglari (canli takip dahil) | WebSocket API anahtari kabul etmiyor, bu yuzden dosyadan okunuyor |

`deployment.allCentralized` bulunmayan eski Dokploy surumlerinde istemci otomatik olarak
`project.all` + servis basina `deployment.all` moduna duser (Tanilama ekraninda gorunur).

### Guncelleme dongusu

- **Uyarlanabilir polling**: bos zamanda 15 sn, aktif deployment varken 2 sn.
- **Kuyruk**: 5 sn'de bir; kuyrukta hareket olunca deployment senkronu da hemen tetiklenir.
- **Webhook**: geldigi anda senkron tetiklenir → sonuc saniyeler icinde ekranda.
- **SignalR** (`/hubs/deployments`): pano degisiklikleri ve canli log akisi. Baglanti kurulamazsa
  tarayici otomatik olarak `/dashboard/snapshot` polling'ine duser.

---

## Giris, roller ve kullanicilar

Panel **giris zorunlu**dur (ASP.NET Core Identity, cookie oturumu). Kendi kendine kayit
ekrani yoktur; hesaplari yalnizca `SuperAdmin` rolundeki bir kullanici olusturur.

**Ilk giris:** uygulama ilk acilista yonetici hesabini olusturur.

```
E-posta : admin@trimango.local
Parola  : Super123!
```

Bu hesapla girildiginde panel **hicbir sayfayi acmaz**; once
`/Account/ChangeCredentials` ekranindan gecmek **zorunludur**. Bu ekranda:

- yeni bir e-posta/parola belirleyebilirsiniz, **ya da**
- **ayni bilgileri tekrar yazip** onlarla devam edebilirsiniz (mevcut parolayi dogrulamaniz
  yeterlidir; onaydan sonra ekran bir daha cikmaz).

Varsayilan parola bu dokumanda yazili oldugu icin panel internete acikken degistirmeniz
onerilir. Parolayi bastan kendiniz belirlemek isterseniz `Auth__AdminPassword` verin —
o zaman onay adimi hic istenmez.

| Rol | Yetki |
|---|---|
| `SuperAdmin` | Her sey: kullanici yonetimi, Dokploy baglantilari, **Durdur / Yeniden Deploy / Replay** |
| `Viewer` | Salt okuma: panolar, gecmis, hata analizi, loglar. Aksiyon butonlari gorunmez |

Yetkisiz bir istek `/Account/AccessDenied` sayfasina duser. Anonim kalan tek uclar:
`/health`, `/Account/Login` ve webhook (`/api/webhooks/dokploy`, token ile korunur).

---

## Coklu Dokploy baglantisi

Her baglanti bir sunucu + bir API anahtaridir. **Baglantilar** ekrani (SuperAdmin) uzerinden
eklenir; senkronizasyon tum **etkin** baglantilari dolasir ve her deployment kaydini
geldigi baglantiyla etiketler.

- **`Dokploy__BaseUrl` / `Dokploy__ApiKey` zorunlu degildir.** Hic verilmezse uygulama normal
  acilir, log'a *"Tanimli Dokploy baglantisi yok"* uyarisi yazar ve baglantiyi Baglantilar
  ekranindan eklersiniz. Verilirse ilk acilista **"Varsayilan"** adiyla veritabanina aktarilir
  ve eski deployment kayitlari bu baglantiya baglanir (geriye uyumluluk).
  Yalnizca **birini** vermek hatadir: eksik olan icin acilista dogrulama hatasi alirsiniz.
- Anahtar degistiginde ortam degiskenini guncellemek gerekmez; Baglantilar ekranindan
  duzenlenir. Duzenleme formunda anahtar alani bos gelir ve kayitli anahtar
  yesil **kayitli** isaretiyle maskeli gosterilir; bos birakirsaniz mevcut anahtar korunur.
- Bir baglanti hata verirse digerleri calismaya devam eder; panoda
  *"1/2 baglanti okunamadi"* uyarisi, Baglantilar ve Tanilama ekranlarinda ise
  baglanti basina durum gorunur.
- Kuyruk her baglanti icin ayri okunur; sira numaralari kendi kuyruguna gore hesaplanir.
- Gecmis ekraninda **sunucu (baglanti)** filtresi, satirlarda baglanti etiketi cikar
  (birden fazla baglanti tanimliysa).
- Baglanti silinirse toplanan deployment gecmisi **korunur**.

> **Istek hacmi baglanti sayisiyla carpilir.** Iki baglanti = iki kat polling. API anahtari
> kotasi/rate limit ayarlarken bunu hesaba katin (bkz. asagidaki kota bolumu).

> API anahtarlari veritabaninda duz metin tutulur (kullanici parola hash'leriyle ayni
> veritabaninda). SQL Server erisimini ve yedeklerini korumali tutun; ekranlarda anahtar
> yalnizca maskeli gosterilir.

---

## Arayuz: tema ve dil

**Tema** — navbar'daki secici uc secenek sunar: **Sistem** (isletim sisteminin
`prefers-color-scheme` tercihi), **Koyu**, **Aydinlik**. Secim `dm.theme` cerezinde tutulur
ve **sunucu ilk render'da** `<html data-bs-theme>` degerini yazar; boylece sayfa acilirken
yanlis temayla "flash" olmaz. Sistem modunda tercih degisirse (or. gece moduna gecis) sayfa
yenilenmeden uyum saglar.

**Dil** — 17 dil desteklenir: **Türkçe (kaynak)**, English, Deutsch, Français, Español,
Português, Italiano, Nederlands, Polski, Русский, Українська, **العربية (RTL)**, 简体中文,
日本語, 한국어, हिन्दी, Bahasa Indonesia. Secim sirasi:

1. Kullanicinin acik secimi (`.AspNetCore.Culture` cerezi — navbar'daki dil secici)
2. **Sistem/tarayici dili** (`Accept-Language` basligi)
3. Varsayilan: Türkçe

### Ceviriler veritabaninda

**resx dosyasi yoktur.** Tum ceviriler `Translations` tablosunda tutulur ve
**SuperAdmin panelden duzenler**: `/Translations` ekraninda dil secilir, satirlar
duzenlenir, kaydedilince **aninda** uygulanir (yeniden derleme/deploy gerekmez).

- **Anahtar, kaynak dildeki (Turkce) metnin kendisidir**: `L["Canli Pano"]`. Ceviri bos
  ise ekranda kaynak metin gorunur — eksik ceviri sayfayi bozmaz.
- **Eksik anahtarlar otomatik toplanir**: bir metin ekranda ilk kez gorunduginde
  "cevrilmemis" olarak listeye eklenir; yonetici neyi cevirmesi gerektigini gorur.
- Kaynak (Turkce) metinler de ezilebilir: `tr` dilinde satira deger yazmak yeterli.
- Ilk kurulumda kutudan cikan ceviriler tohum olarak eklenir
  (`Infrastructure/Localization/TranslationDefaults.cs`); **var olan satirlar asla ezilmez**,
  panelden yapilan duzenlemeler korunur.
- `IStringLocalizer` senkron oldugu icin ceviriler bellekte anlik goruntu olarak tutulur;
  kaydetme aninda tazelenir, ayrica arka planda 30 saniyede bir yenilenir (coklu ornek
  kurulumunda diger ornekler bu surede yakalar).

Sagdan sola yazilan diller (su an Arapca) icin `<html dir="rtl">` otomatik ayarlanir.
Kod olarak **iki harfli** kullanilir; tarayici `zh-Hans` ya da `pt-BR` gonderse de
dogru satira duser.

**Tohum veriler**: kutudan cikan 16 dilin cevirileri
`Infrastructure/Localization/TranslationDefaults.cs` icindedir (dil × 86 anahtar) ve ilk
acilista veritabanina yazilir. Tohumlama davranisi:

| Durum | Sonuc |
|---|---|
| Satir yok | eklenir |
| Satir var, degeri **bos** | tohumla doldurulur |
| Satir var, degeri **dolu** | **dokunulmaz** (panelden yapilan duzenleme korunur) |

Yeni dil eklemek: `Options/LocalizationSetup.cs` icindeki `Supported` dizisine bir satir
ekleyin (`new("sv", "Svenska")`, RTL ise `RightToLeft: true`); cevirileri panelden girin ya
da tohum dosyasina koyun.

---

## Onbellek (Memory / Redis)

Kod her zaman `IDistributedCache` uzerinden calisir; sagalayici yalnizca yapilandirmadan
secilir. Redis **secili ve adres verilmisse** Redis, aksi halde bellek ici onbellek kullanilir.

```env
Cache__Provider=Memory            # veya Redis
Cache__RedisConnectionString=redis:6379
Cache__InstanceName=dokploy-monitor:
Cache__DefaultSeconds=30
```

- `Provider=Redis` verilip adres bos birakilirsa uygulama **acilista hata verir** (sessizce
  bellege dusmez) — yanlis yapilandirma gizlenmesin.
- Redis gecici olarak erisilemezse istek **hata almaz**: onbellek atlanir, deger veritabanindan
  uretilir ve log'a uyari yazilir. Onbellek bir hizlandirma katmanidir.
- Tanilama ekraninda hangi sagalayicinin kullanildigi ve yaz/oku denemesinin sonucu gorunur.
- Onbelleklenen veriler: proje adlari listesi ve baglanti adlari (her pano ciziminde
  tekrarlanan sorgular). Baglanti degistiginde ilgili anahtar dusurulur.

Birden fazla Monitor ornegi calistiracaksaniz (ya da yeniden baslatmada onbellek korunsun
istiyorsaniz) Redis'i secin; tek konteynerde `Memory` yeterlidir.

---

## Ekranlar

| Yol | Icerik |
|---|---|
| `/Account/Login` | Giris (anonim) |
| `/` | Canli pano: KPI'lar, aktif deploymentlar (canli sayac), kuyruk, son deploymentlar, webhook bildirimleri |
| `/Deployments` | Filtrelenebilir gecmis (proje / durum / metin arama) |
| `/Deployments/Details/{id}` | Canli build logu, **container logu (docker logs)**, hata mesaji, olay zaman cizelgesi, servisin ve projenin son deploylari, Durdur / Yeniden Deploy / **Replay** |
| `/Errors` | Hata analizi: proje / son N gun filtresi, gruplanmis hatalar, log onizleme |
| `/Dashboard/Diagnostics` | Baglanti basina yetenek testi, Docker soketi durumu, webhook URL'i |
| `/Connections` | Dokploy sunucu/API anahtari yonetimi (**SuperAdmin**) |
| `/Users` | Kullanici yonetimi (**SuperAdmin**) |
| `/Translations` | Arayuz cevirileri: duzenle, ekle, eksikleri gor (**SuperAdmin**) |
| `/health` | Saglik ucu (anonim) |

Her ekranin goruntusu asagida: **[Ekran goruntuleri](#ekran-goruntuleri)**.

---

## Ekran goruntuleri

Hepsi **calisan uygulamadan** alindi (Playwright, gelistirme ornegi, gercek Dokploy verisi):
masaustu **1440×900**, mobil **390×844** (2× yogunluk). Arayuz dili Turkce; ayni ekranlar
17 dilde calisir — [Ingilizce ornek](#ingilizce-arayuz).

Iki gorselde **ornek veri** var, cunku bu makinede gercek karsiligi olusmuyor:

- **Build logu**: Dokploy'un `/etc/dokploy/logs` klasoru yerelde mount olmadigi icin
  `src/DokployMonitor.Web/data/dev-logs` altina iki ornek build log dosyasi konuldu
  (silmek icin `rm -rf src/DokployMonitor.Web/data/dev-logs`). Dokploy uzerinde bu klasor
  mount edildiginde loglar gercek dosyadan gelir.
- **Hata imzasi**: Dokploy bu 7 basarisiz deployment icin `errorMessage` dondurmedigi icin
  imza uretilemiyor; gruplama ekrani ornek bir imza ile gosteriliyor.

### Masaustu (1440×900)

#### Giris

![Giris ekrani](docs/screenshots/desktop/01-login.png)

#### Zorunlu kimlik guncelleme (ilk giris)

Varsayilan `admin@trimango.local` / `Super123!` ile girildiginde bu ekran atlanamaz;
**ayni e-posta ve ayni parola tekrar yazilarak** devam edilebilir.

![Kimlik guncelleme](docs/screenshots/desktop/19-change-credentials.png)

#### Canli pano — ust (KPI'lar, aktif deploymentlar, kuyruk)

![Canli pano ustu](docs/screenshots/desktop/02-dashboard.png)

#### Canli pano — alt (son deploymentlar, webhook bildirimleri)

![Canli pano alti](docs/screenshots/desktop/02b-dashboard-recent.png)

#### Koyu tema

![Koyu tema](docs/screenshots/desktop/05-dashboard-dark.png)

#### Tema menusu (Sistem / Koyu / Acik)

![Tema menusu](docs/screenshots/desktop/03-theme-menu.png)

#### Dil menusu (bayrakli, 17 dil + sistem dili)

![Dil menusu](docs/screenshots/desktop/04-language-menu.png)

#### Ingilizce arayuz

Ayni pano `en` kulturunde — tum metinler veritabanindaki cevirilerden gelir.

![Ingilizce pano](docs/screenshots/desktop/18-dashboard-english.png)

#### Deployment gecmisi (filtre + sayfalama)

![Deployment gecmisi](docs/screenshots/desktop/06-deployments.png)

#### Gecmis — durum filtresi uygulanmis

![Filtreli gecmis](docs/screenshots/desktop/07-deployments-filtered.png)

#### Log onizleme (liste ekranlarindan, Container / Build secimli)

![Log onizleme](docs/screenshots/desktop/08-log-preview.png)

#### Deployment detayi (build logu, container logu, ozet, olay gecmisi, komsu deploylar)

![Deployment detayi](docs/screenshots/desktop/09-deployment-details.png)

#### Hata analizi (gruplanmis hatalar + son basarisizlar)

![Hata analizi](docs/screenshots/desktop/10-errors.png)

#### Hata imzasi detayi (ayni koku paylasan deploymentlar)

![Hata imzasi](docs/screenshots/desktop/11-error-signature.png)

#### Baglantilar (coklu Dokploy sunucusu / API anahtari — SuperAdmin)

Duzenleme modunda anahtar alani bos gelir; kayitli anahtar yesil **kayitli** isareti ve
maskeli hali ile gosterilir — bos birakilirsa mevcut anahtar korunur.

![Baglantilar](docs/screenshots/desktop/12-connections.png)

#### Kullanicilar (SuperAdmin)

![Kullanicilar](docs/screenshots/desktop/13-users.png)

#### Ceviriler (SuperAdmin)

![Ceviriler](docs/screenshots/desktop/14-translations.png)

#### Ceviriler — yalnizca eksikler

![Eksik ceviriler](docs/screenshots/desktop/15-translations-missing.png)

#### Tanilama (baglanti yetenekleri, Docker soketi, webhook URL'i)

![Tanilama](docs/screenshots/desktop/16-diagnostics.png)

#### Yetki reddi (SuperAdmin gerektiren ekranlar)

![Yetki reddi](docs/screenshots/desktop/17-access-denied.png)

### Mobil (390×844)

Ayni ekranlar; navigasyon hamburger menuye toplanir, tablolarda ikincil kolonlar
(`TIP`, `BASLANGIC`, `HATA` …) gizlenir, filtreler ve KPI kartlari tek kolona akar.
Gorsellere tiklayarak tam boyutta acabilirsiniz.

<table>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/01-login.png"><img src="docs/screenshots/mobile/01-login.png" width="250" alt="Giris"></a><br><sub><b>Giris</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/19-change-credentials.png"><img src="docs/screenshots/mobile/19-change-credentials.png" width="250" alt="Kimlik guncelleme"></a><br><sub><b>Zorunlu kimlik guncelleme</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/02-dashboard.png"><img src="docs/screenshots/mobile/02-dashboard.png" width="250" alt="Canli pano"></a><br><sub><b>Canli pano — ust</b></sub></td>
</tr>
<tr>
<td align="center"><a href="docs/screenshots/mobile/02b-dashboard-recent.png"><img src="docs/screenshots/mobile/02b-dashboard-recent.png" width="250" alt="Son deploymentlar"></a><br><sub><b>Canli pano — son deploymentlar</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/03-mobile-menu.png"><img src="docs/screenshots/mobile/03-mobile-menu.png" width="250" alt="Hamburger menu"></a><br><sub><b>Hamburger menu</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/05-dashboard-dark.png"><img src="docs/screenshots/mobile/05-dashboard-dark.png" width="250" alt="Koyu tema"></a><br><sub><b>Koyu tema</b></sub></td>
</tr>
<tr>
<td align="center"><a href="docs/screenshots/mobile/04-language-menu.png"><img src="docs/screenshots/mobile/04-language-menu.png" width="250" alt="Dil menusu"></a><br><sub><b>Dil menusu (bayrakli)</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/18-dashboard-english.png"><img src="docs/screenshots/mobile/18-dashboard-english.png" width="250" alt="Ingilizce pano"></a><br><sub><b>Ingilizce arayuz</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/06-deployments.png"><img src="docs/screenshots/mobile/06-deployments.png" width="250" alt="Deployment gecmisi"></a><br><sub><b>Deployment gecmisi</b></sub></td>
</tr>
<tr>
<td align="center"><a href="docs/screenshots/mobile/07-deployments-filtered.png"><img src="docs/screenshots/mobile/07-deployments-filtered.png" width="250" alt="Filtreli gecmis"></a><br><sub><b>Gecmis — filtreli</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/08-log-preview.png"><img src="docs/screenshots/mobile/08-log-preview.png" width="250" alt="Log onizleme"></a><br><sub><b>Log onizleme</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/09-deployment-details.png"><img src="docs/screenshots/mobile/09-deployment-details.png" width="250" alt="Deployment detayi"></a><br><sub><b>Deployment detayi</b></sub></td>
</tr>
<tr>
<td align="center"><a href="docs/screenshots/mobile/10-errors.png"><img src="docs/screenshots/mobile/10-errors.png" width="250" alt="Hata analizi"></a><br><sub><b>Hata analizi</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/11-error-signature.png"><img src="docs/screenshots/mobile/11-error-signature.png" width="250" alt="Hata imzasi"></a><br><sub><b>Hata imzasi detayi</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/12-connections.png"><img src="docs/screenshots/mobile/12-connections.png" width="250" alt="Baglantilar"></a><br><sub><b>Baglantilar</b></sub></td>
</tr>
<tr>
<td align="center"><a href="docs/screenshots/mobile/13-users.png"><img src="docs/screenshots/mobile/13-users.png" width="250" alt="Kullanicilar"></a><br><sub><b>Kullanicilar</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/14-translations.png"><img src="docs/screenshots/mobile/14-translations.png" width="250" alt="Ceviriler"></a><br><sub><b>Ceviriler</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/15-translations-missing.png"><img src="docs/screenshots/mobile/15-translations-missing.png" width="250" alt="Eksik ceviriler"></a><br><sub><b>Ceviriler — eksikler</b></sub></td>
</tr>
<tr>
<td align="center"><a href="docs/screenshots/mobile/16-diagnostics.png"><img src="docs/screenshots/mobile/16-diagnostics.png" width="250" alt="Tanilama"></a><br><sub><b>Tanilama</b></sub></td>
<td align="center"><a href="docs/screenshots/mobile/17-access-denied.png"><img src="docs/screenshots/mobile/17-access-denied.png" width="250" alt="Yetki reddi"></a><br><sub><b>Yetki reddi</b></sub></td>
<td></td>
</tr>
</table>

---

## Yapilandirma

Tum ayarlar ortam degiskeni ile gecilebilir (`__` ic ice bolum ayraci):

| Degisken | Aciklama |
|---|---|
| `Dokploy__BaseUrl` | *(istege bagli)* **Ilk kurulum baglantisi**: Dokploy koku, `/api` olmadan. Ayni sunucuda: `http://dokploy:3000`. Acilista "Varsayilan" baglanti olarak ice aktarilir; sonrasi panelden yonetilir. Bos birakilabilir — baglantiyi Baglantilar ekranindan ekleyin |
| `Dokploy__ApiKey` | *(istege bagli)* Ilk baglantinin anahtari ([diyalogun tum alanlari](#dokploy-api-anahtari-generate-api-key)). `BaseUrl` verildiyse bu da zorunludur (yalnizca biri verilirse acilista hata) |
| `Dokploy__ForceLegacyDiscovery` | `true` ise merkezi endpoint hic denenmez (baglanti bazinda da ayarlanabilir) |
| `Dokploy__AllowInvalidCertificates` | Self-signed sertifika icin (baglanti bazinda da ayarlanabilir) |
| `Auth__AdminEmail` | Ilk yonetici e-postasi (varsayilan `admin@trimango.local`) |
| `Auth__AdminPassword` | Bos ise `Super123!` kullanilir ve ilk giriste degistirme zorunlu olur |
| `Auth__SessionDays` | Oturum cerezi omru (7) |
| `Docker__Enabled` / `Docker__SocketPath` | Container logu (docker logs) ayarlari; soket mount edilmeli |
| `Cache__Provider` | `Memory` (varsayilan) veya `Redis` |
| `Cache__RedisConnectionString` | `Redis` secildiginde zorunlu (or. `redis:6379`) |
| `Cache__InstanceName` / `Cache__DefaultSeconds` | Redis anahtar oneki ve varsayilan yasam suresi |
| `ConnectionStrings__Default` | SQL Server baglanti dizesi (zorunlu; ortam degiskeninden) |
| `Monitor__IdlePollSeconds` / `Monitor__ActivePollSeconds` | Polling araliklari (15 / 2) |
| `Monitor__RetentionDays` | Kayit saklama suresi (90, `0` = sinirsiz) |
| `Logs__MountPath` / `Logs__HostPath` | Log mount noktasi ve Dokploy'un log koku |
| `Webhook__Token` | Webhook URL'indeki gizli anahtar. **Bos ise webhook ucu kapalidir (404).** |

---

## Dokploy API anahtari (Generate API Key)

`Dokploy__ApiKey` degeri Dokploy panelinde **Settings → API Keys → Generate API Key**
diyalogundan uretilir: *"Create a new API key for accessing the API. You can set an expiration
date and a custom prefix for better organization."*

Anahtar her istekte `x-api-key` basligiyla gonderilir.

### Temel alanlar

| Alan | Varsayilan / yer tutucu | Ne ise yarar | Monitor icin |
|---|---|---|---|
| **Name** | `My API Key` | Anahtarin panelde gorunen adi; sadece etiket, yetkiyi etkilemez | `dokploy-monitor` |
| **Prefix** | `my_app` | Uretilen anahtarin basina eklenen on ek; birden fazla anahtari ayirt etmeyi kolaylastirir | `monitor` (istege bagli, bos da birakilabilir) |
| **Expiration** | `Never` | Anahtarin gecerlilik suresi | **Never.** Sure verirseniz o tarihte Monitor sessizce 401/403 almaya baslar; belirti sadece Tanilama'daki "API anahtari gecerli ✘" olur |
| **Organization** | `Select organization` | Anahtarin hangi organizasyonun verilerini gorecegi | Izlenecek deployment'larin bulundugu organizasyon. Yanlis secim = baglanti saglikli ama **pano bos** |

### Rate Limiting

| Alan | Varsayilan | Ne ise yarar | Monitor icin |
|---|---|---|---|
| **Enable Rate Limiting** | kapali | Belirli bir zaman penceresi icindeki istek sayisini sinirlar (acilinca pencere/adet alanlari gorunur) | **Kapali birakin** — gerekce asagida |

### Request Limiting

| Alan | Yer tutucu | Ne ise yarar | Monitor icin |
|---|---|---|---|
| **Total Request Limit** | `Leave empty for unlimited` | Anahtarin toplam kullanabilecegi istek adedi; bos = sinirsiz | **Bos** |
| **Refill Amount** | `Amount to refill` | Her yenilemede kotaya eklenecek istek adedi | Bos (Total Request Limit bosken islevsiz) |
| **Refill Interval** | `Select refill interval` | Kotanin ne siklikla yenilenecegi. Secenekler: `1 hour`, `6 hours`, `12 hours`, `1 day`, `7 days`, `30 days` | Bos (listeyi secim yapmadan `Esc` ile kapatin) |

Diyalog **Cancel** / **Generate** ile kapanir. Uretilen anahtar **yalnizca bir kez** gosterilir;
kaybederseniz yenisini uretip `Dokploy__ApiKey` degiskenini guncellemek gerekir.

Anahtarin gidecegi yer: uretimde Dokploy'daki **Environment** sekmesi (`Dokploy__ApiKey`),
yerelde ise [user-secrets](#gizli-anahtarlar-user-secrets) — commit'lenen `appsettings*.json`
dosyalarina **yazilmaz**.

### Nicin kota/limit koymamak gerekiyor?

Monitor iki arka plan iscisiyle surekli polling yapar ve her biri ayri istek uretir
(`DeploymentSyncWorker` → `deployment.allCentralized`, `QueueSyncWorker` → `deployment.queueList`):

| Durum | Istek / dakika | Istek / gun |
|---|---|---|
| Bos (15 sn + 5 sn) | ~16 | ~23.000 |
| Aktif deployment (2 sn + 5 sn) | ~42 | ~60.000 (gun boyu aktif kalirsa) |
| Legacy mod, N servis | (N+1) × 4 + 12 | servis sayisiyla dogrusal buyur |

Limit asildiginda olanlar zincirleme kotulesir:

- 429 yanitlari GET'lerde **2 kez daha yeniden denenir** (POST'lar denenmez), yani sinira
  dayanan her cagri kotadan uc kat harcar.
- `deployment.allCentralized` 429 alirsa istemci bunu "endpoint yok" sayip ayni dongude
  legacy moda duser — bu mod **daha fazla** istek atar.
- `deployment.queueList` 429 alirsa o dongude kuyruk gorunumu "kullanilamiyor" olur.
- Pano hata gostermez, son bilinen durumu tutar: veri **eski ama yesil** gorunur.

Yine de sinir koymak zorundaysaniz once polling'i seyreltin
(`Monitor__IdlePollSeconds`, `Monitor__ActivePollSeconds`, `Monitor__QueuePollSeconds`)
ve kotayi bu araliklara gore hesaplayin. Varsayilan araliklarla emniyetli bir ust sinir:

| Alan | Deger |
|---|---|
| Total Request Limit | `10000` |
| Refill Amount | `10000` |
| Refill Interval | `1 hour` |

Yenileme araligini **saatlik** tutun: `1 day` ve uzeri araliklarda tek bir yogun build gunu
kotayi tuketir ve sonraki yenilemeye kadar pano guncellenmez.

### Yetki

Monitor bu anahtarla cogunlukla **okuma** yapar; panelden aksiyon kullanilacaksa iki **yazma**
cagrisina da ihtiyaci vardir: `deployment.killProcess` ve `application.redeploy` /
`compose.redeploy`. Durdur / Yeniden Deploy butonlarini kullanmayacaksaniz anahtari salt-okunur
bir kullanicidan uretmek yeterlidir.

---

## Dokploy'a kurulum

**Adim adim tam anlatim: [docs/DOKPLOY-KURULUM.md](docs/DOKPLOY-KURULUM.md)**
(API anahtari uretme, mount'lar, domain, webhook, sorun giderme tablosu ve guvenlik notu)

Ozet:

1. **Baglantiyi once test edin** (deploy etmeden):
   ```bash
   ./scripts/dokploy-check.sh https://dokploy.sirketiniz.com <API_KEY>
   ```
2. **Uygulamayi olustur**: Dokploy'da yeni bir Application, kaynak bu repo, build tipi **Dockerfile**.
3. **Ortam degiskenleri**:
   ```env
   ConnectionStrings__Default=Server=mssql;Database=DokployMonitor;User Id=sa;Password=...;TrustServerCertificate=True
   Dokploy__BaseUrl=http://dokploy:3000
   Dokploy__ApiKey=<API anahtari>
   Webhook__Token=<openssl rand -hex 32 ciktisi>
   ```
4. **Mount'lar** (Advanced → Volumes):
   - `/etc/dokploy/logs` → `/app/dokploy-logs` · **read-only** (build loglari icin)
   - `/var/lib/dokploy-monitor/data` → `/app/data` (log arsivi; veritabani SQL Server'da)

   Veri klasoru konteynerdeki root olmayan kullaniciya ait olmali:
   ```bash
   mkdir -p /var/lib/dokploy-monitor/data && chown -R 1654:1654 /var/lib/dokploy-monitor/data
   ```
5. **Domain**: Container Port **8080**, HTTPS + Let's Encrypt.
6. **Webhook** (Dokploy → Settings → Notifications → Add Notification → saglayici: **Custom**):
   ```
   https://monitor.<alan-adiniz>/api/webhooks/dokploy?token=<Webhook__Token>
   ```
   `App Deploy` ve `App Build Error` olaylarini isaretleyin. Token bu ekranda uretilmez —
   URL'in icinde tasinir, degeri `Webhook__Token` ile ayni olmali.
7. Deploy sonrasi `/Dashboard/Diagnostics` sayfasindan tum kontrollerin ✔ oldugunu dogrulayin.

---

## Yerel gelistirme

```bash
dotnet restore
dotnet test
dotnet run --project src/DokployMonitor.Web
```

### Gizli anahtarlar (user-secrets)

`appsettings.Development.json` yer tutucu degerlerle gelir ve **commit'lenir** — gercek API
anahtarini oraya yazmayin. Yerel calisirken gizli degerler user-secrets'ta tutulur: dosya
proje klasorunun disinda, ev dizininde durur, dolayisiyla git'e hic girmez.

Ilk kurulum (klasorde `cd src/DokployMonitor.Web` olmak sart — komutlar csproj'a gore calisir):

```bash
cd src/DokployMonitor.Web
dotnet user-secrets init                      # csproj'a UserSecretsId ekler (bir kez, zaten var)
dotnet user-secrets set "Dokploy:BaseUrl" "https://dokploy.sirketiniz.com"
dotnet user-secrets set "Dokploy:ApiKey" "dokploy_monitor_<anahtar>"
dotnet user-secrets set "Webhook:Token" "$(openssl rand -hex 32)"
```

Gunluk kullanim:

| Islem | Komut |
|---|---|
| Tum anahtarlari listele | `dotnet user-secrets list` |
| Ekle / degistir (ayni komut) | `dotnet user-secrets set "Dokploy:ApiKey" "<yeni deger>"` |
| Tek anahtari sil | `dotnet user-secrets remove "Dokploy:ApiKey"` |
| Hepsini sil | `dotnet user-secrets clear` |
| Baska klasorden calistir | `dotnet user-secrets list --project src/DokployMonitor.Web` |

Nerede saklanir?

```
~/.microsoft/usersecrets/<UserSecretsId>/secrets.json     # macOS / Linux
%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json   # Windows
```

`<UserSecretsId>` degeri `src/DokployMonitor.Web/DokployMonitor.Web.csproj` icinde yazilidir.
Dosya duz JSON'dur (sifreli degil); izinleri `600` olacak sekilde olusturulur. Elle de
duzenlenebilir, ancak `set` komutunu kullanmak daha guvenlidir.

Dikkat edilecek noktalar:

- Ic ice bolumler **iki nokta** ile yazilir: `Dokploy:ApiKey`. Ortam degiskeninde bunun
  karsiligi `Dokploy__ApiKey` (cift alt cizgi) — ikisini karistirmayin.
- User-secrets **yalnizca Development ortaminda** okunur. Uretimde (Dokploy) deger ortam
  degiskeninden gelir; oraya user-secrets tasinmaz.
- Deger degistirdikten sonra uygulamayi yeniden baslatin; yapilandirma acilista okunur.
- `list` ciktisi anahtarlari **maskesiz** yazar; ekran paylasirken dikkat.

> `NuGet.config` bu klasorde kurumsal AtplQuestions feed'ini devre disi birakir; paketler yalnizca
> nuget.org'dan cekilir.

### Sema degisikligi (FluentMigrator)

Sema **FluentMigrator** ile yonetilir; EF Core yalnizca sorgu/kayit katmanidir. Migration'lar
uygulama acilirken `MigrateUp` ile uygulanir — ayrica bir CLI adimi yok.

1. `src/DokployMonitor.Infrastructure/Persistence/Migrations/` altina yeni bir sinif ekleyin.
   Dosya adi tarih onekli, surum numarasi artan olmali:

   ```csharp
   [Migration(20260801120000, "Deployment tablosuna commit bilgisi ekle")]
   public sealed class AddCommitInfo : Migration
   {
       public override void Up() =>
           Alter.Table("Deployments").AddColumn("CommitSha").AsString(40).Nullable();

       public override void Down() =>
           Delete.Column("CommitSha").FromTable("Deployments");
   }
   ```

2. EF tarafindaki varlik/`MonitorDbContext` eslesmesini ayni sekilde guncelleyin.
3. `dotnet test` kosun: `MigrationSchemaTests` FluentMigrator semasini EF modelinin urettigi
   semayla kolon kolon karsilastirir; iki taraf ayrilirsa test kirmizi olur.

> SQL Server'da indeksli string kolonlar icin uzunluk siniri koyun (`nvarchar(max)`
> indekslenemez). Tarih kolonlari `AsDateTimeOffset()` kullanin.

### Yapilandirma dogrulama (FluentValidation)

`Dokploy`, `Logs`, `Monitor` ve `Webhook` bolumlerinin her biri bir `AbstractValidator` ile
dogrulanir ve `ValidateOnStart()` ile **acilista** kontrol edilir: hatali ayarda uygulama
ayaga kalkmaz, log'a hangi alanin neden gecersiz oldugunu yazar. Ornek:

```
DokployOptions.BaseUrl: Panelin koku yazilmali, sonuna /api eklenmemeli; ...
DokployOptions.ApiKey: Zorunlu. Dokploy > Settings > API Keys > Generate API Key ile uretilir.
```

Ayni altyapi ekran filtrelerinde de kullanilir (`DeploymentFilterValidator`): gecersiz bir
durum filtresi geldiginde sorgu hic calistirilmaz, kullaniciya sebep gosterilir.

---

## Bilinen sinirlar

- **Kuyruk**: Dokploy self-hosted artik BullMQ/Redis yerine bellek ici kuyruk kullaniyor.
  Kuyruk yalnizca `deployment.queueList` ile okunabilir; bu endpoint yoksa kuyruk gorunumu kapanir.
- **Uzak sunucu loglari**: `serverId` tasiyan (uzak sunucuda calisan) deployment'larin log dosyalari
  o sunucudadir; yerel mount ile okunamaz. Bu deployment'larda hata mesaji gorunur, tam log gorunmez.
- **Kimlik dogrulama**: Uygulamada henuz oturum yok. Iceriye acmadan once Dokploy'un
  Traefik middleware'i ile basic auth ekleyin veya sadece ic aga acin.
- Webhook payload'inda `deploymentId` bulunmadigindan bildirimler deployment kaydina
  kesin olarak baglanamaz; eslestirme `buildLink` icindeki servis kimligi uzerinden yapilir.
