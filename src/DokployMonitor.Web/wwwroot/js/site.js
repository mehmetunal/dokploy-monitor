// Ortak yardimcilar: bicimlendirme, durum rozetleri ve canli gecen-sure sayaclari.
window.dm = (function () {
    // Ceviriler sunucudan gelir (window.dmI18n); anahtar bulunamazsa Ingilizce kaynak metin.
    function t(key, arg) {
        const dict = window.dmI18n || {};
        const text = dict[key] || key;
        return arg === undefined ? text : text.replace('{0}', arg);
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

    // Log satirini kabaca siniflandirir: hata satirlari goz taramasinda one cikmali.
    function classifyLogLine(line) {
        const lower = line.toLowerCase();
        if (lower.includes('error') || lower.includes('failed') || lower.includes('fatal') || lower.includes('hata')) {
            return 'log-line log-error';
        }
        if (lower.includes('warn')) return 'log-line log-warn';
        if (lower.includes('successfully') || lower.includes('success') || lower.includes('done')) {
            return 'log-line log-success';
        }
        return 'log-line';
    }

    /// Log satirlarini verilen kaba (element) basar; ANSI temizler, siniflandirir.
    function renderLogLines(container, lines) {
        const fragment = document.createDocumentFragment();

        lines.forEach(function (raw) {
            const line = cleanAnsi(raw);
            const div = document.createElement('div');
            div.className = classifyLogLine(line);
            div.textContent = line;
            fragment.appendChild(div);
        });

        container.appendChild(fragment);
    }

    // 95 -> "1d 35sn", 3725 -> "1s 2d"
    function formatDuration(totalSeconds) {
        if (totalSeconds === null || totalSeconds === undefined) return '—';
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
        classifyLogLine: classifyLogLine,
        renderLogLines: renderLogLines,
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
