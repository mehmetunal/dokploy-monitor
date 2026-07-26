// Canli pano: SignalR ile "dashboard" olayini dinler, baglanti kurulamazsa
// /dashboard/snapshot ucundan periyodik veri cekmeye duser.
(function () {
    const dm = window.dm;
    let pollTimer = null;

    // Son gelen snapshot: filtre degistiginde sunucuyu beklemeden yeniden cizmek icin.
    let snapshot = null;

    const filterStorageKey = 'dm.dashboard.filter';
    const filter = loadFilter();

    function loadFilter() {
        try {
            return Object.assign(
                { project: '', text: '', onlyFailed: false },
                JSON.parse(localStorage.getItem(filterStorageKey) || '{}'));
        } catch (e) {
            return { project: '', text: '', onlyFailed: false };
        }
    }

    function saveFilter() {
        try {
            localStorage.setItem(filterStorageKey, JSON.stringify(filter));
        } catch (e) {
            // Ozel modda localStorage yazilamaz; filtre sadece bu sekmede yasar.
        }
    }

    function matches(row) {
        if (filter.project && row.projectName !== filter.project) return false;

        if (filter.onlyFailed && row.status !== 'error' && row.status !== 'cancelled') return false;

        if (filter.text) {
            const needle = filter.text.toLowerCase();
            const haystack = [row.serviceName, row.projectName, row.environmentName,
                              row.errorSummary, row.serviceType, row.connectionName]
                .filter(Boolean).join(' ').toLowerCase();
            if (!haystack.includes(needle)) return false;
        }

        return true;
    }

    function render(current) {
        if (!current) return;
        snapshot = current;

        renderStats(snapshot.stats);
        syncProjectOptions();
        renderActive(snapshot.active);
        renderQueue(snapshot.queue, snapshot.queueUnavailableReason);
        renderRecent(snapshot.recent);
        renderNotifications(snapshot.notifications);
        renderFilterInfo();
    }

    /// Proje listesi snapshot'tan turetilir; secili deger korunur.
    function syncProjectOptions() {
        const select = document.getElementById('filter-project');
        if (!select) return;

        const projects = [...new Set(
            [].concat(snapshot.active || [], snapshot.recent || [])
                .map(function (r) { return r.projectName; })
                .filter(Boolean))].sort();

        // Filtrede secili proje su an listede olmasa da secenek olarak kalmali.
        if (filter.project && !projects.includes(filter.project)) projects.push(filter.project);

        const desired = ['<option value="">' + dm.escapeHtml(dm.t('All projects')) + '</option>']
            .concat(projects.map(function (p) {
                return '<option value="' + dm.escapeHtml(p) + '">' + dm.escapeHtml(p) + '</option>';
            })).join('');

        if (select.innerHTML !== desired) select.innerHTML = desired;
        select.value = filter.project;
    }

    function renderFilterInfo() {
        const info = document.getElementById('filter-info');
        if (!info) return;

        const active = (snapshot.active || []);
        const recent = (snapshot.recent || []);
        const total = active.length + recent.length;
        const shown = active.filter(matches).length + recent.filter(matches).length;

        if (!filter.project && !filter.text && !filter.onlyFailed) {
            info.textContent = dm.t('No filter') + ' · ' + total + ' ' + dm.t('records')
                + '. ' + dm.t('The indicators above always cover all projects over 24 hours.');
            return;
        }

        info.textContent = dm.t('Filter active') + ' · ' + shown + '/' + total
            + ' ' + dm.t('shown (indicators are not filtered)');
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
                ? dm.t('Sync error') + ': ' + stats.syncError
                : dm.t('Last sync') + ': ' + dm.formatTime(stats.lastSyncAt);
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

        const visible = (rows || []).filter(matches);

        if (visible.length === 0) {
            body.innerHTML = emptyRow(6, (rows || []).length === 0
                ? dm.t('No deployment is running right now.')
                : dm.t('No running deployment matches the filter.'));
            return;
        }

        body.innerHTML = visible.map(function (r) {
            const started = r.startedAt || r.createdAt;
            return '<tr class="row-running">' +
                '<td>' + dm.statusBadge(r.status) + '</td>' +
                '<td>' + serviceCell(r) + '</td>' +
                '<td class="text-secondary d-none d-lg-table-cell">' + dm.escapeHtml(r.serviceType) + '</td>' +
                '<td class="d-none d-md-table-cell">' + dm.formatTime(started) + '</td>' +
                '<td class="fw-semibold text-info" data-elapsed-from="' + started + '">' +
                    dm.formatDuration((Date.now() - new Date(started).getTime()) / 1000) + '</td>' +
                '<td class="text-end text-nowrap">' + logButton(r) + detailsLink(r.deploymentId) + '</td>' +
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
            body.innerHTML = emptyRow(4, dm.t('No jobs waiting in the queue.'));
            return;
        }

        body.innerHTML = rows.map(function (r) {
            return '<tr>' +
                '<td><span class="queue-position">' + (r.position || '?') + '</span></td>' +
                '<td>' + dm.escapeHtml(r.serviceLabel) +
                    (r.connectionName
                        ? ' <span class="badge text-bg-dark border border-secondary-subtle">' + dm.escapeHtml(r.connectionName) + '</span>'
                        : '') + '</td>' +
                '<td class="text-secondary d-none d-md-table-cell">' + dm.escapeHtml(r.jobType || '') + '</td>' +
                '<td class="text-secondary">' + dm.relativeTime(r.enqueuedAt) + '</td>' +
                '</tr>';
        }).join('');
    }

    function renderRecent(rows) {
        const body = document.getElementById('recent-body');
        if (!body) return;

        const visible = (rows || []).filter(matches);

        if (visible.length === 0) {
            body.innerHTML = emptyRow(7, (rows || []).length === 0
                ? dm.t('No records.')
                : dm.t('No records match the filter.'));
            return;
        }

        body.innerHTML = visible.map(function (r) {
            const cssClass = r.status === 'error' || r.status === 'cancelled' ? 'row-failed' : '';
            return '<tr class="' + cssClass + '">' +
                '<td>' + dm.statusBadge(r.status) + '</td>' +
                '<td>' + serviceCell(r) + '</td>' +
                '<td class="text-secondary d-none d-lg-table-cell">' + dm.escapeHtml(r.serviceType) + '</td>' +
                '<td class="d-none d-md-table-cell">' + dm.formatTime(r.createdAt) + '</td>' +
                '<td>' + (r.durationSeconds == null ? '—' : dm.formatDuration(r.durationSeconds)) + '</td>' +
                '<td class="error-cell d-none d-xl-table-cell">' + (r.errorSummary ? '<code>' + dm.escapeHtml(r.errorSummary) + '</code>' : '') + '</td>' +
                '<td class="text-end text-nowrap">' + logButton(r) + detailsLink(r.deploymentId) + '</td>' +
                '</tr>';
        }).join('');
    }

    function renderNotifications(rows) {
        const list = document.getElementById('notification-list');
        if (!list) return;

        if (!rows || rows.length === 0) {
            list.innerHTML = '<li class="list-group-item text-secondary">' + dm.escapeHtml(dm.t('No webhook notifications yet.')) + '</li>';
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

        // Coklu Dokploy kurulumunda kaydin hangi sunucudan geldigi gorunmeli.
        const connection = r.connectionName
            ? ' <span class="badge text-bg-dark border border-secondary-subtle">' + dm.escapeHtml(r.connectionName) + '</span>'
            : '';

        return '<div class="fw-semibold">' + dm.escapeHtml(r.serviceName) +
            (r.isPreview ? ' <span class="badge text-bg-secondary">preview</span>' : '') + connection + '</div>' +
            (project ? '<div class="small text-secondary">' + dm.escapeHtml(project) + '</div>' : '');
    }

    function detailsLink(id) {
        return '<a class="btn btn-sm btn-outline-secondary" href="/Deployments/Details/'
            + encodeURIComponent(id) + '">' + dm.escapeHtml(dm.t('Details')) + '</a>';
    }

    /// Log dosyasi olan satirlarda onizleme butonu (bkz. log-preview.js).
    function logButton(r) {
        if (!r.hasLog) return '';
        return '<button type="button" class="btn btn-sm btn-outline-info me-1" ' +
            'data-log-preview="' + dm.escapeHtml(r.deploymentId) + '" ' +
            'data-log-label="' + dm.escapeHtml(r.serviceName) + '">' + dm.escapeHtml(dm.t('Log')) + '</button>';
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
        dm.setConnectionStatus(dm.t('fallback mode (polling)'), 'text-bg-warning');
        pollTimer = setInterval(pollOnce, 5000);
    }

    function stopPolling() {
        if (!pollTimer) return;
        clearInterval(pollTimer);
        pollTimer = null;
    }

    function wireFilterControls() {
        const project = document.getElementById('filter-project');
        const text = document.getElementById('filter-text');
        const failed = document.getElementById('filter-failed');
        const clear = document.getElementById('filter-clear');

        if (text) text.value = filter.text;
        if (failed) failed.checked = filter.onlyFailed;

        function changed() {
            saveFilter();
            if (snapshot) render(snapshot);
        }

        if (project) project.addEventListener('change', function () {
            filter.project = project.value;
            changed();
        });

        if (text) text.addEventListener('input', function () {
            filter.text = text.value.trim();
            changed();
        });

        if (failed) failed.addEventListener('change', function () {
            filter.onlyFailed = failed.checked;
            changed();
        });

        if (clear) clear.addEventListener('click', function () {
            filter.project = '';
            filter.text = '';
            filter.onlyFailed = false;
            if (text) text.value = '';
            if (failed) failed.checked = false;
            changed();
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        wireFilterControls();
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
            dm.setConnectionStatus(dm.t('reconnecting…'), 'text-bg-warning');
        });

        connection.onreconnected(function () {
            dm.setConnectionStatus(dm.t('live'), 'text-bg-success');
            stopPolling();
            pollOnce();
        });

        connection.onclose(function () {
            startPolling();
        });

        connection.start()
            .then(function () { dm.setConnectionStatus(dm.t('live'), 'text-bg-success'); })
            .catch(function () { startPolling(); });
    });
})();
