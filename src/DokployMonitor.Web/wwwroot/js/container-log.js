// Detay ekranindaki "Container Logu" karti: calisan servisin stdout/stderr cikisini
// (docker logs karsiligi) Engine API uzerinden ceker. Build logundan bagimsizdir,
// bu yuzden ilk okuma istege baglidir — her sayfa acilisinda Docker'a istek atilmaz.
//
// Ilk yuklemeden sonra log **kendiliginden** tazelenir (RefreshMs): calisan bir servisin
// cikisi akmaya devam ediyor. Tazeleme artimlidir; kutu bosaltilmaz (dm.syncLogLines).
//
// Docker 404 (redeploy / henuz olusmamis container) gecici olabilir: yoklama durmaz.
(function () {
    const dm = window.dm;
    const config = window.__logStream;
    const viewer = document.getElementById('container-log-viewer');
    const statusEl = document.getElementById('container-log-status');
    const button = document.getElementById('container-log-load');

    if (!config || !viewer || !button) return;

    const RefreshMs = 2000;

    let timer = null;
    let inFlight = false;

    function stopTimer() {
        if (timer !== null) {
            clearInterval(timer);
            timer = null;
        }
    }

    function startTimer() {
        stopTimer();
        timer = setInterval(function () {
            if (!document.hidden) load({ silent: true });
        }, RefreshMs);
    }

    async function load(options) {
        const silent = options && options.silent;
        if (silent && inFlight) return;

        if (!silent) {
            button.disabled = true;
            statusEl.textContent = dm.t('loading…');
            statusEl.className = 'small text-secondary';
        }

        inFlight = true;

        try {
            const response = await fetch(
                '/deployments/' + encodeURIComponent(config.deploymentId) + '/log?source=docker&tail=400',
                { headers: { 'Accept': 'application/json' } });

            if (!response.ok) {
                if (!silent) {
                    statusEl.textContent = dm.t('could not fetch log') + ' (HTTP ' + response.status + ')';
                    statusEl.className = 'small text-warning';
                }
                return;
            }

            const data = await response.json();

            if (!data.available) {
                // Sessiz turda onceki satirlari koru; zamanlayiciyi durdurma.
                statusEl.textContent = (data.unavailableReason || dm.t('Log cannot be read.'))
                    + (silent ? ' · ' + dm.t('reconnecting…') : '');
                statusEl.className = 'small text-warning';
                return;
            }

            if (!data.lines || data.lines.length === 0) {
                statusEl.textContent = dm.t('container has not written any log yet');
                statusEl.className = 'small text-secondary';
                return;
            }

            dm.syncLogLines(viewer, data.lines);

            const rows = viewer.querySelectorAll('.log-line').length;
            statusEl.textContent = dm.t('last {0} lines', rows)
                + (timer !== null ? ' · ' + dm.t('live') : '');
            statusEl.className = 'small text-secondary';
        } catch (e) {
            if (!silent) {
                statusEl.textContent = dm.t('server unreachable');
                statusEl.className = 'small text-warning';
            }
        } finally {
            inFlight = false;
            button.disabled = false;
            button.textContent = dm.t('Refresh');
        }
    }

    button.addEventListener('click', function () {
        load();          // elle tetikleme: durum metni "yukleniyor" olur
        startTimer();    // ve otomatik tazeleme (yeniden) baslar
    });
})();
