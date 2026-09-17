(() => {
    if (window.__libraryCleanupMenuInstalled) return;
    window.__libraryCleanupMenuInstalled = true;

    let pendingItemId = null;
    let pendingItem = null;

    function currentApiClient() {
        return window.ApiClient || (typeof ApiClient !== 'undefined' ? ApiClient : null);
    }

    function itemIdFromUrl() {
        const match = window.location.href.match(/[?&#](?:id|itemId)=([0-9a-f-]{16,})/i);
        return match ? match[1] : null;
    }

    function itemIdFromButton(button) {
        const card = button?.closest?.('.card[data-id]');
        if (card?.getAttribute('data-id')) return card.getAttribute('data-id');

        const holder = button?.closest?.('[data-itemid], [data-item-id]');
        if (holder) {
            return holder.getAttribute('data-itemid') || holder.getAttribute('data-item-id');
        }

        return itemIdFromUrl();
    }

    function notify(message, kind = 'normal', sticky = false) {
        let toast = document.getElementById('libraryCleanupToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'libraryCleanupToast';
            toast.style.position = 'fixed';
            toast.style.left = '50%';
            toast.style.bottom = '2.2rem';
            toast.style.transform = 'translateX(-50%)';
            toast.style.zIndex = '999999';
            toast.style.maxWidth = 'min(92vw, 720px)';
            toast.style.padding = '.8rem 1rem';
            toast.style.borderRadius = '10px';
            toast.style.boxShadow = '0 8px 28px rgba(0,0,0,.35)';
            toast.style.fontSize = '.95rem';
            toast.style.lineHeight = '1.4';
            toast.style.textAlign = 'center';
            toast.style.pointerEvents = 'none';
            document.body.appendChild(toast);
        }

        toast.textContent = message;
        toast.style.background = kind === 'error' ? 'rgba(127,29,29,.96)' : kind === 'good' ? 'rgba(20,83,45,.96)' : 'rgba(17,24,39,.96)';
        toast.style.color = '#fff';
        toast.style.display = 'block';

        window.clearTimeout(toast.__libraryCleanupTimer);
        if (!sticky) {
            toast.__libraryCleanupTimer = window.setTimeout(() => {
                toast.style.display = 'none';
            }, 7000);
        }
    }

    function closeActionSheet(dialog) {
        if (!dialog) return;
        dialog.classList.add('hide');
        dialog.dispatchEvent(new CustomEvent('_close', {
            bubbles: false,
            cancelable: false
        }));
    }

    async function runScanAndClean(dialog, item) {
        closeActionSheet(dialog);
        const apiClient = currentApiClient();
        if (!apiClient) {
            notify('Scan & Clean could not find Jellyfin\'s API client.', 'error');
            return;
        }

        notify(`Scan & Clean: scanning ${item.Name || item.Type}…`, 'normal', true);

        try {
            const headers = {};
            const token = typeof apiClient.accessToken === 'function' ? apiClient.accessToken() : null;
            if (token) headers['X-Emby-Token'] = token;

            const url = apiClient.getUrl(`LibraryCleanup/api/items/${encodeURIComponent(item.Id)}/scan-clean`);
            const response = await fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers
            });

            const text = await response.text();
            let result = null;
            try { result = text ? JSON.parse(text) : null; } catch { /* ignore */ }

            if (!response.ok) {
                throw new Error(result?.message || result?.detail || result?.title || text || `Scan & Clean failed (${response.status})`);
            }

            const issues = Number(result?.IssuesFound ?? result?.issuesFound ?? 0);
            const repaired = Number(result?.RepairedCount ?? result?.repairedCount ?? 0);
            const remaining = Number(result?.RemainingIssues ?? result?.remainingIssues ?? 0);
            const episodes = Number(result?.EpisodesScanned ?? result?.episodesScanned ?? 0);
            const seasons = Number(result?.SeasonsScanned ?? result?.seasonsScanned ?? 0);

            const scope = item.Type === 'Episode'
                ? '1 episode'
                : `${seasons} season${seasons === 1 ? '' : 's'}, ${episodes} episode${episodes === 1 ? '' : 's'}`;

            notify(
                `Scan & Clean finished for ${item.Name || item.Type}: scanned ${scope}; found ${issues} issue${issues === 1 ? '' : 's'}, repaired ${repaired}, ${remaining} remaining. Jellyfin refresh queued.`,
                remaining === 0 ? 'good' : 'normal');
        } catch (error) {
            console.error('[Library Cleanup] Scan & Clean failed', error);
            notify(`Scan & Clean failed: ${error?.message || error}`, 'error');
        }
    }

    function createMenuButton(dialog, item) {
        if (dialog.querySelector('[data-library-cleanup-action="scan-clean"]')) return;

        const reference = dialog.querySelector('.actionSheetMenuItem[data-id="refresh"]')
            || dialog.querySelector('.actionSheetMenuItem[data-id="identify"]')
            || dialog.querySelector('.actionSheetMenuItem');
        if (!reference) return;

        const button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('is', 'emby-button');
        button.setAttribute('data-library-cleanup-action', 'scan-clean');
        button.className = reference.className.replace(/\bactionSheetMenuItem\b/g, '').trim() + ' libraryCleanupMenuItem';
        button.innerHTML = '<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons cleaning_services" aria-hidden="true"></span>' +
            '<div class="listItemBody actionsheetListItemBody"><div class="listItemBodyText actionSheetItemText">Scan &amp; Clean</div></div>';

        button.addEventListener('click', (event) => {
            event.preventDefault();
            event.stopPropagation();
            runScanAndClean(dialog, item);
        });

        reference.parentNode.insertBefore(button, reference);
    }

    async function tryEnhanceActionSheet(dialog) {
        if (!dialog || dialog.dataset.libraryCleanupChecked === 'true') return;
        if (!dialog.querySelector('.actionSheetMenuItem')) return;
        if (!dialog.querySelector('[data-id="refresh"], [data-id="identify"], [data-id="edit"]')) return;

        dialog.dataset.libraryCleanupChecked = 'true';
        const itemId = pendingItemId || itemIdFromUrl();
        if (!itemId) return;

        const apiClient = currentApiClient();
        if (!apiClient || typeof apiClient.getItem !== 'function' || typeof apiClient.getCurrentUserId !== 'function') return;

        try {
            const item = await apiClient.getItem(apiClient.getCurrentUserId(), itemId);
            if (!dialog.isConnected || !item) return;
            if (!['Series', 'Season', 'Episode'].includes(item.Type)) return;

            pendingItem = item;
            createMenuButton(dialog, item);
        } catch (error) {
            console.debug('[Library Cleanup] Could not resolve context-menu item', error);
        }
    }

    document.addEventListener('click', (event) => {
        const button = event.target?.closest?.('.btnMoreCommands');
        if (!button) return;

        pendingItemId = itemIdFromButton(button);
        pendingItem = null;
    }, true);

    const observer = new MutationObserver(() => {
        document.querySelectorAll('.actionSheet').forEach((dialog) => {
            void tryEnhanceActionSheet(dialog);
        });
    });

    observer.observe(document.documentElement, {
        childList: true,
        subtree: true
    });
})();
