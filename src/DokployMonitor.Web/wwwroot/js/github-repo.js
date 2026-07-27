// GitHub repo detayi: branch, PR (open/merged/closed) ve commit gecmisi — 2 sn polling.
(function () {
    const root = document.getElementById('gh-repo-live');
    if (!root) return;

    const dm = window.dm;
    const i18n = window.ghRepoI18n || {};
    function t(key) {
        const text = i18n[key] || (dm && dm.t ? dm.t(key) : key);
        let out = text;
        for (let i = 1; i < arguments.length; i++) {
            out = out.replaceAll('{' + (i - 1) + '}', String(arguments[i]));
        }
        return out;
    }

    const esc = dm && dm.escapeHtml
        ? dm.escapeHtml
        : function (value) {
            return String(value ?? '')
                .replaceAll('&', '&amp;')
                .replaceAll('<', '&lt;')
                .replaceAll('>', '&gt;')
                .replaceAll('"', '&quot;')
                .replaceAll("'", '&#39;');
        };

    const installationId = root.dataset.installationId;
    const owner = root.dataset.owner;
    const name = root.dataset.name;
    const defaultBranch = root.dataset.defaultBranch || 'main';
    const canManage = root.dataset.canManage === 'true';
    const pollMs = Math.max(2000, Number(root.dataset.pollMs) || 2000);

    const branchesBody = document.getElementById('gh-branches-body');
    const branchesTitle = document.getElementById('gh-branches-title');
    const prsBody = document.getElementById('gh-prs-body');
    const commitsBody = document.getElementById('gh-commits-body');
    const commitsBranchSelect = document.getElementById('gh-commits-branch');
    const commitsBranchLabel = document.getElementById('gh-commits-branch-label');
    const liveStatus = document.getElementById('gh-live-status');
    const createCard = document.getElementById('gh-create-branch-card');
    const mergeCard = document.getElementById('gh-merge-branches-card');
    const antiforgery = document.querySelector('input[name="__RequestVerificationToken"]');

    let timer = null;
    let inFlight = false;
    let lastFingerprint = '';
    let activePrTab = 'open';
    let latestPrs = { open: [], merged: [], closed: [] };
    let latestRules = normalizeRules(window.ghRepoInitial && window.ghRepoInitial.rules);
    let latestBranches = [];

    function normalizeRules(raw) {
        raw = raw || {};
        return {
            allowCreateBranch: raw.allowCreateBranch !== false,
            allowMergeBranches: raw.allowMergeBranches !== false,
            allowDeleteBranch: raw.allowDeleteBranch !== false,
            allowedCreateFromBranches: raw.allowedCreateFromBranches || [],
            allowedMergeIntoBranches: raw.allowedMergeIntoBranches || [],
            forbiddenMergeIntoBranches: raw.forbiddenMergeIntoBranches || [],
            protectedFromDeleteBranches: raw.protectedFromDeleteBranches || [],
        };
    }

    function listHas(list, branch) {
        const target = String(branch || '').toLowerCase();
        return (list || []).some(function (item) {
            return String(item || '').toLowerCase() === target;
        });
    }

    function normalizePr(pr) {
        return {
            number: pr.number ?? pr.Number,
            title: pr.title ?? pr.Title ?? '',
            state: pr.state ?? pr.State ?? '',
            headRef: pr.headRef ?? pr.HeadRef ?? '',
            baseRef: pr.baseRef ?? pr.BaseRef ?? '',
            htmlUrl: pr.htmlUrl ?? pr.HtmlUrl ?? '',
            userLogin: pr.userLogin ?? pr.UserLogin ?? '',
            draft: !!(pr.draft ?? pr.Draft),
            merged: !!(pr.merged ?? pr.Merged),
            updatedAt: pr.updatedAt ?? pr.UpdatedAt,
            mergedAt: pr.mergedAt ?? pr.MergedAt,
            closedAt: pr.closedAt ?? pr.ClosedAt,
        };
    }

    function snapshotUrl() {
        const params = new URLSearchParams({
            installationId: installationId,
            owner: owner,
            name: name,
        });
        const branch = commitsBranchSelect ? commitsBranchSelect.value : '';
        if (branch) params.set('commitsBranch', branch);
        return '/GitHub/RepoSnapshot?' + params.toString();
    }

    function fingerprint(data) {
        const branches = (data.branches || []).map(function (b) {
            return [b.name, b.sha, b.isProtected ? 1 : 0].join(':');
        }).join('|');
        function prFp(list) {
            return (list || []).map(function (pr) {
                pr = normalizePr(pr);
                return [pr.number, pr.title, pr.state, pr.merged ? 1 : 0, pr.headRef, pr.baseRef].join(':');
            }).join('|');
        }
        const commits = (data.commits || []).map(function (c) {
            return (c.sha || '') + ':' + (c.message || '');
        }).join('|');
        const rules = normalizeRules(data.rules);
        const rulesFp = [
            rules.allowCreateBranch ? 1 : 0,
            rules.allowMergeBranches ? 1 : 0,
            rules.allowDeleteBranch ? 1 : 0,
            (rules.allowedCreateFromBranches || []).join(','),
            (rules.allowedMergeIntoBranches || []).join(','),
            (rules.forbiddenMergeIntoBranches || []).join(','),
            (rules.protectedFromDeleteBranches || []).join(','),
        ].join(';');
        return [
            branches,
            prFp(data.openPullRequests),
            prFp(data.mergedPullRequests),
            prFp(data.closedPullRequests),
            data.commitsBranch || '',
            commits,
            rulesFp,
        ].join('##');
    }

    function hiddenFields() {
        return ''
            + '<input type="hidden" name="installationId" value="' + esc(installationId) + '" />'
            + '<input type="hidden" name="owner" value="' + esc(owner) + '" />'
            + '<input type="hidden" name="name" value="' + esc(name) + '" />'
            + (antiforgery
                ? '<input type="hidden" name="__RequestVerificationToken" value="' + esc(antiforgery.value) + '" />'
                : '');
    }

    function formatWhen(value) {
        if (!value) return '';
        try {
            const d = new Date(value);
            if (Number.isNaN(d.getTime())) return '';
            return d.toLocaleString();
        } catch (e) {
            return '';
        }
    }

    function canDeleteBranch(branch) {
        const isDefault = branch.name.toLowerCase() === defaultBranch.toLowerCase();
        return canManage
            && latestRules.allowDeleteBranch
            && !isDefault
            && !branch.isProtected
            && !listHas(latestRules.protectedFromDeleteBranches, branch.name);
    }

    function canMergeInto(baseRef) {
        if (!latestRules.allowMergeBranches) return false;
        if (listHas(latestRules.forbiddenMergeIntoBranches, baseRef)) return false;
        if (latestRules.allowedMergeIntoBranches.length
            && !listHas(latestRules.allowedMergeIntoBranches, baseRef)) {
            return false;
        }
        return true;
    }

    function renderBranches(branches) {
        if (!branchesTitle || !branchesBody) return;
        branchesTitle.textContent = t('Branches') + ' (' + branches.length + ')';

        if (!branches.length) {
            branchesBody.innerHTML = '<tr><td colspan="2" class="text-center text-secondary py-3"></td></tr>';
            return;
        }

        branchesBody.innerHTML = branches.map(function (branch) {
            const isDefault = branch.name.toLowerCase() === defaultBranch.toLowerCase();
            const shortSha = (branch.sha || '').slice(0, 7);
            let badges = '';
            if (branch.isProtected) {
                badges += ' <span class="badge text-bg-warning ms-1">' + esc(t('protected')) + '</span>';
            }
            if (isDefault) {
                badges += ' <span class="badge text-bg-success ms-1">' + esc(t('default')) + '</span>';
            }

            let actions = '';
            if (canDeleteBranch(branch)) {
                actions = ''
                    + '<form method="post" action="/GitHub/DeleteBranch" class="m-0 d-inline" data-confirm="'
                    + esc(t("Delete branch '{0}'?", branch.name)) + '">'
                    + hiddenFields()
                    + '<input type="hidden" name="branch" value="' + esc(branch.name) + '" />'
                    + '<button class="btn btn-sm btn-outline-danger" type="submit">' + esc(t('Delete')) + '</button>'
                    + '</form>';
            }

            return ''
                + '<tr><td><code>' + esc(branch.name) + '</code>' + badges
                + '<div class="small text-secondary">' + esc(shortSha) + '</div></td>'
                + '<td class="text-end">' + actions + '</td></tr>';
        }).join('');
    }

    function renderPrList(prs, emptyKey, withActions) {
        if (!prs.length) {
            return '<div class="card-body text-secondary small">' + esc(t(emptyKey)) + '</div>';
        }

        return '<div class="list-group list-group-flush">' + prs.map(function (raw) {
            const pr = normalizePr(raw);
            let actions = '';
            if (withActions && canManage) {
                const showMerge = canMergeInto(pr.baseRef);
                actions = ''
                    + '<div class="d-flex flex-wrap gap-1 align-items-start">'
                    + '<form method="post" action="/GitHub/ApprovePullRequest" class="m-0">'
                    + hiddenFields()
                    + '<input type="hidden" name="number" value="' + esc(pr.number) + '" />'
                    + '<button class="btn btn-sm btn-outline-success" type="submit">' + esc(t('Approve')) + '</button>'
                    + '</form>'
                    + '<form method="post" action="/GitHub/RejectPullRequest" class="m-0" data-confirm="'
                    + esc(t('Request changes on PR #{0}?', pr.number)) + '">'
                    + hiddenFields()
                    + '<input type="hidden" name="number" value="' + esc(pr.number) + '" />'
                    + '<button class="btn btn-sm btn-outline-warning" type="submit">' + esc(t('Request changes')) + '</button>'
                    + '</form>'
                    + (showMerge
                        ? ('<form method="post" action="/GitHub/MergePullRequest" class="m-0" data-confirm="'
                            + esc(t('Merge PR #{0}?', pr.number)) + '">'
                            + hiddenFields()
                            + '<input type="hidden" name="number" value="' + esc(pr.number) + '" />'
                            + '<button class="btn btn-sm btn-primary" type="submit">' + esc(t('Merge')) + '</button>'
                            + '</form>')
                        : '')
                    + '<form method="post" action="/GitHub/ClosePullRequest" class="m-0" data-confirm="'
                    + esc(t('Close PR #{0}?', pr.number)) + '">'
                    + hiddenFields()
                    + '<input type="hidden" name="number" value="' + esc(pr.number) + '" />'
                    + '<button class="btn btn-sm btn-outline-danger" type="submit">' + esc(t('Close')) + '</button>'
                    + '</form>'
                    + '</div>';
            }

            const when = formatWhen(pr.mergedAt || pr.closedAt || pr.updatedAt);
            return ''
                + '<div class="list-group-item"><div class="d-flex flex-wrap gap-2 justify-content-between"><div>'
                + '<a href="' + esc(pr.htmlUrl) + '" target="_blank" rel="noopener" class="fw-semibold text-decoration-none">'
                + '#' + esc(pr.number) + ' ' + esc(pr.title) + '</a>'
                + '<div class="small text-secondary">'
                + esc(pr.userLogin) + ' · <code>' + esc(pr.headRef) + '</code> → <code>' + esc(pr.baseRef) + '</code>'
                + (pr.draft ? ' <span class="badge text-bg-secondary ms-1">' + esc(t('draft')) + '</span>' : '')
                + (when ? ' · ' + esc(when) : '')
                + '</div></div>' + actions + '</div></div>';
        }).join('') + '</div>';
    }

    function renderActivePrTab() {
        if (!prsBody) return;
        const open = latestPrs.open || [];
        const merged = latestPrs.merged || [];
        const closed = latestPrs.closed || [];

        const countOpen = document.getElementById('gh-count-open');
        const countMerged = document.getElementById('gh-count-merged');
        const countClosed = document.getElementById('gh-count-closed');
        if (countOpen) countOpen.textContent = String(open.length);
        if (countMerged) countMerged.textContent = String(merged.length);
        if (countClosed) countClosed.textContent = String(closed.length);

        document.querySelectorAll('[data-gh-pr-tab]').forEach(function (btn) {
            btn.classList.toggle('active', btn.getAttribute('data-gh-pr-tab') === activePrTab);
        });

        if (activePrTab === 'merged') {
            prsBody.innerHTML = renderPrList(merged, 'No merged pull requests.', false);
        } else if (activePrTab === 'closed') {
            prsBody.innerHTML = renderPrList(closed, 'No closed pull requests.', false);
        } else {
            prsBody.innerHTML = renderPrList(open, 'No open pull requests.', true);
        }
        prsBody.dataset.activeTab = activePrTab;
    }

    function renderCommits(commits, branch) {
        if (commitsBranchLabel) commitsBranchLabel.textContent = branch || defaultBranch;
        if (!commitsBody) return;

        if (!commits || !commits.length) {
            commitsBody.innerHTML = '<div class="list-group-item text-secondary small">'
                + esc(t('No commits.')) + '</div>';
            return;
        }

        commitsBody.innerHTML = commits.map(function (c) {
            const sha = (c.sha || '').slice(0, 7);
            const when = formatWhen(c.committedAt);
            return ''
                + '<div class="list-group-item py-2">'
                + '<a href="' + esc(c.htmlUrl) + '" target="_blank" rel="noopener" class="fw-semibold text-decoration-none small">'
                + esc(c.message) + '</a>'
                + '<div class="small text-secondary"><code>' + esc(sha) + '</code> · '
                + esc(c.authorName || c.authorLogin || '')
                + (when ? ' · ' + esc(when) : '')
                + '</div></div>';
        }).join('');
    }

    function fillSelect(select, names, preferDefault) {
        if (!select) return;
        const previous = select.value;
        select.innerHTML = names.map(function (branchName) {
            return '<option value="' + esc(branchName) + '">' + esc(branchName) + '</option>';
        }).join('');

        if (names.includes(previous)) select.value = previous;
        else if (preferDefault && names.includes(defaultBranch)) select.value = defaultBranch;
        else if (names.length) select.value = names[0];
    }

    function syncBranchSelects(branches) {
        const allNames = branches.map(function (b) { return b.name; });
        const createFrom = latestRules.allowedCreateFromBranches.length
            ? allNames.filter(function (n) { return listHas(latestRules.allowedCreateFromBranches, n); })
            : allNames;
        const mergeInto = allNames.filter(function (n) {
            if (listHas(latestRules.forbiddenMergeIntoBranches, n)) return false;
            if (latestRules.allowedMergeIntoBranches.length
                && !listHas(latestRules.allowedMergeIntoBranches, n)) {
                return false;
            }
            return true;
        });

        fillSelect(document.getElementById('gh-from-branch'), createFrom, true);
        fillSelect(document.getElementById('gh-base-branch'), mergeInto, true);
        fillSelect(document.getElementById('gh-head-branch'), allNames, false);

        if (commitsBranchSelect) {
            fillSelect(commitsBranchSelect, allNames, true);
        }
    }

    function applyRulesUi() {
        if (createCard) {
            createCard.style.display = latestRules.allowCreateBranch ? '' : 'none';
        }
        if (mergeCard) {
            mergeCard.style.display = latestRules.allowMergeBranches ? '' : 'none';
        }
    }

    function setLiveStatus(text, css) {
        if (!liveStatus) return;
        liveStatus.textContent = text;
        liveStatus.className = 'badge ' + (css || 'text-bg-secondary');
    }

    function applyData(data) {
        latestRules = normalizeRules(data.rules);
        latestBranches = data.branches || [];
        latestPrs = {
            open: data.openPullRequests || [],
            merged: data.mergedPullRequests || [],
            closed: data.closedPullRequests || [],
        };
        applyRulesUi();
        renderBranches(latestBranches);
        renderActivePrTab();
        syncBranchSelects(latestBranches);
        renderCommits(data.commits || [], data.commitsBranch || (commitsBranchSelect && commitsBranchSelect.value) || defaultBranch);
    }

    async function pollOnce() {
        if (inFlight || document.hidden) return;
        inFlight = true;
        setLiveStatus(t('updating…'), 'text-bg-warning');

        try {
            const response = await fetch(snapshotUrl(), {
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin',
            });
            if (!response.ok) throw new Error('HTTP ' + response.status);

            const data = await response.json();
            const next = fingerprint(data);
            if (next !== lastFingerprint) {
                lastFingerprint = next;
                applyData(data);
            }
            setLiveStatus(t('live'), 'text-bg-success');
        } catch (e) {
            setLiveStatus(t('live refresh error'), 'text-bg-danger');
        } finally {
            inFlight = false;
        }
    }

    function start() {
        if (timer) return;
        timer = setInterval(pollOnce, pollMs);
        pollOnce();
    }

    function stop() {
        if (!timer) return;
        clearInterval(timer);
        timer = null;
    }

    document.querySelectorAll('[data-gh-pr-tab]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            activePrTab = btn.getAttribute('data-gh-pr-tab') || 'open';
            renderActivePrTab();
        });
    });

    if (commitsBranchSelect) {
        commitsBranchSelect.addEventListener('change', function () {
            lastFingerprint = '';
            pollOnce();
        });
    }

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) stop();
        else start();
    });

    const initial = window.ghRepoInitial;
    if (initial) {
        latestRules = normalizeRules(initial.rules);
        latestPrs = {
            open: initial.openPullRequests || [],
            merged: initial.mergedPullRequests || [],
            closed: initial.closedPullRequests || [],
        };
        applyRulesUi();
        renderActivePrTab();
    }

    start();
})();
