// Liste ekranlarindan (pano, gecmis, hata analizi) log onizleme.
// Satirdaki butona basilinca detay sayfasina gitmeden son log satirlari modalde gosterilir.
(function () {
    const dm = window.dm;
    const modalEl = document.getElementById('log-preview-modal');
    if (!modalEl) return;

    const viewer = modalEl.querySelector('#log-preview-viewer');
    const titleEl = modalEl.querySelector('#log-preview-title');
    const statusEl = modalEl.querySelector('#log-preview-status');
    const detailsLink = modalEl.querySelector('#log-preview-details');
    const modal = new bootstrap.Modal(modalEl);

    // Ayni deployment tekrar acilirsa tazeleyebilmek icin son istek hatirlanir.
    let current = null;

    // Kaynak: 'docker' = calisan servisin container logu, 'build' = Dokploy derleme logu.
    let source = 'docker';

    function sourceLabel(value) {
        return value === 'build' ? 'build logu' : 'container logu (docker)';
    }

    function markActiveSource() {
        modalEl.querySelectorAll('[data-log-source]').forEach(function (button) {
            button.classList.toggle('active', button.getAttribute('data-log-source') === source);
        });
    }

    async function load(deploymentId, label) {
        current = { deploymentId: deploymentId, label: label };

        titleEl.textContent = label || deploymentId;
        detailsLink.href = '/Deployments/Details/' + encodeURIComponent(deploymentId);
        viewer.innerHTML = '';
        statusEl.textContent = 'yukleniyor…';
        statusEl.className = 'small text-secondary';
        markActiveSource();

        try {
            const response = await fetch(
                '/deployments/' + encodeURIComponent(deploymentId) + '/log?tail=200&source=' + source,
                { headers: { 'Accept': 'application/json' } });

            if (!response.ok) {
                statusEl.textContent = 'log alinamadi (HTTP ' + response.status + ')';
                statusEl.className = 'small text-warning';
                return;
            }

            const data = await response.json();

            if (!data.available) {
                statusEl.textContent = sourceLabel(data.source) + ': '
                    + (data.unavailableReason || 'Log okunamiyor.');
                statusEl.className = 'small text-warning';
                return;
            }

            if (!data.lines || data.lines.length === 0) {
                statusEl.textContent = 'Log dosyasi bos.';
                statusEl.className = 'small text-secondary';
                return;
            }

            dm.renderLogLines(viewer, data.lines);
            statusEl.textContent = sourceLabel(data.source) + ' · son ' + data.lines.length + ' satir';
            statusEl.className = 'small text-secondary';
            viewer.scrollTop = viewer.scrollHeight;
        } catch (e) {
            statusEl.textContent = 'log alinamadi — sunucuya erisilemiyor';
            statusEl.className = 'small text-warning';
        }
    }

    // Satirlar SignalR ile yeniden olusturuldugu icin olay delegasyonu sart.
    document.addEventListener('click', function (event) {
        const trigger = event.target.closest('[data-log-preview]');
        if (!trigger) return;

        event.preventDefault();
        modal.show();
        load(trigger.getAttribute('data-log-preview'), trigger.getAttribute('data-log-label'));
    });

    modalEl.querySelector('#log-preview-refresh').addEventListener('click', function () {
        if (current) load(current.deploymentId, current.label);
    });

    modalEl.querySelectorAll('[data-log-source]').forEach(function (button) {
        button.addEventListener('click', function () {
            source = button.getAttribute('data-log-source');
            markActiveSource();
            if (current) load(current.deploymentId, current.label);
        });
    });
})();
