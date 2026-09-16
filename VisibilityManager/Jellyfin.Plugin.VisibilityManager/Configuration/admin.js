const VisibilityManagerAdmin = (() => {
    const state = {
        root: null,
        searchTimer: null,
        initializedRoot: null
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
        const response = await fetch(resolveUrl('VisibilityManager/api/' + String(path).replace(/^\/+/, '')), {
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
                if (text && text.length < 400) message = text;
            }
            throw new Error(message);
        }

        if (response.status === 204) return null;
        const text = await response.text();
        return text ? normalize(JSON.parse(text)) : null;
    }

    function status(message, kind = '') {
        const el = byId('vm-status');
        if (!el) return;
        el.textContent = message || '';
        el.className = 'vm-status' + (kind ? ' ' + kind : '');
    }

    function describe(item) {
        const bits = [];
        if (item.context) bits.push(item.context);
        if (item.indexNumber !== null && item.indexNumber !== undefined) bits.push('#' + item.indexNumber);
        return bits.join(' · ');
    }

    function itemMarkup(item, index, action) {
        const virtualBadge = item.isVirtual ? '<span class="vm-badge virtual">Virtual item</span>' : '';
        const context = describe(item);
        const buttonText = action === 'hide' ? 'Hide from Jellyfin' : 'Restore visibility';
        const buttonClass = action === 'restore' ? 'vm-action vm-restore' : 'vm-action';
        return `<div class="vm-item">` +
            `<div>` +
                `<strong>${escapeHtml(item.name)}</strong>` +
                `${context ? `<small>${escapeHtml(context)}</small>` : ''}` +
                `<div class="vm-meta"><span class="vm-badge">${escapeHtml(item.type)}</span>${virtualBadge}</div>` +
            `</div>` +
            `<button class="${buttonClass}" data-action="${action}" data-index="${index}">${buttonText}</button>` +
        `</div>`;
    }

    async function loadSearch(query) {
        const host = byId('vm-results');
        if (!host) return;

        const trimmed = String(query || '').trim();
        if (!trimmed) {
            host.innerHTML = '<div class="vm-empty">Type a title, season, or episode name to search.</div>';
            return;
        }

        host.innerHTML = '<div class="vm-empty">Searching…</div>';
        const items = await request('items?q=' + encodeURIComponent(trimmed) + '&limit=100');

        if (!items?.length) {
            host.innerHTML = '<div class="vm-empty">No visible matching items found.</div>';
            return;
        }

        host.innerHTML = items.map((item, index) => itemMarkup(item, index, 'hide')).join('');
        host.querySelectorAll('[data-action="hide"]').forEach((button) => {
            button.addEventListener('click', async () => {
                const item = items[Number(button.dataset.index)];
                button.disabled = true;
                try {
                    status('Changing visibility for ' + item.name + '…');
                    await request('items/' + encodeURIComponent(item.id) + '/hide', { method: 'POST' });
                    status(item.name + ' is now hidden. No media files were changed.', 'good');
                    await Promise.all([
                        loadSearch(byId('vm-search')?.value || ''),
                        loadHidden()
                    ]);
                } catch (error) {
                    button.disabled = false;
                    showError(error);
                }
            });
        });
    }

    async function loadHidden() {
        const host = byId('vm-hidden');
        if (!host) return;

        host.innerHTML = '<div class="vm-empty">Loading…</div>';
        const items = await request('hidden');

        if (!items?.length) {
            host.innerHTML = '<div class="vm-empty">Nothing is currently hidden by this plugin.</div>';
            return;
        }

        host.innerHTML = items.map((item, index) => itemMarkup(item, index, 'restore')).join('');
        host.querySelectorAll('[data-action="restore"]').forEach((button) => {
            button.addEventListener('click', async () => {
                const item = items[Number(button.dataset.index)];
                button.disabled = true;
                try {
                    status('Restoring visibility for ' + item.name + '…');
                    await request('items/' + encodeURIComponent(item.id) + '/restore', { method: 'POST' });
                    status(item.name + ' is visible again. No media files were changed.', 'good');
                    await Promise.all([
                        loadHidden(),
                        loadSearch(byId('vm-search')?.value || '')
                    ]);
                } catch (error) {
                    button.disabled = false;
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
        if (state.initializedRoot === state.root) return;
        state.initializedRoot = state.root;

        byId('vm-search')?.addEventListener('input', (event) => {
            window.clearTimeout(state.searchTimer);
            state.searchTimer = window.setTimeout(
                () => loadSearch(event.target.value).catch(showError),
                220);
        });
    }

    async function init(view) {
        state.root = view;
        bind();
        await Promise.all([
            loadSearch(byId('vm-search')?.value || ''),
            loadHidden()
        ]).catch(showError);
    }

    return {
        show: init,
        viewshow: init,
        destroy: () => {
            window.clearTimeout(state.searchTimer);
            state.root = null;
            state.initializedRoot = null;
        }
    };
})();

export default VisibilityManagerAdmin;
