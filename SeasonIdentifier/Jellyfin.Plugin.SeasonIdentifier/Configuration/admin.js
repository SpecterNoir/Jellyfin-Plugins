const SeasonIdentifierAdmin = (() => {
    const state = {
        root: null,
        localSeason: null,
        externalTitle: null,
        localTimer: null,
        titleTimer: null
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
        const headers = { ...authHeaders(), ...(options.headers || {}) };
        let body = options.body;
        if (body !== undefined && body !== null) {
            headers['Content-Type'] = 'application/json';
            body = JSON.stringify(body);
        }

        const response = await fetch(resolveUrl('SeasonIdentifier/api/' + String(path).replace(/^\/+/, '')), {
            method,
            credentials: 'same-origin',
            headers,
            body
        });

        if (!response.ok) {
            const text = await response.text();
            let message = `${method} failed (${response.status})`;
            try {
                const parsed = JSON.parse(text);
                message = parsed.message || parsed.detail || parsed.title || message;
            } catch {
                if (text && text.length < 400) message = text;
            }
            throw new Error(message);
        }

        if (response.status === 204) return null;
        const text = await response.text();
        return text ? normalize(JSON.parse(text)) : null;
    }

    function status(message, kind = '') {
        const el = byId('si-status');
        if (!el) return;
        el.textContent = message || '';
        el.className = 'si-status' + (kind ? ' ' + kind : '');
    }

    function renderSelection() {
        const target = byId('si-selection');
        const save = byId('si-save');
        if (!target || !save) return;

        if (!state.localSeason || !state.externalTitle) {
            target.innerHTML = 'Choose a local season and an external title.';
            save.disabled = true;
            return;
        }

        const local = state.localSeason;
        const remote = state.externalTitle;
        target.innerHTML =
            `<strong>${escapeHtml(local.seriesName)} — ${escapeHtml(local.seasonName || ('Season ' + local.seasonNumber))}</strong>` +
            `<div class="si-muted">will use metadata from</div>` +
            `<strong>${escapeHtml(remote.name)}${remote.year ? ' (' + escapeHtml(remote.year) + ')' : ''}</strong>` +
            `<small class="si-muted">${escapeHtml(remote.searchProviderName || 'Configured metadata provider')}</small>`;
        save.disabled = false;
    }

    async function loadLocalSeasons(query = '') {
        const results = await request('seasons?q=' + encodeURIComponent(query) + '&limit=250');
        const host = byId('si-local-results');
        if (!host) return;

        if (!results?.length) {
            host.innerHTML = '<div class="si-muted">No seasons found.</div>';
            return;
        }

        host.innerHTML = results.map((item, index) =>
            `<button class="si-result${state.localSeason?.id === item.id ? ' is-selected' : ''}" data-local-index="${index}">` +
                `<strong>${escapeHtml(item.seriesName)}</strong>` +
                `<small>${escapeHtml(item.seasonName || ('Season ' + item.seasonNumber))}` +
                `${item.mapped ? ' · already mapped' : ''}</small>` +
            `</button>`
        ).join('');

        host.querySelectorAll('[data-local-index]').forEach((button) => {
            button.addEventListener('click', () => {
                state.localSeason = results[Number(button.dataset.localIndex)];
                loadLocalSeasons(byId('si-local-search')?.value || '').catch(showError);
                renderSelection();
            });
        });
    }

    async function searchTitles(query) {
        const host = byId('si-title-results');
        if (!host) return;

        if (!query || query.trim().length < 2) {
            host.innerHTML = '<div class="si-muted">Type at least two characters.</div>';
            return;
        }

        host.innerHTML = '<div class="si-muted">Searching…</div>';
        const results = await request('search-titles?q=' + encodeURIComponent(query.trim()) + '&limit=25');

        if (!results?.length) {
            host.innerHTML = '<div class="si-muted">No matching titles found.</div>';
            return;
        }

        host.innerHTML = results.map((item, index) =>
            `<button class="si-result" data-title-index="${index}">` +
                `<strong>${escapeHtml(item.name)}${item.year ? ' (' + escapeHtml(item.year) + ')' : ''}</strong>` +
                `<small>${escapeHtml(item.searchProviderName || '')}</small>` +
            `</button>`
        ).join('');

        host.querySelectorAll('[data-title-index]').forEach((button) => {
            button.addEventListener('click', () => {
                state.externalTitle = results[Number(button.dataset.titleIndex)];
                host.querySelectorAll('.si-result').forEach((x) => x.classList.remove('is-selected'));
                button.classList.add('is-selected');
                renderSelection();
            });
        });
    }

    async function saveMapping() {
        if (!state.localSeason || !state.externalTitle) return;

        const remote = state.externalTitle;
        const providerIds = Object.entries(remote.providerIds || {}).map(([key, value]) => ({ key, value }));

        status('Saving mapping and queueing metadata refresh…');
        await request('mappings', {
            method: 'POST',
            body: {
                localSeasonId: state.localSeason.id,
                externalTitleName: remote.name,
                externalYear: remote.year,
                searchProviderName: remote.searchProviderName,
                imageUrl: remote.imageUrl,
                providerIds
            }
        });

        status('Mapping saved. Jellyfin is refreshing the season and its episodes.', 'good');
        await loadMappings();
        await loadLocalSeasons(byId('si-local-search')?.value || '');
    }

    async function loadMappings() {
        const mappings = await request('mappings');
        const host = byId('si-mappings');
        if (!host) return;

        if (!mappings?.length) {
            host.innerHTML = '<div class="si-muted">No mappings yet.</div>';
            return;
        }

        host.innerHTML = mappings.map((item, index) =>
            `<div class="si-map">` +
                `<div>` +
                    `<strong>${escapeHtml(item.localSeriesName)} — ${escapeHtml(item.localSeasonName || ('Season ' + item.localSeasonNumber))}</strong>` +
                    `<small>→ ${escapeHtml(item.externalTitleName)}${item.externalYear ? ' (' + escapeHtml(item.externalYear) + ')' : ''} · Entire title</small>` +
                `</div>` +
                `<div class="si-actions">` +
                    `<button class="si-secondary" data-refresh-index="${index}">Refresh</button>` +
                    `<button class="si-secondary si-danger" data-delete-index="${index}">Remove</button>` +
                `</div>` +
            `</div>`
        ).join('');

        host.querySelectorAll('[data-refresh-index]').forEach((button) => {
            button.addEventListener('click', async () => {
                try {
                    const mapping = mappings[Number(button.dataset.refreshIndex)];
                    await request('mappings/' + encodeURIComponent(mapping.localSeasonId) + '/refresh', { method: 'POST' });
                    status('Refresh queued for ' + mapping.localSeriesName + ' — ' + mapping.localSeasonName + '.', 'good');
                } catch (error) {
                    showError(error);
                }
            });
        });

        host.querySelectorAll('[data-delete-index]').forEach((button) => {
            button.addEventListener('click', async () => {
                try {
                    const mapping = mappings[Number(button.dataset.deleteIndex)];
                    await request('mappings/' + encodeURIComponent(mapping.localSeasonId), { method: 'DELETE' });
                    status('Mapping removed. Existing downloaded metadata was left in place.', 'good');
                    await loadMappings();
                    await loadLocalSeasons(byId('si-local-search')?.value || '');
                } catch (error) {
                    showError(error);
                }
            });
        });
    }

    function showError(error) {
        console.error(error);
        status(error?.message || String(error), 'error');
    }

    function bind() {
        byId('si-local-search')?.addEventListener('input', (event) => {
            window.clearTimeout(state.localTimer);
            state.localTimer = window.setTimeout(() => loadLocalSeasons(event.target.value).catch(showError), 220);
        });

        byId('si-title-search')?.addEventListener('input', (event) => {
            window.clearTimeout(state.titleTimer);
            state.titleTimer = window.setTimeout(() => searchTitles(event.target.value).catch(showError), 320);
        });

        byId('si-save')?.addEventListener('click', () => saveMapping().catch(showError));
    }

    async function init(view) {
        state.root = view;
        bind();
        renderSelection();

        try {
            await Promise.all([
                loadLocalSeasons(''),
                loadMappings()
            ]);
        } catch (error) {
            showError(error);
        }
    }

    return {
        show: init,
        viewshow: init,
        destroy: () => {
            window.clearTimeout(state.localTimer);
            window.clearTimeout(state.titleTimer);
            state.root = null;
        }
    };
})();

export default SeasonIdentifierAdmin;
