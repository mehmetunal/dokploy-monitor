# Trimango Dokploy Monitor

**English:** ASP.NET Core MVC app that monitors all Dokploy deployments on one screen.
Deploy it as a Dokploy service; multiple servers/API keys are supported.

**Türkçe:** Dokploy’daki tüm deployment süreçlerini tek ekranda izleyen ASP.NET Core MVC
uygulaması. Dokploy’a servis olarak kurulur; birden fazla sunucu/API anahtarı desteklenir.

[English](#english) · [Türkçe](#türkçe)

---

# English

Questions it answers:

- Did a deploy start? Is it still running, and for how long?
- Did it fail — **which** error? (message + full build log)
- What is in the queue, and in which position?

## Architecture

```
src/DokployMonitor.Core             Entities, contracts, dashboard models (no deps)
src/DokployMonitor.Infrastructure   Dokploy REST client, EF Core (SQL Server), FluentMigrator, log readers
src/DokployMonitor.Web              MVC UI, SignalR, background workers, webhook endpoint
tests/DokployMonitor.Tests          xUnit tests
```

| Topic | Stack | Where |
|---|---|---|
| DB schema | **FluentMigrator** (`MigrateUp` at startup) | `Infrastructure/Persistence/Migrations` |
| Queries / writes | EF Core (SQL Server) | `Infrastructure/Persistence/MonitorDbContext.cs` |
| Config & request validation | **FluentValidation** (`ValidateOnStart`) | `*Validator.cs`, `Infrastructure/Validation` |
| Auth & roles | **ASP.NET Core Identity** (cookie) | `Infrastructure/Identity`, `Controllers/AccountController.cs` |
| Multi-Dokploy | Per-connection client factory | `Infrastructure/Dokploy/DokployClientFactory.cs` |
| Container logs | Docker Engine API (unix socket) | `Infrastructure/Docker` |
| Cache | `IDistributedCache` — **Memory or Redis** | `Infrastructure/Caching` |
| UI language | DB-backed `IStringLocalizer` (editable in panel) | `Infrastructure/Localization` |
| UI theme | Cookie-based, applied server-side | `Services/UiPreferences.cs` |

### Where data comes from

| Channel | Purpose | Notes |
|---|---|---|
| `GET /api/deployment.allCentralized` | Org-wide deployments in **one** call | Primary source |
| `GET /api/deployment.queueList` | Real queue: `waiting` / `active` | Only queue source |
| `POST` kill / redeploy | Panel actions | POSTs are not retried |
| Generic webhook (Dokploy → us) | Instant finish/error notify | No `deploymentId` in payload; ID parsed from `buildLink` |
| `/etc/dokploy/logs` (read-only mount) | Build logs (incl. live tail) | File-based; API WebSocket does not accept the API key |

On older Dokploy builds without `deployment.allCentralized`, the client falls back to
`project.all` + per-service `deployment.all` (visible on Diagnostics).

### Update loop

- **Adaptive polling**: 15 s idle, 2 s while a deploy is active.
- **Queue**: every 5 s; queue movement also triggers an immediate deployment sync.
- **Webhook**: triggers sync immediately → UI updates in seconds.
- **SignalR** (`/hubs/deployments`): dashboard + live logs. If the hub cannot connect, the
  browser falls back to `/dashboard/snapshot` polling.

## Sign-in, roles, and users

The panel **requires sign-in** (Identity cookies). There is no self-service registration;
only a `SuperAdmin` can create accounts.

**First login** — the admin account is seeded on first start:

```
Email    : admin@trimango.local
Password : Super123!
```

With that account the panel opens **no other page** until `/Account/ChangeCredentials` is
completed. You may set a new email/password **or re-enter the same values** to confirm and
dismiss the gate.

Prefer setting `Auth__AdminPassword` at install so the change step is skipped.

| Role | Permissions |
|---|---|
| `SuperAdmin` | Everything: users, Dokploy connections, **Stop / Redeploy / Replay** |
| `Viewer` | Read-only: dashboards, history, errors, logs. Action buttons hidden |

Unauthorized requests go to `/Account/AccessDenied`. Anonymous endpoints:
`/health`, `/Account/Login`, and the webhook (`/api/webhooks/dokploy`, token-protected).

## Multiple Dokploy connections

Each connection is one server + one API key, managed on **Connections** (SuperAdmin).
Sync walks every **enabled** connection and tags each deployment with its source.

- **`Dokploy__BaseUrl` / `Dokploy__ApiKey` are optional.** If omitted, the app starts and you
  add the connection in the UI. If both are set, they import as **"Default"** on first boot.
  Providing only one of them fails validation at startup.
- Key rotation is done in the UI (blank key field keeps the stored key).
- One failing connection does not stop the others; the dashboard shows a partial-failure hint.
- Queue is read per connection; positions are per-queue.
- History can filter by connection when more than one exists.
- Deleting a connection **keeps** collected deployment history.

> Request volume **scales with connection count**. Two connections ≈ 2× polling.

> API keys are stored in plaintext in SQL Server (alongside hashed user passwords). Protect
  DB access and backups; the UI only shows masked keys.

## UI: theme and language

**Theme** — System / Dark / Light via navbar (`dm.theme` cookie). The server sets
`<html data-bs-theme>` on first paint to avoid a flash.

**Language** — 17 locales: Turkish (source), English, Deutsch, Français, Español, Português,
Italiano, Nederlands, Polski, Русский, Українська, العربية (RTL), 简体中文, 日本語, 한국어,
हिन्दी, Bahasa Indonesia. Resolution order:

1. Explicit cookie (navbar picker)
2. Browser `Accept-Language`
3. Default: Turkish

### Translations in the database

There are **no resx files**. Strings live in `Translations` and are edited at `/Translations`
(SuperAdmin) with instant effect.

- Key = source (Turkish) string: `L["Canlı Pano"]`. Missing translation → source text.
- Missing keys are collected when first rendered.
- Seed data in `TranslationDefaults.cs` never overwrites non-empty panel edits.
- In-memory snapshot; refreshed on save and every ~30 s (multi-instance).

RTL languages set `<html dir="rtl">` automatically.

## Cache (Memory / Redis)

```env
Cache__Provider=Memory            # or Redis
Cache__RedisConnectionString=redis:6379
Cache__InstanceName=dokploy-monitor:
Cache__DefaultSeconds=30
```

- `Provider=Redis` with empty address → **startup failure** (no silent Memory fallback).
- Temporary Redis outage → cache skipped, DB used, warning logged.
- Use Redis for multiple Monitor replicas; Memory is fine for a single container.

## Screens

| Path | Content |
|---|---|
| `/Account/Login` | Sign-in (anonymous) |
| `/` | Live dashboard |
| `/Deployments` | Filterable history |
| `/Deployments/Details/{id}` | Live build log, **container log (docker logs)**, timeline, actions |
| `/Errors` | Error analysis |
| `/Dashboard/Diagnostics` | Per-connection capability test, Docker socket, webhook URL |
| `/Connections` | Dokploy servers/keys (**SuperAdmin**) |
| `/Users` | User admin (**SuperAdmin**) |
| `/Translations` | UI strings (**SuperAdmin**) |
| `/health` | Health (anonymous) |

### Screenshots

Captured from a running instance (Playwright). Desktop **1440×900**, mobile **390×844**.

#### Sign-in

![Sign-in](docs/screenshots/desktop/01-login.png)

#### Required credential change (first login)

![Required credential change (first login)](docs/screenshots/desktop/19-change-credentials.png)

#### Live dashboard — top (KPIs, active deployments, queue)

![Live dashboard — top (KPIs, active deployments, queue)](docs/screenshots/desktop/02-dashboard.png)

#### Live dashboard — bottom (recent deployments, webhooks)

![Live dashboard — bottom (recent deployments, webhooks)](docs/screenshots/desktop/02b-dashboard-recent.png)

#### Dark theme

![Dark theme](docs/screenshots/desktop/05-dashboard-dark.png)

#### Theme menu (System / Dark / Light)

![Theme menu (System / Dark / Light)](docs/screenshots/desktop/03-theme-menu.png)

#### Language menu (17 languages + system)

![Language menu (17 languages + system)](docs/screenshots/desktop/04-language-menu.png)

#### English UI

![English UI](docs/screenshots/desktop/18-dashboard-english.png)

#### Deployment history (filters + paging)

![Deployment history (filters + paging)](docs/screenshots/desktop/06-deployments.png)

#### History with status filter

![History with status filter](docs/screenshots/desktop/07-deployments-filtered.png)

#### Log preview (Container / Build)

![Log preview (Container / Build)](docs/screenshots/desktop/08-log-preview.png)

#### Deployment details (build + container logs)

![Deployment details (build + container logs)](docs/screenshots/desktop/09-deployment-details.png)

#### Error analysis

![Error analysis](docs/screenshots/desktop/10-errors.png)

#### Error signature detail

![Error signature detail](docs/screenshots/desktop/11-error-signature.png)

#### Connections (multi-Dokploy — SuperAdmin)

![Connections (multi-Dokploy — SuperAdmin)](docs/screenshots/desktop/12-connections.png)

#### Users (SuperAdmin)

![Users (SuperAdmin)](docs/screenshots/desktop/13-users.png)

#### Translations (SuperAdmin)

![Translations (SuperAdmin)](docs/screenshots/desktop/14-translations.png)

#### Translations — missing only

![Translations — missing only](docs/screenshots/desktop/15-translations-missing.png)

#### Diagnostics

![Diagnostics](docs/screenshots/desktop/16-diagnostics.png)

#### Access denied

![Access denied](docs/screenshots/desktop/17-access-denied.png)


### Mobile (390×844)

<table>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/01-login.png"><img src="docs/screenshots/mobile/01-login.png" width="250" alt="Sign-in"></a><br><sub><b>Sign-in</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/19-change-credentials.png"><img src="docs/screenshots/mobile/19-change-credentials.png" width="250" alt="Credential change"></a><br><sub><b>Credential change</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/02-dashboard.png"><img src="docs/screenshots/mobile/02-dashboard.png" width="250" alt="Live dashboard — top"></a><br><sub><b>Live dashboard — top</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/02b-dashboard-recent.png"><img src="docs/screenshots/mobile/02b-dashboard-recent.png" width="250" alt="Recent deployments"></a><br><sub><b>Recent deployments</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/03-mobile-menu.png"><img src="docs/screenshots/mobile/03-mobile-menu.png" width="250" alt="Hamburger menu"></a><br><sub><b>Hamburger menu</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/05-dashboard-dark.png"><img src="docs/screenshots/mobile/05-dashboard-dark.png" width="250" alt="Dark theme"></a><br><sub><b>Dark theme</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/04-language-menu.png"><img src="docs/screenshots/mobile/04-language-menu.png" width="250" alt="Language menu"></a><br><sub><b>Language menu</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/18-dashboard-english.png"><img src="docs/screenshots/mobile/18-dashboard-english.png" width="250" alt="English UI"></a><br><sub><b>English UI</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/06-deployments.png"><img src="docs/screenshots/mobile/06-deployments.png" width="250" alt="Deployment history"></a><br><sub><b>Deployment history</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/07-deployments-filtered.png"><img src="docs/screenshots/mobile/07-deployments-filtered.png" width="250" alt="Filtered history"></a><br><sub><b>Filtered history</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/08-log-preview.png"><img src="docs/screenshots/mobile/08-log-preview.png" width="250" alt="Log preview"></a><br><sub><b>Log preview</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/09-deployment-details.png"><img src="docs/screenshots/mobile/09-deployment-details.png" width="250" alt="Deployment details"></a><br><sub><b>Deployment details</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/10-errors.png"><img src="docs/screenshots/mobile/10-errors.png" width="250" alt="Error analysis"></a><br><sub><b>Error analysis</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/11-error-signature.png"><img src="docs/screenshots/mobile/11-error-signature.png" width="250" alt="Error signature"></a><br><sub><b>Error signature</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/12-connections.png"><img src="docs/screenshots/mobile/12-connections.png" width="250" alt="Connections"></a><br><sub><b>Connections</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/13-users.png"><img src="docs/screenshots/mobile/13-users.png" width="250" alt="Users"></a><br><sub><b>Users</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/14-translations.png"><img src="docs/screenshots/mobile/14-translations.png" width="250" alt="Translations"></a><br><sub><b>Translations</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/15-translations-missing.png"><img src="docs/screenshots/mobile/15-translations-missing.png" width="250" alt="Missing translations"></a><br><sub><b>Missing translations</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/16-diagnostics.png"><img src="docs/screenshots/mobile/16-diagnostics.png" width="250" alt="Diagnostics"></a><br><sub><b>Diagnostics</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/17-access-denied.png"><img src="docs/screenshots/mobile/17-access-denied.png" width="250" alt="Access denied"></a><br><sub><b>Access denied</b></sub></td>
<td></td>
</tr>
</table>

## Configuration

All settings accept environment variables (`__` nests sections):

| Variable | Description |
|---|---|
| `Dokploy__BaseUrl` | *(optional)* First-boot connection root **without** `/api`. Same host: `http://dokploy:3000` |
| `Dokploy__ApiKey` | *(optional)* First-boot key; required if `BaseUrl` is set |
| `Dokploy__ForceLegacyDiscovery` | Skip centralized endpoint |
| `Dokploy__AllowInvalidCertificates` | Self-signed TLS |
| `Auth__AdminEmail` | Seed admin email |
| `Auth__AdminPassword` | Empty → `Super123!` + forced change |
| `Auth__SessionDays` | Cookie lifetime (default 7) |
| `Docker__Enabled` / `Docker__SocketPath` | Container logs; socket must be mounted |
| `Cache__Provider` | `Memory` or `Redis` |
| `Cache__RedisConnectionString` | Required when Redis |
| `ConnectionStrings__Default` | SQL Server connection string (**required**) |
| `Monitor__IdlePollSeconds` / `Monitor__ActivePollSeconds` | Poll intervals (15 / 2) |
| `Monitor__RetentionDays` | Retention (90, `0` = forever) |
| `Logs__MountPath` / `Logs__HostPath` | Build-log mount |
| `Webhook__Token` | Webhook secret. **Empty disables the endpoint (404).** |
| `DataProtection__KeysPath` | Persist antiforgery/auth keys (default under `/app/data`) |

## Dokploy API key (Generate API Key)

Create under **Settings → API Keys → Generate API Key**. Sent as `x-api-key` on every request.

| Field | For Monitor |
|---|---|
| **Name** | e.g. `dokploy-monitor` |
| **Prefix** | optional, e.g. `monitor` |
| **Expiration** | **Never** (else silent 401 later) |
| **Organization** | Org that owns the deployments you watch |
| **Rate / request limits** | Prefer **off / empty** — see below |

### Why avoid rate limits?

Background workers poll continuously:

| State | Req / min | Req / day |
|---|---|---|
| Idle (15 s + 5 s) | ~16 | ~23,000 |
| Active deploy (2 s + 5 s) | ~42 | ~60,000 |
| Legacy mode, N services | scales with N | linear |

429s are retried on GETs (×3 quota burn), may force legacy mode (even more calls), and the
dashboard stays green on stale data. If you must limit, widen poll intervals first. Safe
ceiling at defaults: Total `10000` / refill `10000` / interval `1 hour`.

Monitor mostly **reads**; Stop/Redeploy need write calls (`killProcess`, `redeploy`).

## Deploy on Dokploy

**Full step-by-step (Turkish):** [docs/DOKPLOY-KURULUM.md](docs/DOKPLOY-KURULUM.md)

Summary:

1. Test connectivity: `./scripts/dokploy-check.sh https://dokploy.example.com <API_KEY>`
2. Create Application from this repo, build type **Dockerfile**.
3. Environment:
   ```env
   ConnectionStrings__Default=Server=mssql;Database=DokployMonitor;User Id=sa;Password=...;TrustServerCertificate=True
   Dokploy__BaseUrl=http://dokploy:3000
   Dokploy__ApiKey=<API key>
   Webhook__Token=<openssl rand -hex 32>
   ```
4. Volumes (Advanced → Volumes) — **only on dokploy-monitor**, not on other apps:
   - `/etc/dokploy/logs` → `/app/dokploy-logs` · **read-only**
   - `/var/lib/dokploy-monitor/data` → `/app/data` (log archive + DataProtection keys)
   - `/var/run/docker.sock` → `/var/run/docker.sock` · **read-only** (container logs)

   ```bash
   mkdir -p /var/lib/dokploy-monitor/data && chown -R 1654:1654 /var/lib/dokploy-monitor/data
   ```
5. Domain: container port **8080**, HTTPS.
6. Webhook (Custom notification):
   `https://monitor.<your-domain>/api/webhooks/dokploy?token=<Webhook__Token>`
   Enable App Deploy + App Build Error.
7. Verify ✔ on `/Dashboard/Diagnostics`.

## Local development

```bash
dotnet restore
dotnet test
dotnet run --project src/DokployMonitor.Web
```

### Secrets (user-secrets)

Do not put real keys in committed `appsettings*.json`.

```bash
cd src/DokployMonitor.Web
dotnet user-secrets set "Dokploy:BaseUrl" "https://dokploy.example.com"
dotnet user-secrets set "Dokploy:ApiKey" "dokploy_monitor_<key>"
dotnet user-secrets set "Webhook:Token" "$(openssl rand -hex 32)"
```

Nested config uses `:` in secrets and `__` in environment variables. User-secrets apply only
in Development.

### Schema changes (FluentMigrator)

Add a dated migration under `Infrastructure/Persistence/Migrations/`, update the EF model,
run `dotnet test` (`MigrationSchemaTests` compares both schemas). Use bounded string lengths
for indexed columns; dates as `AsDateTimeOffset()`.

### Config validation

`Dokploy`, `Logs`, `Monitor`, `Webhook` (and filters) use FluentValidation with
`ValidateOnStart` — bad config prevents startup.

## Known limitations

- **Queue**: self-hosted Dokploy uses in-memory queue; without `deployment.queueList` the
  queue UI is unavailable.
- **Remote server logs**: build/container logs for remote `serverId` targets are not on the
  Monitor host mount/socket — message may show, full log often will not.
- **Container logs** require `docker.sock` on Monitor and containers on the **same** Docker host.
- Webhook payloads lack `deploymentId`; matching uses service id inside `buildLink`.

---

# Türkçe

Dokploy'daki **tüm projelerin deployment süreçlerini tek ekranda** izleyen ASP.NET Core MVC
uygulaması. Kendisi de Dokploy'a bir servis olarak deploy edilir. **Birden fazla Dokploy
sunucusu / API anahtarı** aynı panelden izlenebilir.

Cevapladığı sorular:

- Deploy başladı mı? Hâlâ devam ediyor mu, ne kadar süredir?
- Hata mı verdi, **hangi** hata? (mesaj + tam build logu)
- Kuyrukta ne var, hangi iş kaçıncı sırada?

## Mimari

```
src/DokployMonitor.Core             Varlıklar, sözleşmeler, pano modelleri (bağımsız)
src/DokployMonitor.Infrastructure   Dokploy REST istemcisi, EF Core (SQL Server), FluentMigrator, log okuyucu
src/DokployMonitor.Web              MVC ekranları, SignalR, arka plan servisleri, webhook ucu
tests/DokployMonitor.Tests          xUnit testleri
```

| Konu | Kullanılan | Nerede |
|---|---|---|
| Veritabanı şeması | **FluentMigrator** (açılışta `MigrateUp`) | `Infrastructure/Persistence/Migrations` |
| Sorgu / kayıt | EF Core (SQL Server) | `Infrastructure/Persistence/MonitorDbContext.cs` |
| Yapılandırma ve istek doğrulama | **FluentValidation** (`ValidateOnStart`) | `*Validator.cs`, `Infrastructure/Validation` |
| Giriş ve roller | **ASP.NET Core Identity** (cookie) | `Infrastructure/Identity`, `Controllers/AccountController.cs` |
| Çoklu Dokploy sunucusu | Bağlantı başına istemci fabrikası | `Infrastructure/Dokploy/DokployClientFactory.cs` |
| Container logları | Docker Engine API (unix socket) | `Infrastructure/Docker` |
| Önbellek | `IDistributedCache` — **Memory veya Redis** | `Infrastructure/Caching` |
| Arayüz dili | Veritabanı tabanlı `IStringLocalizer` | `Infrastructure/Localization` |
| Arayüz teması | Çerez tabanlı, sunucu tarafında uygulanır | `Services/UiPreferences.cs` |

### Veri nereden geliyor?

| Kanal | Ne için | Not |
|---|---|---|
| `GET /api/deployment.allCentralized` | Tüm organizasyonun deployment'ları tek istekte | Birincil kaynak |
| `GET /api/deployment.queueList` | Gerçek kuyruk: `waiting` / `active` | Kuyruk görünümünün tek kaynağı |
| `POST` kill / redeploy | Panelden aksiyon | POST'lar yeniden denenmez |
| Generic webhook (Dokploy → bize) | Build biter bitmez anlık bildirim | `deploymentId` yok; `buildLink`'ten ID ayıklanır |
| `/etc/dokploy/logs` (salt-okunur mount) | Build logları (canlı takip dahil) | Dosyadan okunur |

`deployment.allCentralized` yoksa istemci `project.all` + servis başına `deployment.all`
moduna düşer (Tanılama'da görünür).

### Güncelleme döngüsü

- **Uyarlanabilir polling**: boş zamanda 15 sn, aktif deployment varken 2 sn.
- **Kuyruk**: 5 sn'de bir; kuyrukta hareket olunca deployment senkronu da tetiklenir.
- **Webhook**: geldiği anda senkron → sonuç saniyeler içinde ekranda.
- **SignalR** (`/hubs/deployments`): pano + canlı log. Bağlantı kurulamazsa tarayıcı
  `/dashboard/snapshot` polling'ine düşer.

## Giriş, roller ve kullanıcılar

Panel **giriş zorunludur**. Kendi kendine kayıt yoktur; hesapları yalnızca `SuperAdmin` oluşturur.

**İlk giriş** — yönetici hesabı ilk açılışta oluşur:

```
E-posta : admin@trimango.local
Parola  : Super123!
```

Bu hesapla `/Account/ChangeCredentials` tamamlanmadan panel açılmaz. Yeni bilgi girebilir
veya **aynı bilgileri tekrar yazarak** onaylayabilirsiniz. Kurulumda `Auth__AdminPassword`
verirseniz bu adım atlanır.

| Rol | Yetki |
|---|---|
| `SuperAdmin` | Her şey: kullanıcılar, bağlantılar, **Durdur / Yeniden Deploy / Replay** |
| `Viewer` | Salt okuma; aksiyon butonları görünmez |

Anonim uçlar: `/health`, `/Account/Login`, webhook (`/api/webhooks/dokploy`, token ile).

## Çoklu Dokploy bağlantısı

Her bağlantı bir sunucu + bir API anahtarıdır (**Bağlantılar**, SuperAdmin).

- **`Dokploy__BaseUrl` / `Dokploy__ApiKey` isteğe bağlıdır.** İkisi birden verilirse ilk
  açılışta **"Varsayılan"** olarak içe aktarılır; yalnızca biri verilirse açılış hatası.
- Anahtar paneldan döndürülür (boş alan = mevcut anahtar korunur).
- Bir bağlantı hata verse diğerleri çalışmaya devam eder.
- Bağlantı silinse toplanan geçmiş **korunur**.

> İstek hacmi bağlantı sayısıyla çarpılır. Anahtarlar SQL Server'da düz metin tutulur —
  DB erişimini koruyun.

## Arayüz: tema ve dil

**Tema** — Sistem / Koyu / Aydınlık (`dm.theme` çerezi; sunucu ilk render'da yazar).

**Dil** — 17 dil. Sıra: çerez → `Accept-Language` → Türkçe.

### Çeviriler veritabanında

**resx yoktur.** `/Translations` (SuperAdmin) ile anında uygulanır. Anahtar = Türkçe kaynak
metin. Tohum (`TranslationDefaults.cs`) dolu satırları ezmez.

## Önbellek (Memory / Redis)

```env
Cache__Provider=Memory
Cache__RedisConnectionString=redis:6379
Cache__InstanceName=dokploy-monitor:
Cache__DefaultSeconds=30
```

`Provider=Redis` + boş adres → açılış hatası. Redis geçici düşerse istek yine çalışır
(önbellek atlanır). Çok örnekte Redis, tek konteynerde Memory yeterlidir.

## Ekranlar

| Yol | İçerik |
|---|---|
| `/Account/Login` | Giriş (anonim) |
| `/` | Canlı pano |
| `/Deployments` | Filtrelenebilir geçmiş |
| `/Deployments/Details/{id}` | Canlı build logu, **container logu**, zaman çizelgesi, aksiyonlar |
| `/Errors` | Hata analizi |
| `/Dashboard/Diagnostics` | Yetenek testi, Docker soketi, webhook URL |
| `/Connections` / `/Users` / `/Translations` | SuperAdmin |
| `/health` | Sağlık (anonim) |

### Ekran görüntüleri

#### Giriş

![Giriş](docs/screenshots/desktop/01-login.png)

#### Zorunlu kimlik güncelleme (ilk giriş)

![Zorunlu kimlik güncelleme (ilk giriş)](docs/screenshots/desktop/19-change-credentials.png)

#### Canlı pano — üst (KPI'lar, aktif deployment'lar, kuyruk)

![Canlı pano — üst (KPI'lar, aktif deployment'lar, kuyruk)](docs/screenshots/desktop/02-dashboard.png)

#### Canlı pano — alt (son deployment'lar, webhook'lar)

![Canlı pano — alt (son deployment'lar, webhook'lar)](docs/screenshots/desktop/02b-dashboard-recent.png)

#### Koyu tema

![Koyu tema](docs/screenshots/desktop/05-dashboard-dark.png)

#### Tema menüsü (Sistem / Koyu / Açık)

![Tema menüsü (Sistem / Koyu / Açık)](docs/screenshots/desktop/03-theme-menu.png)

#### Dil menüsü (17 dil + sistem)

![Dil menüsü (17 dil + sistem)](docs/screenshots/desktop/04-language-menu.png)

#### İngilizce arayüz

![İngilizce arayüz](docs/screenshots/desktop/18-dashboard-english.png)

#### Deployment geçmişi (filtre + sayfalama)

![Deployment geçmişi (filtre + sayfalama)](docs/screenshots/desktop/06-deployments.png)

#### Geçmiş — durum filtresi uygulanmış

![Geçmiş — durum filtresi uygulanmış](docs/screenshots/desktop/07-deployments-filtered.png)

#### Log önizleme (Container / Build)

![Log önizleme (Container / Build)](docs/screenshots/desktop/08-log-preview.png)

#### Deployment detayı (build + container log)

![Deployment detayı (build + container log)](docs/screenshots/desktop/09-deployment-details.png)

#### Hata analizi

![Hata analizi](docs/screenshots/desktop/10-errors.png)

#### Hata imzası detayı

![Hata imzası detayı](docs/screenshots/desktop/11-error-signature.png)

#### Bağlantılar (çoklu Dokploy — SuperAdmin)

![Bağlantılar (çoklu Dokploy — SuperAdmin)](docs/screenshots/desktop/12-connections.png)

#### Kullanıcılar (SuperAdmin)

![Kullanıcılar (SuperAdmin)](docs/screenshots/desktop/13-users.png)

#### Çeviriler (SuperAdmin)

![Çeviriler (SuperAdmin)](docs/screenshots/desktop/14-translations.png)

#### Çeviriler — yalnızca eksikler

![Çeviriler — yalnızca eksikler](docs/screenshots/desktop/15-translations-missing.png)

#### Tanılama

![Tanılama](docs/screenshots/desktop/16-diagnostics.png)

#### Yetki reddi

![Yetki reddi](docs/screenshots/desktop/17-access-denied.png)


### Mobil (390×844)

<table>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/01-login.png"><img src="docs/screenshots/mobile/01-login.png" width="250" alt="Giriş"></a><br><sub><b>Giriş</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/19-change-credentials.png"><img src="docs/screenshots/mobile/19-change-credentials.png" width="250" alt="Kimlik güncelleme"></a><br><sub><b>Kimlik güncelleme</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/02-dashboard.png"><img src="docs/screenshots/mobile/02-dashboard.png" width="250" alt="Canlı pano — üst"></a><br><sub><b>Canlı pano — üst</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/02b-dashboard-recent.png"><img src="docs/screenshots/mobile/02b-dashboard-recent.png" width="250" alt="Son deployment'lar"></a><br><sub><b>Son deployment'lar</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/03-mobile-menu.png"><img src="docs/screenshots/mobile/03-mobile-menu.png" width="250" alt="Hamburger menü"></a><br><sub><b>Hamburger menü</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/05-dashboard-dark.png"><img src="docs/screenshots/mobile/05-dashboard-dark.png" width="250" alt="Koyu tema"></a><br><sub><b>Koyu tema</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/04-language-menu.png"><img src="docs/screenshots/mobile/04-language-menu.png" width="250" alt="Dil menüsü"></a><br><sub><b>Dil menüsü</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/18-dashboard-english.png"><img src="docs/screenshots/mobile/18-dashboard-english.png" width="250" alt="İngilizce arayüz"></a><br><sub><b>İngilizce arayüz</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/06-deployments.png"><img src="docs/screenshots/mobile/06-deployments.png" width="250" alt="Deployment geçmişi"></a><br><sub><b>Deployment geçmişi</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/07-deployments-filtered.png"><img src="docs/screenshots/mobile/07-deployments-filtered.png" width="250" alt="Geçmiş — filtreli"></a><br><sub><b>Geçmiş — filtreli</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/08-log-preview.png"><img src="docs/screenshots/mobile/08-log-preview.png" width="250" alt="Log önizleme"></a><br><sub><b>Log önizleme</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/09-deployment-details.png"><img src="docs/screenshots/mobile/09-deployment-details.png" width="250" alt="Deployment detayı"></a><br><sub><b>Deployment detayı</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/10-errors.png"><img src="docs/screenshots/mobile/10-errors.png" width="250" alt="Hata analizi"></a><br><sub><b>Hata analizi</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/11-error-signature.png"><img src="docs/screenshots/mobile/11-error-signature.png" width="250" alt="Hata imzası"></a><br><sub><b>Hata imzası</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/12-connections.png"><img src="docs/screenshots/mobile/12-connections.png" width="250" alt="Bağlantılar"></a><br><sub><b>Bağlantılar</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/13-users.png"><img src="docs/screenshots/mobile/13-users.png" width="250" alt="Kullanıcılar"></a><br><sub><b>Kullanıcılar</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/14-translations.png"><img src="docs/screenshots/mobile/14-translations.png" width="250" alt="Çeviriler"></a><br><sub><b>Çeviriler</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/15-translations-missing.png"><img src="docs/screenshots/mobile/15-translations-missing.png" width="250" alt="Eksik çeviriler"></a><br><sub><b>Eksik çeviriler</b></sub></td>
</tr>
<tr>
<td align="center" width="33%"><a href="docs/screenshots/mobile/16-diagnostics.png"><img src="docs/screenshots/mobile/16-diagnostics.png" width="250" alt="Tanılama"></a><br><sub><b>Tanılama</b></sub></td>
<td align="center" width="33%"><a href="docs/screenshots/mobile/17-access-denied.png"><img src="docs/screenshots/mobile/17-access-denied.png" width="250" alt="Yetki reddi"></a><br><sub><b>Yetki reddi</b></sub></td>
<td></td>
</tr>
</table>

## Yapılandırma

| Değişken | Açıklama |
|---|---|
| `Dokploy__BaseUrl` | *(isteğe bağlı)* İlk kurulum kökü, `/api` olmadan |
| `Dokploy__ApiKey` | *(isteğe bağlı)* `BaseUrl` varsa zorunlu |
| `Dokploy__ForceLegacyDiscovery` | Merkezi endpoint'i atla |
| `Dokploy__AllowInvalidCertificates` | Self-signed TLS |
| `Auth__AdminEmail` / `Auth__AdminPassword` | İlk yönetici; parola boşsa `Super123!` + zorunlu değişim |
| `Auth__SessionDays` | Oturum ömrü (7) |
| `Docker__Enabled` / `Docker__SocketPath` | Container logu; soket mount edilmeli |
| `Cache__*` | Memory / Redis |
| `ConnectionStrings__Default` | SQL Server (**zorunlu**) |
| `Monitor__IdlePollSeconds` / `ActivePollSeconds` | 15 / 2 |
| `Monitor__RetentionDays` | 90 (`0` = sınırsız) |
| `Logs__MountPath` / `Logs__HostPath` | Build log mount |
| `Webhook__Token` | Boşsa webhook kapalı (404) |
| `DataProtection__KeysPath` | Auth/antiforgery anahtarları (`/app/data` altında) |

## Dokploy API anahtarı (Generate API Key)

`Dokploy__ApiKey`, Dokploy panelinde **Settings → API Keys → Generate API Key** ile üretilir.
Her istekte `x-api-key` başlığıyla gönderilir. Anahtar **yalnızca bir kez** gösterilir.

### Temel alanlar

| Alan | Monitor için |
|---|---|
| **Name** | `dokploy-monitor` (yalnızca etiket) |
| **Prefix** | isteğe bağlı, ör. `monitor` |
| **Expiration** | **Never** — süre bitince Monitor sessizce 401/403 alır |
| **Organization** | İzlenecek deployment’ların organizasyonu (yanlış seçim = pano boş) |

### Rate / Request Limiting

| Alan | Monitor için |
|---|---|
| Enable Rate Limiting | **Kapalı** |
| Total Request Limit | **Boş** (sınırsız) |
| Refill Amount / Interval | Limit yokken işlevsiz |

### Neden kota koymamak?

| Durum | İstek / dk | İstek / gün |
|---|---|---|
| Boş (15 sn + 5 sn) | ~16 | ~23.000 |
| Aktif deployment (2 sn + 5 sn) | ~42 | ~60.000 |
| Legacy mod, N servis | (N+1)×… | servis sayısıyla büyür |

429 yanıtları GET’lerde yeniden denenir (kota 3 kat harcanır), istemci legacy moda düşebilir
(daha fazla istek), pano hata göstermeden **eski ama yeşil** kalabilir. Mecbursanız önce
`Monitor__*PollSeconds` değerlerini büyütün. Varsayılanlarla güvenli tavan:

| Alan | Değer |
|---|---|
| Total Request Limit | `10000` |
| Refill Amount | `10000` |
| Refill Interval | `1 hour` (günlük aralık yoğun build gününde kotayı bitirir) |

Üretimde anahtar Dokploy **Environment**’ta (`Dokploy__ApiKey`); yerelde user-secrets —
commit’lenen `appsettings*.json` dosyalarına yazılmaz.

## Dokploy'a kurulum

**Adım adım:** [docs/DOKPLOY-KURULUM.md](docs/DOKPLOY-KURULUM.md)

1. `./scripts/dokploy-check.sh https://dokploy.sirketiniz.com <API_KEY>`
2. Application + Dockerfile.
3. Ortam: `ConnectionStrings__Default`, isteğe bağlı `Dokploy__*`, `Webhook__Token`.
4. Mount'lar (**yalnızca dokploy-monitor**):
   - `/etc/dokploy/logs` → `/app/dokploy-logs` (ro)
   - `/var/lib/dokploy-monitor/data` → `/app/data`
   - `/var/run/docker.sock` → `/var/run/docker.sock` (ro)
   ```bash
   mkdir -p /var/lib/dokploy-monitor/data && chown -R 1654:1654 /var/lib/dokploy-monitor/data
   ```
5. Domain port **8080**, HTTPS.
6. Webhook Custom URL + token.
7. `/Dashboard/Diagnostics` ✔.

## Yerel geliştirme

```bash
dotnet restore && dotnet test
dotnet run --project src/DokployMonitor.Web
```

### Gizli anahtarlar (user-secrets)

```bash
cd src/DokployMonitor.Web
dotnet user-secrets set "Dokploy:BaseUrl" "https://dokploy.sirketiniz.com"
dotnet user-secrets set "Dokploy:ApiKey" "dokploy_monitor_<anahtar>"
dotnet user-secrets set "Webhook:Token" "$(openssl rand -hex 32)"
```

Secrets'ta `:`, ortam değişkeninde `__`. Yalnızca Development.

### Şema (FluentMigrator)

Yeni migration + EF modeli + `dotnet test` (`MigrationSchemaTests`). İndeksli string'lerde
uzunluk sınırı; tarihler `AsDateTimeOffset()`.

### Yapılandırma doğrulama

FluentValidation + `ValidateOnStart` — hatalı ayarda uygulama ayağa kalkmaz.

## Bilinen sınırlar

- **Kuyruk**: `deployment.queueList` yoksa kuyruk UI kapanır.
- **Uzak sunucu logları**: uzak `serverId` build/container logları bu host'tan okunamaz.
- **Container logları**: Monitor'da `docker.sock` + aynı Docker host.
- Webhook'ta `deploymentId` yok; eşleştirme `buildLink` üzerinden.
