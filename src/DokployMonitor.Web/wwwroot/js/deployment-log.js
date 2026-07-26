// Deployment detay ekraninda canli log akisi.
// Sunucu, log dosyasini SignalR stream'i uzerinden satir satir gonderir.
(function () {
    const dm = window.dm;
    const config = window.__logStream;
    const viewer = document.getElementById('log-viewer');
    const statusEl = document.getElementById('log-status');
    const autoscroll = document.getElementById('autoscroll');

    if (!config || !viewer) return;

    function append(lines) {
        dm.renderLogLines(viewer, lines);

        if (autoscroll && autoscroll.checked) {
            viewer.scrollTop = viewer.scrollHeight;
        }
    }

    // Sunucudan gelen ilk (statik) satirlari da temizle ve renklendir.
    Array.prototype.forEach.call(viewer.querySelectorAll('.log-line'), function (el) {
        const text = dm.cleanAnsi(el.textContent);
        el.textContent = text;
        el.className = dm.classifyLogLine(text);
    });

    viewer.scrollTop = viewer.scrollHeight;

    if (!config.live) {
        if (statusEl) statusEl.textContent = dm.t('completed');
        return;
    }

    if (statusEl) statusEl.textContent = dm.t('live stream…');

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/deployments')
        .withAutomaticReconnect()
        .build();

    // Navbar rozetini bu sayfada log akisi yonetir; aksi halde "baglaniyor…" takili kalirdi.
    connection.onreconnecting(function () {
        dm.setConnectionStatus(dm.t('reconnecting…'), 'text-bg-warning');
    });

    connection.onreconnected(function () {
        dm.setConnectionStatus(dm.t('live'), 'text-bg-success');
    });

    connection.onclose(function () {
        dm.setConnectionStatus(dm.t('connection closed'), 'text-bg-secondary');
    });

    connection.start().then(function () {
        dm.setConnectionStatus(dm.t('live'), 'text-bg-success');

        connection.stream('StreamLogs', config.deploymentId, config.offset).subscribe({
            next: function (chunk) {
                if (chunk && chunk.lines && chunk.lines.length) {
                    append(chunk.lines);
                    config.offset = chunk.offset;
                }
            },
            complete: function () {
                if (statusEl) statusEl.textContent = dm.t('stream closed');
            },
            error: function () {
                if (statusEl) statusEl.textContent = dm.t('stream interrupted — refresh the page');
            }
        });
    }).catch(function () {
        if (statusEl) statusEl.textContent = dm.t('could not start live stream');
        dm.setConnectionStatus(dm.t('could not connect'), 'text-bg-warning');
    });
})();
