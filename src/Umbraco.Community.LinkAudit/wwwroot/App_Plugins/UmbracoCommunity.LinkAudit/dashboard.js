import { LitElement, html, css, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UMB_WORKSPACE_MODAL } from '@umbraco-cms/backoffice/workspace';
import { UmbModalRouteRegistrationController } from '@umbraco-cms/backoffice/router';

const REPORT_URL = '/umbraco/management/api/v1/link-audit/report';
const RESCAN_URL = '/umbraco/management/api/v1/link-audit/rescan';

// The C# enum may serialize as either its name or its numeric index — normalise both.
const KIND_BY_INDEX = ['FlaggedHost', 'BrokenExternal', 'BrokenInternal', 'Warning'];

const KIND_META = {
    FlaggedHost: { label: 'Flagged', color: 'danger' },
    BrokenExternal: { label: 'Broken', color: 'warning' },
    BrokenInternal: { label: 'Broken', color: 'warning' },
    Warning: { label: 'Unverified', color: 'information' },
};

// Sort order for the table: most actionable first.
const KIND_ORDER = ['FlaggedHost', 'BrokenExternal', 'BrokenInternal', 'Warning'];

// Standard reason phrases for the codes that actually surface here, shown inline next to the number.
const STATUS_TEXT = {
    400: 'Bad Request',
    401: 'Unauthorized',
    403: 'Forbidden',
    404: 'Not Found',
    405: 'Method Not Allowed',
    408: 'Request Timeout',
    410: 'Gone',
    418: 'I\'m a teapot',
    429: 'Too Many Requests',
    500: 'Internal Server Error',
    502: 'Bad Gateway',
    503: 'Service Unavailable',
    504: 'Gateway Timeout',
};

class LinkAuditDashboardElement extends UmbElementMixin(LitElement) {
    static get properties() {
        return {
            _loading: { state: true },
            _error: { state: true },
            _report: { state: true },
            _rescanning: { state: true },
            _notice: { state: true },
        };
    }

    constructor() {
        super();
        this._loading = true;
        this._error = null;
        this._report = null;
        this._rescanning = false;
        this._notice = null;
        this._auth = undefined;

        this.consumeContext(UMB_AUTH_CONTEXT, (auth) => {
            this._auth = auth;
            this.#load();
        });
    }

    async #load() {
        if (!this._auth) {
            return;
        }

        this._loading = true;
        this._error = null;

