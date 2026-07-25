// Deployment detay ekraninda canli log akisi.
// Sunucu, log dosyasini SignalR stream'i uzerinden satir satir gonderir.
(function () {
    const config = window.__logStream;
    const viewer = document.getElementById('log-viewer');
    const statusEl = document.getElementById('log-status');
    const autoscroll = document.getElementById('autoscroll');

    if (!config || !viewer) return;

    // Docker/build ciktilarindaki ANSI renk kodlari (or. \x1b[31m) temizlenir.
    const ansiPattern = /\x1B\[[0-9;?]*[ -/]*[@-~]/g;

    function classify(line) {
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

    function append(lines) {
        const fragment = document.createDocumentFragment();

        lines.forEach(function (raw) {
            const line = raw.replace(ansiPattern, '');
            const div = document.createElement('div');
            div.className = classify(line);
            div.textContent = line;
            fragment.appendChild(div);
        });

        viewer.appendChild(fragment);

        if (autoscroll && autoscroll.checked) {
            viewer.scrollTop = viewer.scrollHeight;
        }
    }

    // Sunucudan gelen ilk (statik) satirlari da temizle ve renklendir.
    Array.prototype.forEach.call(viewer.querySelectorAll('.log-line'), function (el) {
        const text = el.textContent.replace(ansiPattern, '');
        el.textContent = text;
        el.className = classify(text);
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

    connection.start().then(function () {
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
    });
})();
