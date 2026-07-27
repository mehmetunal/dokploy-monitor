// Liste ekranlarindan (pano, gecmis, hata analizi) log onizleme.
// Satirdaki butona basilinca detay sayfasina gitmeden son log satirlari modalde gosterilir.
//
// Pencere acikken log **kendiliginden** tazelenir (bkz. RefreshMs): kullanicinin "Yenile"ye
// basmasi gerekmez. Tazeleme artimlidir — kutu bosaltilmaz, yalnizca yeni satirlar eklenir,
// kaydirma konumu korunur (bkz. dm.syncLogLines).
//
// Varsayilan kaynak "auto": once container, yoksa build (sunucu dusumu). Deploy sirasinda
// container henuz yokken 404 gecici olabilir — yoklama durmaz, bir sonraki turda tekrar dener.
(function () {
    const dm = window.dm;
    const modalEl = document.getElementById('log-preview-modal');
    if (!modalEl) return;

    const viewer = modalEl.querySelector('#log-preview-viewer');
    const titleEl = modalEl.querySelector('#log-preview-title');
    const statusEl = modalEl.querySelector('#log-preview-status');
    const detailsLink = modalEl.querySelector('#log-preview-details');
    const modal = new bootstrap.Modal(modalEl);

    /// Otomatik tazeleme araligi.
    const RefreshMs = 2000;

    // Ayni deployment tekrar acilirsa tazeleyebilmek icin son istek hatirlanir.
    let current = null;

    // '' = auto (sunucu docker→build dusumu), 'docker' | 'build' = sabit kaynak.
    let source = '';
    let lastUsedSource = 'docker';

    let timer = null;
    let inFlight = false;
    let requestSeq = 0;

    function sourceLabel(value) {
        return dm.t(value === 'build' ? 'build log' : 'container log (docker)');
    }

    function markActiveSource() {
        const active = source || lastUsedSource;
        modalEl.querySelectorAll('[data-log-source]').forEach(function (button) {
            const value = button.getAttribute('data-log-source');
            // Auto modda sunucunun sectigi kaynagi vurgula.
            button.classList.toggle('active', value === active);
        });
    }

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

    /// silent: periyodik tazeleme (kutu bosaltilmaz, "yukleniyor" yazilmaz).
    async function load(options) {
        if (!current) return;

        const silent = options && options.silent;
        if (silent && inFlight) return;   // yavas yanitta istekler ust uste binmesin

        const requestedId = current.deploymentId;
        const requestedSource = source;
        const seq = ++requestSeq;

        if (!silent) {
            viewer.replaceChildren();
            statusEl.textContent = dm.t('loading…');
            statusEl.className = 'small text-secondary';
            markActiveSource();
        }

        inFlight = true;

        try {
            const sourceQuery = requestedSource ? ('&source=' + encodeURIComponent(requestedSource)) : '';
            const response = await fetch(
                '/deployments/' + encodeURIComponent(requestedId) + '/log?tail=200' + sourceQuery,
                { headers: { 'Accept': 'application/json' } });

            // Kullanici kaynak/deployment degistirdiyse gec kalan yaniti yoksay.
            if (seq !== requestSeq || !current || current.deploymentId !== requestedId || source !== requestedSource) {
                return;
            }

            if (!response.ok) {
                if (!silent) {
                    statusEl.textContent = dm.t('could not fetch log') + ' (HTTP ' + response.status + ')';
                    statusEl.className = 'small text-warning';
                }
                // Gecici ag/proxy hatalari: yoklamayi surdur.
                return;
            }

            const data = await response.json();

            if (seq !== requestSeq || !current || current.deploymentId !== requestedId || source !== requestedSource) {
                return;
            }

            if (data.source) {
                lastUsedSource = data.source;
                markActiveSource();
            }

            if (!data.available) {
                // Sessiz tazelemede onceki satirlari koru; Docker 404 deploy sirasinda
                // sik gecicidir — zamanlayiciyi DURDURMA, Yenile'ye mahkum etme.
                if (silent) {
                    statusEl.textContent = sourceLabel(data.source || lastUsedSource) + ': '
                        + (data.unavailableReason || dm.t('Log cannot be read.'))
                        + ' · ' + dm.t('reconnecting…');
                    statusEl.className = 'small text-warning';
                    return;
                }

                statusEl.textContent = sourceLabel(data.source || lastUsedSource) + ': '
                    + (data.unavailableReason || dm.t('Log cannot be read.'));
                statusEl.className = 'small text-warning';
                return;
            }

            if (!data.lines || data.lines.length === 0) {
                if (!silent) {
                    statusEl.textContent = dm.t('Log file is empty.');
                    statusEl.className = 'small text-secondary';
                }
                return;
            }

            dm.syncLogLines(viewer, data.lines);

            const rows = viewer.querySelectorAll('.log-line').length;
            statusEl.textContent = sourceLabel(data.source) + ' · ' + dm.t('last {0} lines', rows)
                + (timer !== null ? ' · ' + dm.t('live') : '');
            statusEl.className = 'small text-secondary';

            // Bitmis bir build logu artik degismez: yoklamayi burada birakiyoruz.
            // Container logu (veya auto→docker) calisan serviste akmaya devam edebilir.
            if (data.live === false && data.source === 'build') {
                stopTimer();
                statusEl.textContent = sourceLabel(data.source) + ' · ' + dm.t('last {0} lines', rows)
                    + ' · ' + dm.t('completed');
            }
        } catch (e) {
            if (seq !== requestSeq) return;

            if (!silent) {
                statusEl.textContent = dm.t('could not fetch log') + ' — ' + dm.t('server unreachable');
                statusEl.className = 'small text-warning';
            }
            // Ag kopmasi gecici olabilir; zamanlayiciyi acik tut.
        } finally {
            inFlight = false;
        }
    }

    function open(deploymentId, label) {
        current = { deploymentId: deploymentId, label: label };
        source = ''; // her acilista auto: docker yoksa build
        lastUsedSource = 'docker';
        titleEl.textContent = label || deploymentId;
        detailsLink.href = '/Deployments/Details/' + encodeURIComponent(deploymentId);

        load();
        startTimer();
    }

    // Satirlar SignalR ile yeniden olusturuldugu icin olay delegasyonu sart.
    document.addEventListener('click', function (event) {
        const trigger = event.target.closest('[data-log-preview]');
        if (!trigger) return;

        event.preventDefault();
        modal.show();
        open(trigger.getAttribute('data-log-preview'), trigger.getAttribute('data-log-label'));
    });

    // Pencere kapaninca yoklama da dursun.
    modalEl.addEventListener('hidden.bs.modal', function () {
        stopTimer();
        current = null;
    });

    modalEl.querySelector('#log-preview-refresh').addEventListener('click', function () {
        if (!current) return;
        load({ silent: true });   // elle tazelemede de kutu bosalmaz
        startTimer();             // durmussa otomatik tazeleme yeniden baslar
    });

    modalEl.querySelectorAll('[data-log-source]').forEach(function (button) {
        button.addEventListener('click', function () {
            source = button.getAttribute('data-log-source') || '';
            markActiveSource();

            // Kaynak degisti: icerik bastan yuklenir, otomatik tazeleme yeniden kurulur.
            load();
            startTimer();
        });
    });
})();
