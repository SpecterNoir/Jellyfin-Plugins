using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VisibilityManager.Web;

/// <summary>
/// Injects a tiny companion script into Jellyfin Web and serves that script in-memory.
/// No Jellyfin Web files are modified on disk.
/// </summary>
public sealed class VisibilityMenuMiddleware
{
    private const string ScriptFileName = "visibility-manager.js";
    private const string ScriptTag = "<script src=\"visibility-manager.js?v=0.2.0.0\"></script>";

    private static readonly byte[] ScriptBytes = Encoding.UTF8.GetBytes(MenuScript);

    private readonly RequestDelegate _next;
    private readonly ILogger<VisibilityMenuMiddleware> _logger;

    public VisibilityMenuMiddleware(RequestDelegate next, ILogger<VisibilityMenuMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.EndsWith("/web/" + ScriptFileName, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/javascript; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.ContentLength = ScriptBytes.Length;
            await context.Response.Body.WriteAsync(ScriptBytes).ConfigureAwait(false);
            return;
        }

        if (!IsWebIndex(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        var originalAcceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
        using var buffer = new MemoryStream();

        context.Request.Headers.Remove("Accept-Encoding");
        context.Response.Body = buffer;

        try
        {
            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode != StatusCodes.Status200OK)
            {
                buffer.Position = 0;
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                return;
            }

            buffer.Position = 0;
            string html;
            using (var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            if (!html.Contains(ScriptFileName, StringComparison.OrdinalIgnoreCase))
            {
                var bodyIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                html = bodyIndex >= 0
                    ? html.Insert(bodyIndex, ScriptTag)
                    : html + ScriptTag;

                _logger.LogInformation("Visibility Manager injected its Jellyfin Web menu integration.");
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.Body = originalBody;
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.Remove("Last-Modified");
            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;

            if (string.IsNullOrEmpty(originalAcceptEncoding))
            {
                context.Request.Headers.Remove("Accept-Encoding");
            }
            else
            {
                context.Request.Headers.AcceptEncoding = originalAcceptEncoding;
            }
        }
    }

    private static bool IsWebIndex(string path)
        => path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase);

    private const string MenuScript = """
(() => {
    'use strict';

    if (window.__visibilityManagerMenuV2) return;
    window.__visibilityManagerMenuV2 = true;

    const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
    let pending = null;

    function isGuid(value) {
        return guidPattern.test(String(value || '').trim());
    }

    function idFromElement(element) {
        let node = element;
        while (node && node !== document.body) {
            const id = node.getAttribute?.('data-id');
            if (isGuid(id)) return id;
            node = node.parentElement;
        }
        return null;
    }

    function idFromLocation() {
        const match = window.location.href.match(/[?&#]id=([0-9a-f-]{36})(?:[&#]|$)/i);
        return match && isGuid(match[1]) ? match[1] : null;
    }

    function findItemId(button) {
        return idFromElement(button) || idFromLocation();
    }

    function apiUrl(itemId) {
        const relative = `VisibilityManager/api/items/${encodeURIComponent(itemId)}/hide`;
        try {
            if (window.ApiClient && typeof window.ApiClient.getUrl === 'function') {
                return window.ApiClient.getUrl(relative);
            }
        } catch (_) { }

        const pathname = window.location.pathname || '';
        const lower = pathname.toLowerCase();
        const webIndex = lower.lastIndexOf('/web/');
        const base = webIndex >= 0 ? pathname.slice(0, webIndex) : '';
        return `${base}/${relative}`.replace(/\/+/g, '/');
    }

    function authHeaders() {
        const headers = {};
        try {
            if (window.ApiClient && typeof window.ApiClient.accessToken === 'function') {
                const token = window.ApiClient.accessToken();
                if (token) headers['X-Emby-Token'] = token;
            }
        } catch (_) { }
        return headers;
    }

    function removeVisibleCopies(itemId) {
        document.querySelectorAll(`[data-id="${CSS.escape(itemId)}"]`).forEach((element) => {
            const item = element.closest('.card, .listItem');
            if (item) item.remove();
        });
    }

    function closeSheet(sheet) {
        try {
            sheet.dispatchEvent(new CustomEvent('_close', { bubbles: false, cancelable: false }));
        } catch (_) {
            const container = sheet.closest('.dialogContainer');
            if (container) container.remove();
            else sheet.remove();
        }
    }

    function showMessage(message, error = false) {
        const old = document.getElementById('visibility-manager-toast');
        if (old) old.remove();

        const toast = document.createElement('div');
        toast.id = 'visibility-manager-toast';
        toast.textContent = message;
        Object.assign(toast.style, {
            position: 'fixed',
            left: '50%',
            bottom: '2rem',
            transform: 'translateX(-50%)',
            zIndex: '999999',
            maxWidth: 'min(90vw, 42rem)',
            padding: '.8rem 1rem',
            borderRadius: '.6rem',
            background: error ? '#7f1d1d' : '#202020',
            color: '#fff',
            boxShadow: '0 4px 18px rgba(0,0,0,.4)',
            textAlign: 'center'
        });
        document.body.appendChild(toast);
        window.setTimeout(() => toast.remove(), 4200);
    }

    async function removeItem(itemId, sourceButton, sheet, actionButton) {
        const confirmed = window.confirm(
            'Remove this item from Jellyfin?\n\n' +
            'This only hides the Jellyfin library entry. The media file is NOT deleted, moved, renamed, or sent to a recycle bin.'
        );
        if (!confirmed) return;

        actionButton.disabled = true;
        try {
            const response = await fetch(apiUrl(itemId), {
                method: 'POST',
                credentials: 'same-origin',
                headers: authHeaders()
            });

            if (!response.ok) {
                const text = await response.text();
                throw new Error(text || `Request failed (${response.status})`);
            }

            closeSheet(sheet);
            removeVisibleCopies(itemId);
            showMessage('Removed from Jellyfin view. Media files were left untouched.');

            if (sourceButton?.classList?.contains('btnMoreCommands')) {
                window.setTimeout(() => {
                    if (window.history.length > 1) window.history.back();
                }, 150);
            }
        } catch (error) {
            actionButton.disabled = false;
            console.error('[Visibility Manager] Remove failed', error);
            showMessage('Visibility Manager could not remove this item from view.', true);
        }
    }

    function buildRemoveButton(itemId, sourceButton, sheet) {
        const button = document.createElement('button');
        button.setAttribute('is', 'emby-button');
        button.type = 'button';
        button.className = 'listItem listItem-button actionSheetMenuItem visibilityManagerRemove';
        button.setAttribute('data-visibility-manager-remove', itemId);
        button.innerHTML =
            '<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons remove_circle_outline" aria-hidden="true"></span>' +
            '<div class="listItemBody actionsheetListItemBody">' +
                '<div class="listItemBodyText actionSheetItemText">Remove</div>' +
                '<div class="listItemBodyText secondary">Hide from Jellyfin only — files stay untouched</div>' +
            '</div>';

        button.addEventListener('click', (event) => {
            event.preventDefault();
            event.stopPropagation();
            event.stopImmediatePropagation();
            removeItem(itemId, sourceButton, sheet, button);
        }, true);

        return button;
    }

    function injectIntoLatestSheet() {
        if (!pending || Date.now() - pending.time > 3500) return;

        const sheets = Array.from(document.querySelectorAll('.actionSheet.opened, .actionSheet'));
        const sheet = sheets.at(-1);
        if (!sheet || sheet.querySelector('.visibilityManagerRemove')) return;

        const scroller = sheet.querySelector('.actionSheetScroller');
        if (!scroller) return;

        const divider = document.createElement('div');
        divider.className = 'actionsheetDivider visibilityManagerDivider';
        scroller.appendChild(divider);
        scroller.appendChild(buildRemoveButton(pending.itemId, pending.sourceButton, sheet));
    }

    function scheduleInjection() {
        [0, 30, 90, 180, 350].forEach((delay) => window.setTimeout(injectIntoLatestSheet, delay));
    }

    document.addEventListener('click', (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;

        const menuButton = target.closest(
            '.itemAction[data-action="menu"], .btnMoreCommands, .btnCardMenu, .btnCardOptions'
        );
        if (!menuButton) return;

        const itemId = findItemId(menuButton);
        if (!itemId) return;

        pending = {
            itemId,
            sourceButton: menuButton,
            time: Date.now()
        };
        scheduleInjection();
    }, true);

    const observer = new MutationObserver(() => injectIntoLatestSheet());
    observer.observe(document.documentElement, { childList: true, subtree: true });
})();
""";
}
