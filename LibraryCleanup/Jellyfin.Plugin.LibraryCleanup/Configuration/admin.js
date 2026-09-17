const LibraryCleanupAdmin = (() => {
    const state = {
        root: null,
        initializedRoot: null,
        scan: null,
        filter: 'all'
    };

    const byId = (id) => state.root?.querySelector('#' + id) || null;

    function normalize(value) {
        if (Array.isArray(value)) return value.map(normalize);
        if (!value || typeof value !== 'object') return value;
        const result = {};
        Object.keys(value).forEach((key) => {
            const normalizedKey = key.length ? key[0].toLowerCase() + key.slice(1) : key;
            result[normalizedKey] = normalize(value[key]);
        });
        return result;
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function resolveUrl(path) {
        const clean = String(path || '').replace(/^\/+/, '');
        if (typeof ApiClient !== 'undefined' && typeof ApiClient.getUrl === 'function') {
            return ApiClient.getUrl(clean);
        }
        return '/' + clean;
    }

    function authHeaders() {
        const headers = {};
        if (typeof ApiClient !== 'undefined' && typeof ApiClient.accessToken === 'function') {
            const token = ApiClient.accessToken();
            if (token) headers['X-Emby-Token'] = token;
        }
        return headers;
    }

    async function request(path, options = {}) {
        const method = options.method || 'GET';
        const response = await fetch(resolveUrl('LibraryCleanup/api/' + String(path).replace(/^\/+/, '')), {
            method,
            credentials: 'same-origin',
            headers: authHeaders()
        });

        if (!response.ok) {
            const text = await response.text();
            let message = `${method} failed (${response.status})`;
            try {
                const parsed = JSON.parse(text);
                message = parsed.message || parsed.detail || parsed.title || message;
            } catch {
                if (text && text.length < 500) message = text;
            }
            throw new Error(message);
        }

        if (response.status === 204) return null;
        const text = await response.text();
        return text ? normalize(JSON.parse(text)) : null;
    }

    function setStatus(message, kind = '') {
        const el = byId('lc-status');
        if (!el) return;
        el.textContent = message || '';
        el.className = 'lc-status' + (kind ? ' ' + kind : '');
    }

    function showError(error) {
        console.error(error);
        setStatus(error?.message || String(error), 'error');
    }

    function filteredIssues() {
        const issues = state.scan?.issues || [];
        if (state.filter === 'all') return issues;
        if (state.filter === 'safe') return issues.filter((issue) => issue.safeRepairAvailable);
        return issues.filter((issue) => issue.severity === state.filter);
    }

    function contextText(issue) {
        return [issue.seriesName, issue.seasonName, issue.itemName]
            .filter(Boolean)
            .join(' / ');
    }

    function issueMarkup(issue, index) {
        const safeButton = issue.safeRepairAvailable
            ? `<button class="lc-action safe" data-action="repair" data-index="${index}">Safe Repair</button>`
            : '';
        const refreshButton = issue.refreshAvailable
            ? `<button class="lc-action secondary" data-action="refresh" data-index="${index}">Full metadata refresh</button>`
            : '';
        const countBadge = issue.relatedItemIds?.length > 1
            ? `<span class="lc-badge">${issue.relatedItemIds.length} related records</span>`
            : '';

        return `<div class="lc-issue">` +
            `<div class="lc-issue-head"><div><strong>${escapeHtml(issue.title)}</strong>` +
            `<div class="lc-context">${escapeHtml(contextText(issue))}</div></div>` +
            `<span class="lc-badge ${escapeHtml(issue.severity)}">${escapeHtml(issue.severity)}</span></div>` +
            `<div class="lc-detail">${escapeHtml(issue.detail)}</div>` +
            `${issue.path ? `<div class="lc-context">${escapeHtml(issue.path)}</div>` : ''}` +
            `<div class="lc-badges"><span class="lc-badge">${escapeHtml(issue.kind)}</span>${countBadge}</div>` +
            `<div class="lc-buttons">${safeButton}${refreshButton}</div>` +
        `</div>`;
    }

    function render() {
        const scan = state.scan;
        if (!scan) return;

        byId('lc-episodes').textContent = scan.episodeCount;
        byId('lc-seasons').textContent = scan.seasonCount;
        byId('lc-issues').textContent = scan.issueCount;
        byId('lc-safe-count').textContent = scan.safeRepairCount;

        const list = byId('lc-list');
        const issues = filteredIssues();
        if (!issues.length) {
            list.innerHTML = '<div class="lc-empty">No issues match this filter.</div>';
            return;
        }

        list.innerHTML = issues.map((issue, index) => issueMarkup(issue, index)).join('');

        list.querySelectorAll('[data-action="repair"]').forEach((button) => {
            button.addEventListener('click', async () => {
                const issue = issues[Number(button.dataset.index)];
                button.disabled = true;
                try {
                    setStatus('Repairing ' + issue.itemName + '…');
                    const result = await request('items/' + encodeURIComponent(issue.itemId) + '/repair-links', { method: 'POST' });
                    setStatus(result?.message || 'Repair completed.', 'good');
                    await scanLibrary(false);
                } catch (error) {
                    button.disabled = false;
                    showError(error);
                }
            });
        });

        list.querySelectorAll('[data-action="refresh"]').forEach((button) => {
            button.addEventListener('click', async () => {
                const issue = issues[Number(button.dataset.index)];
                button.disabled = true;
                try {
                    setStatus('Queued a full Jellyfin metadata refresh for ' + issue.itemName + '…');
                    await request('items/' + encodeURIComponent(issue.itemId) + '/refresh', { method: 'POST' });
                    setStatus('Full metadata and image refresh queued for ' + issue.itemName + '.', 'good');
                } catch (error) {
                    button.disabled = false;
                    showError(error);
                }
            });
        });
    }

    async function scanLibrary(showMessage = true) {
        const list = byId('lc-list');
        const scanButton = byId('lc-scan');
        if (scanButton) scanButton.disabled = true;
        if (list) list.innerHTML = '<div class="lc-empty">Scanning Jellyfin…</div>';
        if (showMessage) setStatus('Scanning seasons and episodes…');

        try {
            state.scan = await request('scan');
            render();
            if (showMessage) {
                const count = state.scan?.issueCount || 0;
                setStatus(count ? `Scan complete: ${count} issue${count === 1 ? '' : 's'} found.` : 'Scan complete: no issues found.', count ? '' : 'good');
            }
        } finally {
            if (scanButton) scanButton.disabled = false;
        }
    }

    function bind() {
        if (state.initializedRoot === state.root) return;
        state.initializedRoot = state.root;

        byId('lc-scan')?.addEventListener('click', () => scanLibrary(true).catch(showError));
        byId('lc-repair-safe')?.addEventListener('click', async (event) => {
            const button = event.currentTarget;
            button.disabled = true;
            try {
                setStatus('Repairing all safe hierarchy-link issues…');
                const result = await request('repair-safe', { method: 'POST' });
                setStatus(result?.message || 'Safe repairs completed.', 'good');
                await scanLibrary(false);
            } catch (error) {
                showError(error);
            } finally {
                button.disabled = false;
            }
        });

        state.root.querySelectorAll('.lc-filter').forEach((button) => {
            button.addEventListener('click', () => {
                state.filter = button.dataset.filter || 'all';
                state.root.querySelectorAll('.lc-filter').forEach((candidate) => candidate.classList.toggle('active', candidate === button));
                render();
            });
        });
    }

    async function init(view) {
        state.root = view;
        bind();
        await scanLibrary(true).catch(showError);
    }

    return {
        show: init,
        viewshow: init,
        destroy: () => {
            state.root = null;
            state.initializedRoot = null;
            state.scan = null;
            state.filter = 'all';
        }
    };
})();

export default LibraryCleanupAdmin;
