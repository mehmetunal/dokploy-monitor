// Ortak yardimcilar: bicimlendirme, durum rozetleri ve canli gecen-sure sayaclari.
window.dm = (function () {
    const statusMeta = {
        running: { label: 'calisiyor', css: 'status-running' },
        done: { label: 'basarili', css: 'status-done' },
        error: { label: 'HATA', css: 'status-error' },
        cancelled: { label: 'iptal', css: 'status-cancelled' },
        unknown: { label: 'bilinmiyor', css: 'status-unknown' }
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
        return '<span class="status-badge ' + m.css + '">' + m.label + '</span>';
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
        return new Date(iso).toLocaleString('tr-TR', {
            day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit'
        });
    }

    function relativeTime(iso) {
        if (!iso) return '—';
        const seconds = (Date.now() - new Date(iso).getTime()) / 1000;
        if (seconds < 60) return 'az once';
        if (seconds < 3600) return Math.floor(seconds / 60) + ' dk once';
        if (seconds < 86400) return Math.floor(seconds / 3600) + ' sa once';
        return Math.floor(seconds / 86400) + ' gun once';
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
        renderLogLines: renderLogLines
    };
})();

document.addEventListener('DOMContentLoaded', function () {
    window.dm.startElapsedTicker();
});
