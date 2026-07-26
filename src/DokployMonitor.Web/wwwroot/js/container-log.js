// Detay ekranindaki "Container Logu" karti: calisan servisin stdout/stderr cikisini
// (docker logs karsiligi) Engine API uzerinden ceker. Build logundan bagimsizdir,
// bu yuzden istege bagli yuklenir — her sayfa acilisinda Docker'a istek atilmaz.
(function () {
    const dm = window.dm;
    const config = window.__logStream;
    const viewer = document.getElementById('container-log-viewer');
    const statusEl = document.getElementById('container-log-status');
    const button = document.getElementById('container-log-load');

    if (!config || !viewer || !button) return;

    async function load() {
        button.disabled = true;
        statusEl.textContent = dm.t('loading…');
        statusEl.className = 'small text-secondary';
        viewer.innerHTML = '';

        try {
            const response = await fetch(
                '/deployments/' + encodeURIComponent(config.deploymentId) + '/log?source=docker&tail=400',
                { headers: { 'Accept': 'application/json' } });

            if (!response.ok) {
                statusEl.textContent = dm.t('could not fetch log') + ' (HTTP ' + response.status + ')';
                statusEl.className = 'small text-warning';
                return;
            }

            const data = await response.json();

            if (!data.available) {
                statusEl.textContent = data.unavailableReason || dm.t('Log cannot be read.');
                statusEl.className = 'small text-warning';
                return;
            }

            if (!data.lines || data.lines.length === 0) {
                statusEl.textContent = dm.t('container has not written any log yet');
                return;
            }

            dm.renderLogLines(viewer, data.lines);
            statusEl.textContent = dm.t('last {0} lines', data.lines.length);
            viewer.scrollTop = viewer.scrollHeight;
        } catch (e) {
            statusEl.textContent = dm.t('server unreachable');
            statusEl.className = 'small text-warning';
        } finally {
            button.disabled = false;
            button.textContent = dm.t('Refresh');
        }
    }

    button.addEventListener('click', load);
})();