        try {
            const token = await this._auth.getLatestToken();
            const response = await fetch(REPORT_URL, {
                headers: { Authorization: `Bearer ${token}` },
            });

            if (!response.ok) {
                throw new Error(`Request failed (${response.status})`);
            }

            this._report = await response.json();
        } catch (err) {
            this._error = err?.message ?? 'Could not load the link audit report.';
        } finally {
            this._loading = false;
        }
    }

    // Kick off a fresh crawl on demand. The server serialises runs, so a 409 means one is already going.
    async #rescan() {
        if (!this._auth || this._rescanning) {
            return;
        }

        this._rescanning = true;
        this._notice = null;
        this._error = null;

        try {
            const token = await this._auth.getLatestToken();
            const response = await fetch(RESCAN_URL, {
                method: 'POST',
                headers: { Authorization: `Bearer ${token}` },
            });

            if (response.status === 409) {
                this._notice = 'A scan is already running — try again in a moment.';
                return;
            }
            if (!response.ok) {
                throw new Error(`Rescan failed (${response.status})`);
            }

            this._report = await response.json();
        } catch (err) {
            this._error = err?.message ?? 'Could not start a rescan.';
        } finally {
            this._rescanning = false;
        }
    }

    #normalizeKind(kind) {
        if (typeof kind === 'number') {
            return KIND_BY_INDEX[kind] ?? 'Warning';
        }
        return kind ?? 'Warning';
    }

    #sortedFindings() {
        const findings = [...(this._report?.findings ?? [])];
        findings.sort((a, b) => {
            const ka = KIND_ORDER.indexOf(this.#normalizeKind(a.kind));
            const kb = KIND_ORDER.indexOf(this.#normalizeKind(b.kind));
            if (ka !== kb) return ka - kb;
            return (a.pageName ?? '').localeCompare(b.pageName ?? '');
        });
        return findings;
    }

    #renderGeneratedAt() {
        const ts = this._report?.generatedAt;
        // DateTimeOffset.MinValue (year 1) means no crawl has completed yet.
        if (!ts || new Date(ts).getFullYear() <= 1) {
            return html`<em>No scan has run yet. The first scan runs shortly after the site starts.</em>`;
        }
        const when = new Date(ts).toLocaleString();
        const next = this._report.nextScheduledScan;
        return html`Last scanned <strong>${when}</strong> &middot;
            ${this._report.pagesScanned} pages, ${this._report.linksScanned} links checked.${next
                ? html` &middot; Next automatic scan <strong>${new Date(next).toLocaleString()}</strong>.`
                : nothing}`;
    }

    #renderKind(kind) {
        const meta = KIND_META[kind] ?? KIND_META.Warning;
        return html`<uui-tag color=${meta.color} look="primary">${meta.label}</uui-tag>`;
    }

    // HTTP status codes carry meaning most editors won't know by heart, so link the number to the MDN
    // reference and show its reason phrase inline. For non-numeric outcomes (timeout, DNS) show the detail text.
    #renderStatus(f) {
        if (f.httpStatus == null) {
            return f.detail ? html`<span class="status-text" title=${f.detail}>${f.detail}</span>` : html`—`;
        }
        const phrase = STATUS_TEXT[f.httpStatus];
        const mdn = `https://developer.mozilla.org/en-US/docs/Web/HTTP/Status/${f.httpStatus}`;
        const tip = [phrase, f.detail].filter(Boolean).join(' — ') || undefined;
        return html`<a class="status-code" href=${mdn} target="_blank" rel="noopener" title=${tip ?? nothing}>
                <strong>${f.httpStatus}</strong>${phrase ? html` ${phrase}` : nothing}</a>`;
    }

    #renderRow(f) {
        const kind = this.#normalizeKind(f.kind);
        return html`
            <uui-table-row>
                <uui-table-cell>${this.#renderKind(kind)}</uui-table-cell>
                <uui-table-cell>
                    ${f.pageKey
                        ? html`<linkaudit-page-link .unique=${f.pageKey} .label=${f.pageName} .culture=${f.culture ?? ''}></linkaudit-page-link>`
                        : html`${f.pageName}`}
                </uui-table-cell>
                <uui-table-cell>${f.culture || '—'}</uui-table-cell>
                <uui-table-cell>${f.propertyAlias}</uui-table-cell>
                <uui-table-cell class="url">
                    <a href=${f.url} target="_blank" rel="noopener">${f.url}</a>
                </uui-table-cell>
                <uui-table-cell>${this.#renderStatus(f)}</uui-table-cell>
            </uui-table-row>`;
    }

    #renderBody() {
        if (this._loading) {
            return html`<uui-loader></uui-loader>`;
        }

        if (this._error) {
            return html`<div class="msg error">${this._error}</div>`;
        }

        const findings = this.#sortedFindings();
        if (findings.length === 0) {
            return html`<div class="msg ok">✓ No problem links found.</div>`;
        }

        return html`
            <uui-table>
                <uui-table-head>
                    <uui-table-head-cell>Issue</uui-table-head-cell>
                    <uui-table-head-cell>Page</uui-table-head-cell>
                    <uui-table-head-cell>Lang</uui-table-head-cell>
                    <uui-table-head-cell>Property</uui-table-head-cell>
                    <uui-table-head-cell>Link</uui-table-head-cell>
                    <uui-table-head-cell>Status</uui-table-head-cell>
                </uui-table-head>
                ${repeat(findings, (f, i) => `${f.pageKey}|${f.propertyAlias}|${f.url}|${i}`, (f) => this.#renderRow(f))}
            </uui-table>`;
    }

    render() {
        return html`
            <uui-box headline="Link Audit">
                <div slot="header-actions" class="header-actions">
                    <span class="summary">${this.#renderGeneratedAt()}</span>
                    <uui-button
                        look="secondary"
                        label="Rescan now"
                        .disabled=${this._rescanning}
                        @click=${() => this.#rescan()}>
                        ${this._rescanning
                            ? html`Scanning… <uui-loader-circle></uui-loader-circle>`
                            : html`Rescan now`}
                    </uui-button>
                </div>
                <p class="intro">
                    Links in published content that point to a flagged host (ie <code>*.umbraco.io</code> etc)
                    instead of an internal link, plus external links that failed to respond with a valid status code.
                </p>
                ${this._notice ? html`<div class="msg notice">${this._notice}</div>` : nothing}
                ${this.#renderBody()}
            </uui-box>`;
    }

    static get styles() {
        return css`
            :host {
                display: block;
                padding: var(--uui-size-layout-1);
            }
            .intro {
                color: var(--uui-color-text-alt);
                max-width: 60em;
            }
            .header-actions {
                display: flex;
                align-items: center;
                gap: var(--uui-size-space-4);
            }
            .summary {
                font-size: var(--uui-type-small-size);
                color: var(--uui-color-text-alt);
            }
            .msg.notice {
                color: var(--uui-color-text-alt);
                background: var(--uui-color-surface-alt);
                margin-bottom: var(--uui-size-space-4);
            }
            uui-table-cell.url a {
                word-break: break-all;
            }
            a.status-code {
                color: inherit;
                text-decoration: none;
                white-space: nowrap;
            }
            a.status-code:hover {
                text-decoration: underline;
            }
            .status-text {
                color: var(--uui-color-text-alt);
            }
            .msg {
                padding: var(--uui-size-space-5);
                border-radius: var(--uui-border-radius);
            }
            .msg.ok {
                color: var(--uui-color-positive);
                font-weight: bold;
            }
            .msg.error {
                color: var(--uui-color-danger);
            }
        `;
    }
}

