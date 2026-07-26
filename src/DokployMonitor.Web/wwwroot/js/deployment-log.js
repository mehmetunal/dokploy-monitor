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
        if (statusEl) statusEl.textContent = 'tamamlandi';
        return;
    }

    if (statusEl) statusEl.textContent = 'canli akis…';

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/deployments')
        .withAutomaticReconnect()
        .build();

    // Navbar rozetini bu sayfada log akisi yonetir; aksi halde "baglaniyor…" takili kalirdi.
    connection.onreconnecting(function () {
        dm.setConnectionStatus('yeniden baglaniyor…', 'text-bg-warning');
    });

    connection.onreconnected(function () {
        dm.setConnectionStatus('canli', 'text-bg-success');
    });

    connection.onclose(function () {
        dm.setConnectionStatus('baglanti kapandi', 'text-bg-secondary');
    });

    connection.start().then(function () {
        dm.setConnectionStatus('canli', 'text-bg-success');

        connection.stream('StreamLogs', config.deploymentId, config.offset).subscribe({
            next: function (chunk) {
                if (chunk && chunk.lines && chunk.lines.length) {
                    append(chunk.lines);
                    config.offset = chunk.offset;
                }
            },
            complete: function () {
                if (statusEl) statusEl.textContent = 'akis kapandi';
            },
            error: function () {
                if (statusEl) statusEl.textContent = 'akis kesildi — sayfayi yenileyin';
            }
        });
    }).catch(function () {
        if (statusEl) statusEl.textContent = 'canli akis baslatilamadi';
        dm.setConnectionStatus('baglanti kurulamadi', 'text-bg-warning');
    });
})();
