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

    // ------------------------------------------------------------------ Log akisi
    // Her satir bir seviyeyle etiketlenir (Dokploy'un deployment akisi gibi): rozet,
    // renkli sol kenar ve soluk arka plan sayesinde 1000+ satirlik build logunda
    // hata/uyari satirlari goz taramasiyla bulunur.
    //
    // Sira onemli: buildkit ciktisi "#22 33.99 ... warning CS8602" gibi satirlari
    // adim onekiyle basladigi icin once "step" sanilabilir; bu yuzden hata ve uyari
    // desenleri adim deseninden once denenir.
    const logLevels = [
        { level: 'error', pattern: /\berror\s+[A-Z]{2}\d+|\berrors?\b|\berr!|\bfatal\b|\bfailed\b|\bfailure\b|\bexception\b|\bpanic\b|non-zero code|\[ERR\]|❌/i },
        { level: 'warning', pattern: /\bwarning\s+[A-Z]{2}\d+|\bwarnings?\b|\bwarn\b|\bdeprecated\b|\[WRN\]|⚠/i },
        { level: 'success', pattern: /successfully|succeeded|\bconverged\b|\bhealthy\b|✓|✅/i },
        { level: 'step', pattern: /^\s*(?:step\s+\d+\s*\/\s*\d+|--->|#\d+\s|\[\d+\/\d+\]|={5,}|-{5,})/i }
    ];

    function logLevel(line) {
        for (let i = 0; i < logLevels.length; i++) {
            if (logLevels[i].pattern.test(line)) return logLevels[i].level;
        }
        return 'info';
    }

    /// Tek bir log satiri: rozet + metin. Metin textContent ile yazilir (HTML kacisi bedava).
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

    /// Kopyalama icin logun duz metni (rozetler haric).
    function logText(container) {
        return Array.prototype.map.call(
            container.querySelectorAll('.log-line'),
            function (el) { return (el.querySelector('.log-text') || el).textContent; }).join('\n');
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
        logLevel: logLevel,
        renderLogLines: renderLogLines,
        decorateLogViewer: decorateLogViewer,
        logText: logText,
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

// ------------------------------------------------------------------- Log araclari
// Her .log-viewer icin: sunucudan gelen satirlari rozetle, satir sayisini yaz,
// "yalniz sorunlar" filtresini uygula. Satirlar SignalR/fetch ile sonradan da
// eklendigi icin kap bir MutationObserver ile izlenir — her render yerine tek yer.
window.dmLogViewers = (function () {
    const dm = window.dm;

    function targetOf(el, attribute) {
        const id = el.getAttribute(attribute);
        return id ? document.getElementById(id) : null;
    }

    function toolbarsFor(viewer, attribute) {
        return document.querySelectorAll('[' + attribute + '="' + viewer.id + '"]');
    }

    function counts(viewer) {
        const rows = viewer.querySelectorAll('.log-line');
        let problems = 0;
        Array.prototype.forEach.call(rows, function (row) {
            const level = row.getAttribute('data-log-level');
            if (level === 'error' || level === 'warning') problems++;
        });
        return { total: rows.length, problems: problems };
    }

    function refresh(viewer) {
        const stats = counts(viewer);

        toolbarsFor(viewer, 'data-log-count-for').forEach(function (el) {
            el.textContent = stats.total === 0 ? '' : dm.t('{0} lines', stats.total);
        });

        // Sorun yoksa filtre kutusu yanlis umut vermesin.
        toolbarsFor(viewer, 'data-log-filter-for').forEach(function (input) {
            input.disabled = stats.problems === 0;
            applyFilter(viewer, input.checked && stats.problems > 0);
        });
    }

    function applyFilter(viewer, onlyProblems) {
        Array.prototype.forEach.call(viewer.querySelectorAll('.log-line'), function (row) {
            const level = row.getAttribute('data-log-level');
            const keep = !onlyProblems || level === 'error' || level === 'warning';
            row.classList.toggle('d-none', !keep);
        });
    }

    async function copy(viewer, button) {
        const text = dm.logText(viewer);
        if (!text) return;

        try {
            // Guvenli olmayan baglamda (http + alan adi) Clipboard API yok: gecici alana yaz.
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
            } else {
                const area = document.createElement('textarea');
                area.value = text;
                area.setAttribute('readonly', '');
                area.style.position = 'fixed';
                area.style.opacity = '0';
                document.body.appendChild(area);
                area.select();
                document.execCommand('copy');
                area.remove();
            }

            const previous = button.textContent;
            button.textContent = dm.t('Copied');
            setTimeout(function () { button.textContent = previous; }, 1500);
        } catch (e) {
            button.textContent = dm.t('Copy');
        }
    }

    function init() {
        document.querySelectorAll('.log-viewer').forEach(function (viewer) {
            dm.decorateLogViewer(viewer);
            refresh(viewer);

            new MutationObserver(function () { refresh(viewer); })
                .observe(viewer, { childList: true });
        });

        document.addEventListener('click', function (event) {
            const button = event.target.closest('[data-log-copy-for]');
            if (!button) return;

            const viewer = targetOf(button, 'data-log-copy-for');
            if (viewer) copy(viewer, button);
        });

        document.addEventListener('change', function (event) {
            const input = event.target.closest('[data-log-filter-for]');
            if (!input) return;

            const viewer = targetOf(input, 'data-log-filter-for');
            if (viewer) applyFilter(viewer, input.checked);
        });
    }

    return { init: init, refresh: refresh };
})();

document.addEventListener('DOMContentLoaded', function () {
    window.dm.startElapsedTicker();
    window.dmTheme.init();
    window.dmLogViewers.init();
    wireConfirmForms();
});
