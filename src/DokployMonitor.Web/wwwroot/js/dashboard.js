// Canli pano: SignalR ile "dashboard" olayini dinler, baglanti kurulamazsa
// /dashboard/snapshot ucundan periyodik veri cekmeye duser.
(function () {
    const dm = window.dm;
    let pollTimer = null;

    function render(snapshot) {
        if (!snapshot) return;
        renderStats(snapshot.stats);
        renderActive(snapshot.active);
        renderQueue(snapshot.queue, snapshot.queueUnavailableReason);
        renderRecent(snapshot.recent);
        renderNotifications(snapshot.notifications);
    }

    function renderStats(stats) {
        if (!stats) return;
        set('stat-running', stats.runningCount);
        set('stat-queued', stats.queuedCount);
        set('stat-succeeded', stats.succeededLast24H);
        set('stat-failed', stats.failedLast24H);
        set('stat-avg', stats.averageDurationSecondsLast24H == null
            ? '—'
            : dm.formatDuration(stats.averageDurationSecondsLast24H));

        const longest = document.getElementById('stat-longest');
        if (longest) {
            longest.textContent = stats.longestRunningService
                ? stats.longestRunningService + ' · ' + dm.formatDuration(parseTimeSpan(stats.longestRunningElapsed))
                : '—';
        }

        const sync = document.getElementById('sync-info');
        if (sync) {
            sync.textContent = stats.syncError
                ? 'Senkronizasyon hatasi: ' + stats.syncError
                : 'Son senkronizasyon: ' + dm.formatTime(stats.lastSyncAt);
            sync.className = stats.syncError ? 'small text-danger' : 'small text-secondary';
        }
    }

    // "00:04:31.5" gibi TimeSpan metnini saniyeye cevirir.
    function parseTimeSpan(value) {
        if (!value) return 0;
        const parts = String(value).split(':');
        if (parts.length < 3) return 0;
        return (+parts[0]) * 3600 + (+parts[1]) * 60 + Math.floor(parseFloat(parts[2]));
    }

    function renderActive(rows) {
        const body = document.getElementById('active-body');
        if (!body) return;

        if (!rows || rows.length === 0) {
            body.innerHTML = emptyRow(6, 'Su anda calisan deployment yok.');
            return;
        }

        body.innerHTML = rows.map(function (r) {
            const started = r.startedAt || r.createdAt;
            return '<tr class="row-running">' +
                '<td>' + dm.statusBadge(r.status) + '</td>' +
                '<td>' + serviceCell(r) + '</td>' +
                '<td class="text-secondary">' + dm.escapeHtml(r.serviceType) + '</td>' +
                '<td>' + dm.formatTime(started) + '</td>' +
                '<td class="fw-semibold text-info" data-elapsed-from="' + started + '">' +
                    dm.formatDuration((Date.now() - new Date(started).getTime()) / 1000) + '</td>' +
                '<td class="text-end">' + detailsLink(r.deploymentId) + '</td>' +
                '</tr>';
        }).join('');
    }

    function renderQueue(rows, unavailableReason) {
        const body = document.getElementById('queue-body');
        const card = document.getElementById('queue-card');
        if (!body || !card) return;

        if (unavailableReason) {
            body.innerHTML = emptyRow(4, unavailableReason);
            return;
        }

        if (!rows || rows.length === 0) {
            body.innerHTML = emptyRow(4, 'Kuyrukta bekleyen is yok.');
            return;
        }

        body.innerHTML = rows.map(function (r) {
            return '<tr>' +
                '<td><span class="queue-position">' + (r.position || '?') + '</span></td>' +
                '<td>' + dm.escapeHtml(r.serviceLabel) + '</td>' +
                '<td class="text-secondary">' + dm.escapeHtml(r.jobType || '') + '</td>' +
                '<td class="text-secondary">' + dm.relativeTime(r.enqueuedAt) + '</td>' +
                '</tr>';
        }).join('');
    }

    function renderRecent(rows) {
        const body = document.getElementById('recent-body');
        if (!body) return;

        if (!rows || rows.length === 0) {
            body.innerHTML = emptyRow(7, 'Kayit yok.');
            return;
        }

        body.innerHTML = rows.map(function (r) {
            const cssClass = r.status === 'error' || r.status === 'cancelled' ? 'row-failed' : '';
            return '<tr class="' + cssClass + '">' +
                '<td>' + dm.statusBadge(r.status) + '</td>' +
                '<td>' + serviceCell(r) + '</td>' +
                '<td class="text-secondary">' + dm.escapeHtml(r.serviceType) + '</td>' +
                '<td>' + dm.formatTime(r.createdAt) + '</td>' +
                '<td>' + (r.durationSeconds == null ? '—' : dm.formatDuration(r.durationSeconds)) + '</td>' +
                '<td class="error-cell">' + (r.errorSummary ? '<code>' + dm.escapeHtml(r.errorSummary) + '</code>' : '') + '</td>' +
                '<td class="text-end">' + detailsLink(r.deploymentId) + '</td>' +
                '</tr>';
        }).join('');
    }

    function renderNotifications(rows) {
        const list = document.getElementById('notification-list');
        if (!list) return;

        if (!rows || rows.length === 0) {
            list.innerHTML = '<li class="list-group-item text-secondary">Henuz webhook bildirimi gelmedi.</li>';
            return;
        }

        list.innerHTML = rows.map(function (n) {
            const tone = n.status === 'error' ? 'text-danger' : 'text-success';
            return '<li class="list-group-item">' +
                '<div class="d-flex justify-content-between">' +
                    '<span class="' + tone + ' fw-semibold">' + dm.escapeHtml(n.title) + '</span>' +
                    '<span class="text-secondary small">' + dm.relativeTime(n.receivedAt) + '</span>' +
                '</div>' +
                '<div class="small text-secondary">' +
                    dm.escapeHtml([n.projectName, n.applicationName].filter(Boolean).join(' / ')) +
                '</div>' +
                (n.errorMessage ? '<div class="small text-danger text-truncate">' + dm.escapeHtml(n.errorMessage) + '</div>' : '') +
                '</li>';
        }).join('');
    }

    function serviceCell(r) {
        const project = [r.projectName, r.environmentName].filter(Boolean).join(' / ');
        return '<div class="fw-semibold">' + dm.escapeHtml(r.serviceName) +
            (r.isPreview ? ' <span class="badge text-bg-secondary">preview</span>' : '') + '</div>' +
            (project ? '<div class="small text-secondary">' + dm.escapeHtml(project) + '</div>' : '');
    }

    function detailsLink(id) {
        return '<a class="btn btn-sm btn-outline-light" href="/Deployments/Details/' + encodeURIComponent(id) + '">Detay</a>';
    }

    function emptyRow(colspan, text) {
        return '<tr><td colspan="' + colspan + '" class="text-center text-secondary py-4">' + dm.escapeHtml(text) + '</td></tr>';
    }

    function set(id, value) {
        const el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    async function pollOnce() {
        try {
            const response = await fetch('/dashboard/snapshot', { headers: { 'Accept': 'application/json' } });
            if (response.ok) render(await response.json());
        } catch (e) {
            // Sunucu gecici olarak erisilemez; bir sonraki turda tekrar denenir.
        }
    }

    function startPolling() {
        if (pollTimer) return;
        dm.setConnectionStatus('yedek mod (polling)', 'text-bg-warning');
        pollTimer = setInterval(pollOnce, 5000);
    }

    function stopPolling() {
        if (!pollTimer) return;
        clearInterval(pollTimer);
        pollTimer = null;
    }

    document.addEventListener('DOMContentLoaded', function () {
        render(window.__initialSnapshot);

        const connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/deployments')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();

        connection.on('dashboard', function (snapshot) {
            stopPolling();
            render(snapshot);
        });

        connection.onreconnecting(function () {
            dm.setConnectionStatus('yeniden baglaniyor…', 'text-bg-warning');
        });

        connection.onreconnected(function () {
            dm.setConnectionStatus('canli', 'text-bg-success');
            stopPolling();
            pollOnce();
        });

        connection.onclose(function () {
            startPolling();
        });

        connection.start()
            .then(function () { dm.setConnectionStatus('canli', 'text-bg-success'); })
            .catch(function () { startPolling(); });
    });
})();
