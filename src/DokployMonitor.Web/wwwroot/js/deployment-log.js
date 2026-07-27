// Deployment detay ekraninda canli log akisi.
// Birincil yol: SignalR stream'i (sunucu log dosyasini satir satir gonderir).
// Yedek yol: SignalR kurulamaz ya da akis koparsa /deployments/{id}/log ucu her
// RefreshMs'de yoklanir. Iki yolda da guncelleme artimlidir: kutu bosaltilmaz,
// yalnizca yeni satirlar eklenir ve kaydirma konumu korunur (dm.syncLogLines).
(function () {
    const dm = window.dm;
    const config = window.__logStream;
    const viewer = document.getElementById('log-viewer');
    const statusEl = document.getElementById('log-status');
    const autoscroll = document.getElementById('autoscroll');

    if (!config || !viewer) return;

    const RefreshMs = 2000;

    let timer = null;
    let inFlight = false;

    function status(key, cssClass) {
        if (statusEl) {
            statusEl.textContent = dm.t(key);
            statusEl.className = 'small ' + (cssClass || 'text-secondary');
        }
    }

    function append(lines) {
        dm.syncLogLines(viewer, lines);

        if (autoscroll && autoscroll.checked) {
            viewer.scrollTop = viewer.scrollHeight;
        }
    }

    // Sunucudan gelen ilk (statik) satirlar duz metindir: ANSI temizlenir ve
    // akistan gelenlerle ayni rozetli yapiya cevrilir.
    dm.decorateLogViewer(viewer);

    viewer.scrollTop = viewer.scrollHeight;

    if (!config.live) {
        status('completed');
        return;
    }

    // ---------------------------------------------------------------- Yedek: yoklama
    function stopPolling() {
        if (timer !== null) {
            clearInterval(timer);
            timer = null;
        }
    }

    async function poll() {
        if (inFlight) return;
        inFlight = true;

        try {
            const response = await fetch(
                '/deployments/' + encodeURIComponent(config.deploymentId) + '/log?source=build&tail=400',
                { headers: { 'Accept': 'application/json' } });

            if (!response.ok) return;

            const data = await response.json();
            if (data.available && data.lines) {
                append(data.lines);
            }

            // Deployment bitti: build logu artik degismez.
            if (data.live === false) {
                stopPolling();
                status('completed');
            }
        } catch (e) {
            // Gecici ag hatasi: bir sonraki turda tekrar denenir.
        } finally {
            inFlight = false;
        }
    }

    function startPolling() {
        if (timer !== null) return;

        status('fallback mode (polling)', 'text-warning');
        timer = setInterval(function () {
            if (!document.hidden) poll();
        }, RefreshMs);
        poll();
    }

    // ---------------------------------------------------------------- Birincil: SignalR
    status('live stream…');

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/deployments', {
            transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
            withCredentials: true
        })
        .withAutomaticReconnect()
        .build();

    // Navbar rozetini bu sayfada log akisi yonetir; aksi halde "baglaniyor…" takili kalirdi.
    connection.onreconnecting(function () {
        dm.setConnectionStatus(dm.t('reconnecting…'), 'text-bg-warning');
    });

    connection.onreconnected(function () {
        dm.setConnectionStatus(dm.t('live'), 'text-bg-success');
        stopPolling();
    });

    connection.onclose(function () {
        dm.setConnectionStatus(dm.t('connection closed'), 'text-bg-secondary');
        startPolling();   // baglanti kapandiysa yoklama devralir
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
                status('stream closed');
            },
            error: function () {
                // Akis koptu ama sayfa acik: yoklamaya gecip guncellemeye devam ederiz.
                startPolling();
            }
        });
    }).catch(function () {
        dm.setConnectionStatus(dm.t('could not connect'), 'text-bg-warning');
        startPolling();
    });
})();
