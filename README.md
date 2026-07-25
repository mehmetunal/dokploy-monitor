# Dokploy Monitor

Dokploy'daki **tum projelerin deployment sureclerini tek ekranda** izleyen ASP.NET Core MVC uygulamasi.
Kendisi de Dokploy'a bir servis olarak deploy edilir.

Cevapladigi sorular:

- Deploy basladi mi? Hala devam ediyor mu, ne kadar suredir?
- Hata mi verdi, **hangi** hata? (mesaj + tam build logu)
- Kuyrukta ne var, hangi is kacinci sirada?

---

## Mimari

```
src/DokployMonitor.Core             Varliklar, sozlesmeler, pano modelleri (bagimsiz)
src/DokployMonitor.Infrastructure   Dokploy REST istemcisi, EF Core (SQLite), log okuyucu
src/DokployMonitor.Web              MVC ekranlari, SignalR, arka plan servisleri, webhook ucu
tests/DokployMonitor.Tests          xUnit testleri
```

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

## Ekranlar

| Yol | Icerik |
|---|---|
| `/` | Canli pano: KPI'lar, aktif deploymentlar (canli sayac), kuyruk, son deploymentlar, webhook bildirimleri |
| `/Deployments` | Filtrelenebilir gecmis (proje / durum / metin arama) |
| `/Deployments/Details/{id}` | Canli build logu, hata mesaji, olay zaman cizelgesi, servisin son deploylari, Durdur / Yeniden Deploy |
| `/Errors` | Hata analizi: normalize edilmis imzalara gore gruplanmis hatalar |
| `/Dashboard/Diagnostics` | Baglanti ve yetenek testi, webhook URL'i |
| `/health` | Saglik ucu |

---

## Yapilandirma

Tum ayarlar ortam degiskeni ile gecilebilir (`__` ic ice bolum ayraci):

| Degisken | Aciklama |
|---|---|
| `Dokploy__BaseUrl` | Dokploy koku, `/api` olmadan. Ayni sunucuda: `http://dokploy:3000` |
| `Dokploy__ApiKey` | Dokploy → Settings → API Keys |
| `Dokploy__ForceLegacyDiscovery` | `true` ise merkezi endpoint hic denenmez |
| `Dokploy__AllowInvalidCertificates` | Self-signed sertifika icin |
| `ConnectionStrings__Default` | SQLite yolu (varsayilan `/app/data/monitor.db`) |
| `Monitor__IdlePollSeconds` / `Monitor__ActivePollSeconds` | Polling araliklari (15 / 2) |
| `Monitor__RetentionDays` | Kayit saklama suresi (90, `0` = sinirsiz) |
| `Logs__MountPath` / `Logs__HostPath` | Log mount noktasi ve Dokploy'un log koku |
| `Webhook__Token` | Webhook URL'indeki gizli anahtar. **Bos ise webhook ucu kapalidir (404).** |

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
   Dokploy__BaseUrl=http://dokploy:3000
   Dokploy__ApiKey=<API anahtari>
   Webhook__Token=<openssl rand -hex 32 ciktisi>
   ```
4. **Mount'lar** (Advanced → Volumes):
   - `/etc/dokploy/logs` → `/app/dokploy-logs` · **read-only** (build loglari icin)
   - `/var/lib/dokploy-monitor/data` → `/app/data` (SQLite + log arsivi)

   Veri klasoru konteynerdeki root olmayan kullaniciya ait olmali:
   ```bash
   mkdir -p /var/lib/dokploy-monitor/data && chown -R 1654:1654 /var/lib/dokploy-monitor/data
   ```
5. **Domain**: Container Port **8080**, HTTPS + Let's Encrypt.
6. **Webhook** (Dokploy → Settings → Notifications → Webhook):
   ```
   https://monitor.<alan-adiniz>/api/webhooks/dokploy?token=<Webhook__Token>
   ```
   `App Deploy` ve `App Build Error` olaylarini isaretleyin.
7. Deploy sonrasi `/Dashboard/Diagnostics` sayfasindan tum kontrollerin ✔ oldugunu dogrulayin.

---

## Yerel gelistirme

```bash
dotnet restore
dotnet test
dotnet run --project src/DokployMonitor.Web
```

`appsettings.Development.json` yer tutucu degerlerle gelir; gercek bir Dokploy'a baglanmak icin
kullanici gizli anahtarlarini kullanin:

```bash
cd src/DokployMonitor.Web
dotnet user-secrets init
dotnet user-secrets set "Dokploy:BaseUrl" "https://dokploy.sirketiniz.com"
dotnet user-secrets set "Dokploy:ApiKey" "..."
```

Sema degisikligi sonrasi migration:

```bash
dotnet dotnet-ef migrations add <Ad> \
  --project src/DokployMonitor.Infrastructure \
  --startup-project src/DokployMonitor.Web \
  --output-dir Persistence/Migrations
```

> `NuGet.config` bu klasorde kurumsal AtplQuestions feed'ini devre disi birakir; paketler yalnizca
> nuget.org'dan cekilir.

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
