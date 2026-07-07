# Link Audit for Umbraco

A lightweight, single-purpose **link auditor** for Umbraco 17.5+ and 18 (new backoffice). It scans your
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

Install the LinkAudit version that matches your Umbraco **major** — the package version tracks Umbraco
(LinkAudit `17.x` targets Umbraco 17, `18.x` targets Umbraco 18):

```sh
# Umbraco 17
dotnet add package Umbraco.Community.LinkAudit --version 17.5.0

# Umbraco 18
dotnet add package Umbraco.Community.LinkAudit --version 18.0.0
```

> **Pin the version.** A LinkAudit build is compiled against — and only runtime-compatible with — the
> Umbraco major it targets. NuGet installs the newest version by default, so `dotnet add package` *without*
> `--version` would pull the latest major (e.g. `18.x`) onto a 17 site, where it will fail to resolve or
> run. Always specify the version matching your Umbraco major.

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

- **Umbraco 17.5+ or 18, on .NET 10.** Install the LinkAudit version matching your Umbraco major
  (see [Install](#install)) — there is a separate build per major.
  - The assembly targets `net10.0`, which both **Umbraco 17 and 18** run on.
  - The scheduled crawl derives from `RecurringBackgroundJobBase` and overrides the
    `RunJobAsync(CancellationToken)` method — both **new in Umbraco 17.5.0**. The token is threaded
    through the crawl and every external HTTP probe. On 17.0–17.4 the base type simply doesn't exist,
    so the `17.x` build requires **17.5.0 or later**.

## Author

Built by Rolf Johansson at [Chalmers University of Technology](https://www.chalmers.se),
co-authored with [Claude](https://www.anthropic.com/claude), Anthropic's AI assistant.

## License

[MIT](LICENSE) © 2026 Rolf Johansson, Chalmers University of Technology
