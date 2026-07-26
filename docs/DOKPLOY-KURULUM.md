# Dokploy Tarafi Kurulum

Bu dokuman, Dokploy Monitor'u **Dokploy uzerinde bir servis olarak** calistirmak icin
Dokploy panelinde yapilmasi gereken ayarlari adim adim anlatir.

Ozet: bir API anahtari uretilecek, uygulama Dockerfile ile deploy edilecek, iki mount
eklenecek ve bir webhook tanimlanacak.

---

## Adim 0 — Ortami tanimak

Dokploy kurulumu su bilesenlerden olusur (resmi `install.sh`'ten):

| Bilesen | Ad | Not |
|---|---|---|
| Panel | `dokploy` (swarm service) | Port **3000**, ag: `dokploy-network` |
| Veritabani | `dokploy-postgres` | Bizim uygulamamiz buraya **dokunmaz** |
| Reverse proxy | `dokploy-traefik` | TLS'i sonlandirir |
| Build loglari | `/etc/dokploy/logs` (host uzerinde) | Salt-okunur mount edecegiz |

Dokploy'un deploy ettigi tum uygulamalar `dokploy-network` agina baglanir. Bu yuzden
Monitor, panele **`http://dokploy:3000`** adresiyle ic agdan erisebilir — trafik internete
cikmaz, TLS/sertifika derdi olmaz.

> Bu isim cozulmezse (ozel kurulum, uzak sunucu vb.) `Dokploy__BaseUrl` yerine panelin
> public adresini yazin: `https://dokploy.sirketiniz.com`

---

## Adim 1 — API anahtari uret

1. Dokploy panelinde sag ust profil menusu → **Settings**
2. **API / API Keys** sekmesi → **Generate API Key**
3. Bir ad verin (or. `dokploy-monitor`) ve uretilen anahtari kopyalayin.

> Anahtar yalnizca bir kez gosterilir. Kaybederseniz yenisini uretip ortam degiskenini
> guncellemeniz gerekir.

Monitor bu anahtarla **okuma** yapar (deployment listesi, kuyruk) ve iki **yazma**
islemi yapabilir: `deployment.killProcess` (durdur) ve `application.redeploy` /
`compose.redeploy` (yeniden deploy). Bu iki butonu kullanmayacaksaniz anahtari
salt-okunur bir kullanicidan uretmeniz yeterlidir.

---

## Adim 2 — Webhook token'i uret

Terminalde:

```bash
openssl rand -hex 32
```

Cikan degeri saklayin; hem uygulamanin ortam degiskenine hem de webhook URL'ine yazacagiz.
**Bu deger bos birakilirsa webhook ucu tamamen kapali kalir (404 doner)** — yani yanlislikla
kimlik dogrulamasiz acik kalmaz.

---

## Adim 3 — Log arsiv klasorunu hazirla

Uygulama arsivlenmis build loglarini `/app/data` altinda tutar (veritabani SQL Server'dadir).
Konteyner **root olmayan** kullanici (uid `1654`) ile calistigi icin klasorun sahipligi
ayarlanmali:

```bash
mkdir -p /var/lib/dokploy-monitor/data
chown -R 1654:1654 /var/lib/dokploy-monitor/data
```

---

## Adim 4 — Uygulamayi olustur

1. Dokploy → istediginiz **Project** → **Create Service** → **Application**
2. Ad: `dokploy-monitor`
3. **Provider** sekmesi:
   - Git saglayicinizi (GitHub/GitLab/Gitea) ve bu repoyu secin
   - Branch: `main`
   - **Build Path**: `/` (repo koku)
4. **Build Type** sekmesi:
   - **Dockerfile** secin
   - Dockerfile yolu: `Dockerfile`

---

## Adim 5 — Ortam degiskenleri

**Environment** sekmesine yapistirin (kendi degerlerinizle):

```env
# Zorunlu: SQL Server baglanti dizesi (deploy'lar arasinda veri kalir)
ConnectionStrings__Default=Server=mssql;Database=DokployMonitor;User Id=sa;Password=BURAYA_SIFRE;TrustServerCertificate=True;Encrypt=True

# Dokploy baglantisi: istege bagli. Bu ikisini hic vermeden de kurabilirsiniz;
# panele girip Baglantilar ekranindan ekleyin (birden fazla sunucu/anahtar da oradan).
# Verecekseniz IKISINI birlikte verin — yalnizca biri verilirse uygulama acilista hata verir.
Dokploy__BaseUrl=http://dokploy:3000
Dokploy__ApiKey=BURAYA_ADIM_1_ANAHTARI
Webhook__Token=BURAYA_ADIM_2_TOKENI

# Panel girisi (bos birakilirsa Super123! kullanilir ve ilk giriste degisim zorunlu olur)
Auth__AdminEmail=admin@sirketiniz.com
Auth__AdminPassword=

Logs__MountPath=/app/dokploy-logs
Logs__HostPath=/etc/dokploy/logs

Monitor__IdlePollSeconds=15
Monitor__ActivePollSeconds=2
Monitor__RetentionDays=90

# Onbellek: tek konteynerde Memory yeterli. Birden fazla ornek calisacaksa Redis:
Cache__Provider=Memory
# Cache__Provider=Redis
# Cache__RedisConnectionString=redis:6379
```

> `ConnectionStrings__Default` bos birakilirsa uygulama acilista hata verir.
> Veritabani yoksa uygulama `CREATE DATABASE` dener (sunucu yetkisi gerekir);
> onceden bos bir DB olusturmaniz da yeterlidir.

> Redis kullanacaksaniz Dokploy'da bir **Redis** servisi olusturup ayni `dokploy-network`
> agina baglayin; adres olarak servis adini verin (or. `monitor-redis:6379`).

Istege bagli:

| Degisken | Ne zaman |
|---|---|
| `Dokploy__AllowInvalidCertificates=true` | Panel self-signed sertifika kullaniyorsa |
| `Dokploy__ForceLegacyDiscovery=true` | Eski surumde merkezi endpoint hatali davraniyorsa |
| `Monitor__RetentionDays=0` | Hicbir kayit silinmesin |

---

## Adim 6 — Mount'lar

**Advanced → Volumes → Add Volume**, iki adet **Bind Mount**:

| # | Host yolu | Konteyner yolu | Mod | Nicin |
|---|---|---|---|---|
| 1 | `/etc/dokploy/logs` | `/app/dokploy-logs` | **read-only** | Build loglarini okumak icin |
| 2 | `/var/lib/dokploy-monitor/data` | `/app/data` | read-write | Log arsivi + DataProtection anahtarlari (auth/antiforgery) |
| 3 | `/var/run/docker.sock` | `/var/run/docker.sock` | **read-only** | Container loglari (`docker logs`) icin |

> 3 numarali mount olmazsa uygulama calisir; log goruntuleyicide "Container" sekmesi
> "Docker soketi bulunamadi" der ve build loguna duser. Docker soketi guclu bir yetkidir:
> yalnizca salt-okunur verin ve panele erisimi kisitli tutun.

> 1 numarali mount salt-okunur olmali. Monitor'un Dokploy'un loglarini degistirmesi
> gerekmiyor; boylece yanlislikla silme/bozma riski de kalmiyor.
>
> Bu mount olmazsa uygulama yine calisir ama deployment detay ekraninda log yerine
> "Log klasoru mount edilmemis" uyarisi gorunur.

---

## Adim 7 — Domain

**Domains** sekmesi → **Add Domain**:

- Host: `monitor.sirketiniz.com`
- **Container Port: `8080`** (uygulama bu portu dinler)
- HTTPS: acik, Certificate: **Let's Encrypt**

---

## Adim 8 — Deploy

**Deploy** butonuna basin. Ilk build birkac dakika surer (SDK imaji indirilecek).

Deploy bittiginde:

```
https://monitor.sirketiniz.com/health   →  Healthy
```

---

## Adim 9 — Webhook tanimla

Bu adim, build biter bitmez sonucun panoya **aninda** dusmesini saglar (polling'i beklemez).

**Onemli:** Bu ekran bir token **uretmez**. Token'i Adim 2'de siz uretiyorsunuz; burada
yalnizca o token'i icinde tasiyan URL'i Dokploy'a tanitiyorsunuz.

1. Dokploy → **Settings → Notifications** → **Add Notification**
2. **Select a provider** listesinden **Custom** secin.
   (Slack / Telegram / Discord / Lark / Microsoft Teams / Email / Resend / Gotify / ntfy /
   Pushover kutulari hazir servisler icindir; Monitor'un bekledigi ham JSON'u yalnizca
   **Custom** gonderir.)
3. **Name**: `Dokploy Monitor`
4. **Webhook URL**:
   ```
   https://monitor.sirketiniz.com/api/webhooks/dokploy?token=ADIM_2_TOKENI
   ```
   Token URL'in **icinde** gider; ayri bir alan yoktur. Ek bir "Channel" / baslik alani
   cikarsa bos birakin.
5. **Select the actions** bolumunde en az sunlari acin:
   - ✅ **App Deploy** — "Trigger the action when a app is deployed." (basarili build)
   - ✅ **App Build Error** — "Trigger the action when the build fails." (hatali build)
   - Istege bagli: Database Backup, Volume Backup, Docker Cleanup, Dokploy Restart
6. **Create** ile kaydedin, ardindan **Test Notification** ile deneyin — Monitor'un ana
   panosundaki "Webhook Bildirimleri" listesinde gorunmeli.

> **Test Notification** butonu kaydetmeden once de calisir; Monitor'da hicbir sey gorunmuyorsa
> once URL'deki token ile `Webhook__Token` degerinin ayni oldugunu kontrol edin (401 = token
> uyusmuyor, 404 = `Webhook__Token` bos birakilmis).

> Dokploy webhook payload'inda `deploymentId` gondermez; Monitor servis eslesmesini
> `buildLink` icindeki kimlikten cikarir. Bu yuzden webhook, deployment tablosunun
> yerine gecmez — onu hizlandirir.

---

## Adim 10 — Dogrulama

`https://monitor.sirketiniz.com/Dashboard/Diagnostics` sayfasini acin. Dort kontrol de ✔ olmali:

| Kontrol | ✘ ise ne yapmali |
|---|---|
| Sunucuya erisim | Baglantinin adresi yanlis veya konteyner `dokploy-network`'te degil (Baglantilar ekranindan duzeltin) |
| API anahtari gecerli | Anahtar hatali/silinmis — yenisini uretip Baglantilar ekranindan guncelleyin (kayitli anahtar formda yesil **kayitli** isaretiyle gorunur) |
| `deployment.allCentralized` | Eski Dokploy surumu; otomatik yedek moda duser, calismaya devam eder |
| `deployment.queueList` | Eski surum; kuyruk gorunumu kapanir, digerleri calisir |

Ayni sayfada log mount'unun durumu ve kopyalanabilir webhook URL'i de gosterilir.

---

## Sorun giderme

| Belirti | Neden / Cozum |
|---|---|
| Acilista `unable to open database file` | Adim 3'teki `chown 1654:1654` yapilmamis |
| Acilista `OptionsValidationException` / `DokployOptions.BaseUrl: ...` | Yapilandirma FluentValidation ile acilista dogrulanir; log'daki alan adini duzeltin (or. `BaseUrl`'in sonundaki `/api`'yi silin) |
| Panele girince "Kimlik Bilgilerini Guncelle" ekrani | Hesap varsayilan parolayla olusturulmus. Yeni bilgiler girin ya da **ayni bilgileri tekrar yazip** onaylayin; onaydan sonra ekran bir daha cikmaz. Bu adimi hic gormemek icin kurulumda `Auth__AdminPassword` verin |
| "Bu islem icin yetkiniz yok" | Hesap `Viewer` rolunde; Durdur/Yeniden Deploy/Replay ve kullanici-baglanti yonetimi `SuperAdmin` ister |
| Container logu "Docker soketi bulunamadi" | Adim 6'daki 3. mount eksik |
| Pano "1/2 baglanti okunamadi" diyor | Baglantilar ekranindan ilgili baglantiyi **Test** edin; adres/anahtar hatali ya da sunucu erisilemez |
| Acilista `CacheOptions.RedisConnectionString` hatasi | `Cache__Provider=Redis` verilmis ama adres bos; adresi girin ya da `Provider=Memory` yapin |
| Tanilama'da "Onbellek ✘" | Redis erisilemez. Uygulama calismaya devam eder (onbellek atlanir) ama Redis adresini/agini kontrol edin |
| Panel yanlis dilde aciliyor | Dil sirasi: cerez → tarayici `Accept-Language` → Turkce. Navbar'daki dil secicisiyle sabitleyin |
| Bir metin cevrilmemis gorunuyor | `/Translations` ekranindan (SuperAdmin) ilgili dili secip "Sadece cevrilmemisler" filtresiyle bulun; kaydettiginizde aninda uygulanir |
| Tanilama: "Sunucuya erisim ✘" | `http://dokploy:3000` cozulemiyor → panelin public URL'ini kullanin |
| Tanilama: "API anahtari gecerli ✘" (401/403) | Anahtar yanlis ya da yetkisi yetersiz |
| Pano bos, hata yok | Henuz hic deployment yok ya da API anahtari baska bir organizasyona ait |
| Log yerine "mount edilmemis" uyarisi | Adim 6'daki 1. mount eksik |
| Uzak sunucudaki deploy'un logu bos | Log dosyasi o sunucuda; yerel mount ile okunamaz (bilinen sinir) |
| Webhook gelmiyor | URL'deki token ile `Webhook__Token` ayni mi; Dokploy'da event'ler isaretli mi |
| Webhook 401 donuyor | Token uyusmuyor |
| Webhook 404 donuyor | `Webhook__Token` bos — uc kapali |
| "yedek mod (polling)" rozeti | SignalR baglantisi kurulamadi; uygulama 5 sn'de bir HTTP polling yapar. Once sayfayi yenileyip tekrar giris yapin (oturum/DataProtection). WebSocket icin asagidaki nota bakin |
| SignalR `WebSocket failed` / `negotiate 401` | (1) `/app/data` mount'u yoksa her redeploy oturumu bozar — Adim 6. (2) Traefik WebSocket'i dusuruyorsa istemci LongPolling'e duser; yine de 401 ise cikis yapip tekrar girin. (3) Birden fazla replica varsa sticky session gerekir (tek ornekte gerekmez) |

---

## Guvenlik notu

Uygulamada **kimlik dogrulama yok**. Internete acacaksaniz once erisimi kisitlayin:

**Secenek A — Traefik basic auth** (Dokploy → Advanced → Traefik middleware):

```bash
# Kullanici/parola hash'i uret
htpasswd -nb admin 'guclu-bir-parola'
```

Uretilen satiri bir `basicAuth` middleware'ine ekleyip uygulamanin router'ina baglayin.

**Secenek B — Sadece ic ag**: Domain tanimlamayin; VPN veya SSH tuneli ile erisin:

```bash
ssh -L 8080:localhost:3001 kullanici@sunucu
```

Her iki durumda da `/api/webhooks/dokploy` ucunun Dokploy tarafindan erisilebilir kalmasi gerekir
(Traefik basic auth kullaniyorsaniz bu yolu middleware'den muaf tutun).