customElements.define('linkaudit-dashboard', LinkAuditDashboardElement);

/**
 * A page name that opens the document in a slide-in (sidebar) content editor over the dashboard,
 * instead of navigating away or opening a new tab.
 *
 * Mirrors Umbraco's own <umb-document-item-ref>: registering a modal route for the document workspace
 * turns a plain <a href> into a sidebar modal (the backoffice router intercepts the nav and opens the
 * workspace in a modal rather than replacing the view). One element per row = one registration each,
 * which is how Umbraco's reference lists handle many items at once.
 */
class LinkAuditPageLinkElement extends UmbElementMixin(LitElement) {
    static get properties() {
        return {
            unique: { type: String },
            label: { type: String },
            culture: { type: String },
            _editPath: { state: true },
        };
    }

    constructor() {
        super();
        this.unique = '';
        this.label = '';
        this.culture = '';
        this._editPath = '';

        new UmbModalRouteRegistrationController(this, UMB_WORKSPACE_MODAL)
            .addUniquePaths(['unique'])
            .onSetup(() => ({ data: { entityType: 'document', preset: {} } }))
            .observeRouteBuilder((routeBuilder) => {
                // Base path of the registered modal route; the workspace resolves `edit/<unique>` beneath it.
                this._editPath = routeBuilder({});
            });
    }

    #href() {
        if (!this.unique || !this._editPath) return undefined;
        // The workspace parses a trailing variant fragment (UmbVariantId.toString()) to pick the active
        // language; for a culture-only variant that fragment is just the culture code. Omit it for
        // invariant content so the workspace uses its default.
        const variant = this.culture ? `/${this.culture}` : '';
        return `${this._editPath}/edit/${this.unique}${variant}`;
    }

    render() {
        const href = this.#href();
        return href
            ? html`<a href=${href} title="Open in content editor">${this.label}</a>`
            : html`${this.label}`;
    }
}

customElements.define('linkaudit-page-link', LinkAuditPageLinkElement);

export default LinkAuditDashboardElement;
