# Link Audit for Umbraco

A lightweight, single-purpose **link auditor** for Umbraco 17+ (new backoffice). It scans your
published content on a schedule — or on demand — and surfaces, in a read-only backoffice dashboard:

- **Broken external links** (404 / 410, plus timeouts and other unverifiable responses as warnings).
- **Flagged-host links** — absolute links that point at a host you would rather keep internal
  (e.g. `*.umbraco.io`, or your own public domain), which usually means an editor pasted an absolute
  URL where an internal link picker should have been used.

Unlike the SEO-suite crawlers, Link Audit does **not** crawl your rendered site over HTTP. It reads the
published **content cache** directly and scans each property's raw source value for URLs. That makes it
fast, deterministic on a background thread, and able to run headless — it never depends on a page
rendering correctly.

## Install

```sh
dotnet add package Umbraco.Community.LinkAudit
```

The package is a Razor Class Library — the backoffice dashboard assets ship inside it, so there is
nothing to copy into `App_Plugins`. After install and restart, a **Link Audit** dashboard appears in the
**Content** section (visible to users with Content access).

The first crawl runs shortly after startup; thereafter it runs on the configured interval. Use the
**Rescan now** button on the dashboard to run one immediately.

## Configuration

All settings are optional and bound from a `LinkAudit` section in `appsettings.json`. Sensible defaults
mean it works with no configuration at all.

```jsonc
{
  "LinkAudit": {
    "Enabled": true,
    "RootDocumentTypeAlias": "",          // empty = crawl every content root; set to restrict to one root type
    "FlaggedHostPatterns": [ "*.umbraco.io" ], // hosts that should never appear as absolute links ("*." wildcard ok)
    "IgnoredHosts": [],                   // hosts excluded from the audit entirely (never flagged/probed/reported)
    "ExternalCheckEnabled": true,         // HTTP-probe external links for 404s
    "UserAgent": "",                      // empty = "LinkAudit/1.0 (+<your site root>)"
    "IgnoredStatusCodes": [],             // treat these codes as OK (e.g. [401, 403] for login-gated links)
    "ExternalTimeoutSeconds": 10,
    "ExternalConcurrency": 8,
    "IntervalHours": 24,
    "StartupDelayMinutes": 5
  }
}
```

### How the three "suppress" levers differ

The first match wins, in this order:

1. **`IgnoredHosts`** (exact host) — the link is dropped from the audit entirely.
2. **`FlaggedHostPatterns`** (host, `*.` wildcard) — reported as a *Flagged* finding.
3. Otherwise the link is HTTP-probed (when `ExternalCheckEnabled`), and reported only if the probe fails.
   **`IgnoredStatusCodes`** then forgives specific response codes for links you knowingly can't verify
   anonymously.

## How it works

- A recurring background job (`LinkAuditJob`) runs on the scheduling server only, once the runtime is in
  the `Run` state, so the content cache is guaranteed available.
- Both the scheduled job and the manual **Rescan now** button go through a single runner that holds a
  one-at-a-time gate, so crawls can never overlap.
- External links are probed with `HEAD`, falling back to `GET` when a server rejects `HEAD` (405/501) or
  returns a misleading auth/not-found status, before any link is reported as broken.
- The latest report is held in memory (no database schema, no migrations).

## Requirements

- **Umbraco 17+ on .NET 10.** This is a hard floor, not a preference:
  - The dashboard and API use the **new backoffice + Management API** (Umbraco 14+) — they cannot run on Umbraco 13's AngularJS backoffice.
  - Content is read via `IPublishedContentCache.GetByIdAsync`, part of the **Hybrid Cache** (Umbraco 15+).
  - The assembly targets `net10.0`, which only **Umbraco 17** runs on.

  Supporting Umbraco 15/16 would require multi-targeting (`net9.0;net10.0`) with per-target Umbraco
  references; Umbraco 13/14 is out of scope (old backoffice, synchronous cache API).

## License

[MIT](LICENSE)
