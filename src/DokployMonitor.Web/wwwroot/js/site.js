// Ortak yardimcilar: bicimlendirme, durum rozetleri ve canli gecen-sure sayaclari.
window.dm = (function () {
    // Ceviriler sunucudan gelir (window.dmI18n); anahtar bulunamazsa Ingilizce kaynak metin.
    // Ek argumanlar {0}, {1}, ... yer tutucularini doldurur.
    function t(key) {
        const dict = window.dmI18n || {};
        let text = dict[key] || key;
        for (let i = 1; i < arguments.length; i++) {
            text = text.replaceAll('{' + (i - 1) + '}', String(arguments[i]));
        }
        return text;
    }

    const statusMeta = {
        running: { key: 'running', css: 'status-running' },
        done: { key: 'succeeded', css: 'status-done' },
        error: { key: 'ERROR', css: 'status-error' },
        cancelled: { key: 'cancelled', css: 'status-cancelled' },
        unknown: { key: 'unknown', css: 'status-unknown' }
    };

    function meta(status) {
        return statusMeta[(status || '').toLowerCase()] || statusMeta.unknown;
    }

    function escapeHtml(value) {
        if (value === null || value === undefined) return '';
        return String(value)
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function statusBadge(status) {
        const m = meta(status);
        return '<span class="status-badge ' + m.css + '">' + escapeHtml(t(m.key)) + '</span>';
    }

    // Docker/build ciktilarindaki ANSI renk kodlari (or. \x1b[31m).
    const ansiPattern = /\x1B\[[0-9;?]*[ -/]*[@-~]/g;

    function cleanAnsi(line) {
        return String(line === null || line === undefined ? '' : line).replace(ansiPattern, '');
    }

    // Log satirlarinin seviyesi: her satir rozetlenir (hata / uyari / basarili / bilgi),
    // renkli sol kenar ve soluk zemin sorunlu satirlari goz taramasiyla bulunur hale getirir.
    // Sira onemli: buildkit ciktisi "#22 33.99 ... warning CS8602" gibi satirlari once
    // "bilgi" sanmamak icin hata ve uyari desenleri once denenir.
    const logLevels = [
        { level: 'error', pattern: /\berror\s+[A-Z]{2}\d+|\berrors?\b|\berr!|\bfatal\b|\bfailed\b|\bfailure\b|\bexception\b|\bpanic\b|non-zero code|\[ERR\]|❌/i },
        { level: 'warning', pattern: /\bwarning\s+[A-Z]{2}\d+|\bwarnings?\b|\bwarn\b|\bdeprecated\b|\[WRN\]|⚠/i },
        { level: 'success', pattern: /successfully|succeeded|\bconverged\b|\bhealthy\b|✓|✅/i }
    ];

    function logLevel(line) {
        for (let i = 0; i < logLevels.length; i++) {
            if (logLevels[i].pattern.test(line)) return logLevels[i].level;
        }
        return 'info';
    }

    /// Tek log satiri: seviye rozeti + metin. Metin textContent ile yazilir (HTML kacisi bedava).
    function buildLogLine(raw) {
        const text = cleanAnsi(raw);
        const level = logLevel(text);

        const row = document.createElement('div');
        row.className = 'log-line log-level-' + level;
        row.setAttribute('data-log-level', level);

        const badge = document.createElement('span');
        badge.className = 'log-badge';
        badge.textContent = t(level);

        const body = document.createElement('span');
        body.className = 'log-text';
        body.textContent = text;

        row.appendChild(badge);
        row.appendChild(body);
        return row;
    }

    /// Satirin ham metni (rozet metni haric). Artimlı tazelemede karsilastirma buna gore yapilir.
    function logLineText(row) {
        const body = row.querySelector ? row.querySelector('.log-text') : null;
        return (body || row).textContent;
    }

    /// Log satirlarini verilen kaba (element) basar; ANSI temizler, seviyelendirir.
    function renderLogLines(container, lines) {
        const fragment = document.createDocumentFragment();
        lines.forEach(function (raw) { fragment.appendChild(buildLogLine(raw)); });
        container.appendChild(fragment);
    }

    /// Sunucunun ilk render'da bastigi duz satirlari ayni rozetli yapiya cevirir.
    function decorateLogViewer(container) {
        Array.prototype.forEach.call(container.querySelectorAll('.log-line'), function (el) {
            if (el.querySelector('.log-text')) return;   // zaten rozetli
            el.replaceWith(buildLogLine(el.textContent));
        });
    }

    /// Kullanici logun sonunu mu izliyor? (Yeni satir gelince kaydirma buna gore yapilir.)
    function atLogBottom(container, slack) {
        return container.scrollHeight - container.scrollTop - container.clientHeight <= (slack || 24);
    }

    /// <summary>
    /// Gelen satirlarin kacinin zaten ekranda oldugunu bulur: mevcut listenin **sonu** ile
    /// gelen listenin **basi** ne kadar ortusuyor? Uc, son N satiri dondurdugu icin pencere
    /// kaydikca bas taraf degisir; bu yuzden salt onek karsilastirmasi yetmez.
    /// Ortusme yoksa null doner (log dondurulmus/degismis: bastan cizilmeli).
    /// </summary>
    function overlapLength(existing, incoming) {
        const max = Math.min(existing.length, incoming.length);

        for (let k = max; k > 0; k--) {
            let same = true;
            for (let i = 0; i < k; i++) {
                if (existing[existing.length - k + i] !== incoming[i]) {
                    same = false;
                    break;
                }
            }
            if (same) return k;
        }

        return null;
    }

    /// <summary>
    /// Log kutusunu **bosaltmadan** tazeler: yalnizca yeni satirlar eklenir. Boylece
    /// periyodik tazelemede icerik "kaybolup gelmez" ve kullanicinin kaydirma konumu korunur
    /// (sonu izliyorsa asagida tutulur). Ortusme bulunamazsa (rotasyon, kaynak degisimi)
    /// bastan cizer. Donen deger: { added, redrawn }.
    /// </summary>
    function syncLogLines(container, lines) {
        const incoming = (lines || []).map(cleanAnsi);
        const rows = container.querySelectorAll('.log-line');

        if (rows.length === 0) {
            renderLogLines(container, incoming);
            container.scrollTop = container.scrollHeight;
            return { added: incoming.length, redrawn: true };
        }

        const existing = Array.prototype.map.call(rows, logLineText);
        const stick = atLogBottom(container);
        const overlap = overlapLength(existing, incoming);

        if (overlap === null) {
            container.replaceChildren();
            renderLogLines(container, incoming);
            container.scrollTop = container.scrollHeight;
            return { added: incoming.length, redrawn: true };
        }

        const fresh = incoming.slice(overlap);
        if (fresh.length > 0) {
            renderLogLines(container, fresh);
            if (stick) container.scrollTop = container.scrollHeight;
        }

        return { added: fresh.length, redrawn: false };
    }

    // 95 -> "1d 35sn", 3725 -> "1s 2d"
    // Gecersiz deger (NaN / sonsuz) "NaNs NaNd" gibi bir cikti uretmesin: tire doner.
    // NaN tipik olarak ayrıştirilamayan bir tarihten gelir (bkz. data-elapsed-from).
    function formatDuration(totalSeconds) {
        if (totalSeconds === null || totalSeconds === undefined) return '—';
        if (!Number.isFinite(Number(totalSeconds))) return '—';
        const s = Math.max(0, Math.floor(totalSeconds));
        if (s < 60) return s + 'sn';
        const minutes = Math.floor(s / 60);
        const seconds = s % 60;
        if (minutes < 60) return minutes + 'd ' + seconds + 'sn';
        return Math.floor(minutes / 60) + 's ' + (minutes % 60) + 'd';
    }

    function formatTime(iso) {
        if (!iso) return '—';
        // Bicim kullanicinin diline gore; sabit 'tr-TR' yok.
        return new Date(iso).toLocaleString(document.documentElement.lang || undefined, {
            day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit'
        });
    }

    function relativeTime(iso) {
        if (!iso) return '—';
        const seconds = (Date.now() - new Date(iso).getTime()) / 1000;
        if (seconds < 60) return t('just now');
        if (seconds < 3600) return Math.floor(seconds / 60) + ' ' + t('min ago');
        if (seconds < 86400) return Math.floor(seconds / 3600) + ' ' + t('h ago');
        return Math.floor(seconds / 86400) + ' ' + t('d ago');
    }

    // data-elapsed-from="<iso>" tasiyan elemanlari her saniye tazeler.
    function startElapsedTicker() {
        setInterval(function () {
            document.querySelectorAll('[data-elapsed-from]').forEach(function (el) {
                const from = el.getAttribute('data-elapsed-from');
                if (!from) return;
                el.textContent = formatDuration((Date.now() - new Date(from).getTime()) / 1000);
            });
        }, 1000);
    }

    function setConnectionStatus(text, cssClass) {
        const el = document.getElementById('connection-status');
        if (!el) return;
        el.textContent = text;
        el.className = 'badge ' + cssClass;
    }

    return {
        escapeHtml: escapeHtml,
        statusBadge: statusBadge,
        formatDuration: formatDuration,
        formatTime: formatTime,
        relativeTime: relativeTime,
        startElapsedTicker: startElapsedTicker,
        setConnectionStatus: setConnectionStatus,
        meta: meta,
        cleanAnsi: cleanAnsi,
        logLevel: logLevel,
        renderLogLines: renderLogLines,
        decorateLogViewer: decorateLogViewer,
        syncLogLines: syncLogLines,
        atLogBottom: atLogBottom,
        t: t
    };
})();

// ------------------------------------------------------------------ Tema secimi
// Tercih cerezde tutulur ve sunucu ilk render'da data-bs-theme yazar (flash olmaz).
// "Sistem" secildiginde isletim sisteminin tercihi izlenir ve degisince aninda uygulanir.
window.dmTheme = (function () {
    const cookieName = 'dm.theme';
    const media = window.matchMedia('(prefers-color-scheme: light)');

    function readPreference() {
        const match = document.cookie.match(/(?:^|;\s*)dm\.theme=([^;]+)/);
        return match ? decodeURIComponent(match[1]) : 'System';
    }

    function resolve(preference) {
        if (preference === 'Light') return 'light';
        if (preference === 'Dark') return 'dark';
        return media.matches ? 'light' : 'dark';
    }

    function apply(preference) {
        document.documentElement.setAttribute('data-bs-theme', resolve(preference));

        document.querySelectorAll('[data-theme-option]').forEach(function (el) {
            el.classList.toggle('active', el.getAttribute('data-theme-option') === preference);
        });
    }

    function set(preference) {
        // 1 yil; sunucu tarafi da ayni cerezi okur.
        document.cookie = cookieName + '=' + encodeURIComponent(preference)
            + ';path=/;max-age=' + (60 * 60 * 24 * 365) + ';samesite=lax';
        apply(preference);
    }

    function init() {
        apply(readPreference());

        // Sistem tercihi degisirse (or. gece moduna gecis) otomatik uy.
        media.addEventListener('change', function () {
            if (readPreference() === 'System') apply('System');
        });

        document.querySelectorAll('[data-theme-option]').forEach(function (el) {
            el.addEventListener('click', function (event) {
                event.preventDefault();
                set(el.getAttribute('data-theme-option'));
            });
        });
    }

    return { init: init, set: set, readPreference: readPreference };
})();

// data-confirm tasiyan formlar gonderilmeden once onay ister. Metin sunucudan
// yerelleştirilmis olarak gelir; inline confirm() yerine attribute kullanilmasi
// cevirideki kesme isaretlerinin JS'i bozmasini engeller.
function wireConfirmForms() {
    document.addEventListener('submit', function (event) {
        const form = event.target.closest('form[data-confirm]');
        if (!form) return;
        if (!window.confirm(form.getAttribute('data-confirm'))) event.preventDefault();
    });
}

document.addEventListener('DOMContentLoaded', function () {
    window.dm.startElapsedTicker();
    window.dmTheme.init();
    wireConfirmForms();
});
