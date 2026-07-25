#!/usr/bin/env bash
# Dokploy baglantisini ve API yeteneklerini uygulamayi deploy etmeden dogrular.
#
# Kullanim:
#   ./scripts/dokploy-check.sh https://dokploy.sirketiniz.com <API_KEY>
# veya:
#   DOKPLOY_URL=... DOKPLOY_API_KEY=... ./scripts/dokploy-check.sh

set -uo pipefail

BASE_URL="${1:-${DOKPLOY_URL:-}}"
API_KEY="${2:-${DOKPLOY_API_KEY:-}}"

if [[ -z "$BASE_URL" || -z "$API_KEY" ]]; then
    echo "Kullanim: $0 <dokploy-url> <api-key>" >&2
    echo "   veya: DOKPLOY_URL=... DOKPLOY_API_KEY=... $0" >&2
    exit 2
fi

BASE_URL="${BASE_URL%/}"
API="$BASE_URL/api"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

ok=0
fail=0

# $1 = endpoint, $2 = aciklama, $3 = zorunlu mu (required|optional)
check() {
    local endpoint="$1" label="$2" requirement="$3"
    local body="$TMP/$(echo "$endpoint" | tr '/?=&' '____').json"

    local status
    status=$(curl -s -o "$body" -w '%{http_code}' --max-time 20 \
        -H "x-api-key: $API_KEY" -H 'accept: application/json' \
        "$API/$endpoint" 2>/dev/null)

    if [[ "$status" == "200" ]]; then
        local count
        count=$(python3 -c "
import json,sys
try:
    d=json.load(open('$body'))
    print(len(d) if isinstance(d,list) else '?')
except Exception:
    print('?')
" 2>/dev/null)
        printf '  \033[32m✔\033[0m %-34s HTTP 200  (%s kayit)\n' "$label" "$count"
        ok=$((ok + 1))
        return 0
    fi

    if [[ "$requirement" == "required" ]]; then
        printf '  \033[31m✘\033[0m %-34s HTTP %s\n' "$label" "$status"
        fail=$((fail + 1))
    else
        printf '  \033[33m!\033[0m %-34s HTTP %s  (opsiyonel)\n' "$label" "$status"
    fi
    return 1
}

echo
echo "Dokploy: $BASE_URL"
echo "----------------------------------------------------------------"

check "project.all"                "Erisim + API anahtari"          required
centralized=0; check "deployment.allCentralized" "Merkezi deployment listesi" optional && centralized=1
queue=0;       check "deployment.queueList"      "Kuyruk (sira gorunumu)"     optional && queue=1

echo "----------------------------------------------------------------"

if [[ $fail -gt 0 ]]; then
    echo "Baglanti kurulamadi. Kontrol edin:"
    echo "  · URL dogru mu (panelin adresi, /api olmadan)"
    echo "  · API anahtari gecerli mi (Dokploy > Settings > API Keys)"
    echo "  · 401/403 ise anahtar yanlis; 000 ise ag/DNS sorunu"
    exit 1
fi

if [[ $centralized -eq 1 ]]; then
    echo "→ Merkezi mod kullanilacak: tum deploymentlar tek istekte gelir."
else
    echo "→ Merkezi endpoint yok: servis servis toplama (legacy) moduna dusulecek."
    echo "  Calisir, sadece daha fazla istek uretir."
fi

if [[ $queue -eq 1 ]]; then
    echo "→ Kuyruk gorunumu aktif."
else
    echo "→ Kuyruk endpoint'i yok: 'sirada bekleyenler' paneli kapali kalacak."
fi

echo
echo "Hazir. Yerel calistirmak icin:"
echo "  cd src/DokployMonitor.Web"
echo "  dotnet user-secrets set \"Dokploy:BaseUrl\" \"$BASE_URL\""
echo "  dotnet user-secrets set \"Dokploy:ApiKey\" \"<api-key>\""
echo "  dotnet run"
echo
