# Changelog

All notable changes to Link Audit are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

From **2.0.0** this package uses plain semver, independent of Umbraco's version numbers. Supported Umbraco
versions are declared by the NuGet dependency range and shown on the Marketplace — not encoded in the
package version. **Install the latest version; there is nothing to pin.**

Releases before 2.0.0 shipped one binary per Umbraco major, versioned to match it (`17.x`, `18.x`).

## [Unreleased]

### Changed
- **The default User-Agent now carries the real package version** — `LinkAudit/2.0.1 (+https://example.com)`
  rather than a hardcoded `LinkAudit/1.0`, which had not moved since `1.0`. It is read from the assembly's
  informational version, so it tracks releases by itself. A configured `LinkAudit:UserAgent` is still sent
  verbatim, unchanged.

## [2.0.1] — 2026-08-20

**If you installed `2.0.0`, upgrade.** No configuration changes.

### Fixed
- **The Link Audit dashboard is back in the backoffice.** `2.0.0` was packaged without the manifest Umbraco
  uses to discover a package, so the dashboard never appeared and the package was missing from **Installed
  packages**. Scheduled audits were unaffected and kept running.

> **Maintainer note:** unlist `2.0.0` on nuget.org — a published version can only be superseded, not replaced.

## [2.0.0] — 2026-08-14

> **Do not use: this release is missing its backoffice manifest — see [2.0.1].** Superseded, and unlisted on
> nuget.org.

One package for every supported Umbraco major, and plain semver. Functionally identical to `17.5.0`/`18.0.0`
— this release is about packaging.

### Changed
- **A single build now supports Umbraco 17.5 – 18.x.** Previously there was one binary per major and
  installing required `--version`; now `dotnet add package Umbraco.Community.LinkAudit` is correct on every
  supported version.
- **Versioning is now plain semver** and carries no information about Umbraco — hence `2.0.0` after `18.0.0`.
  Which Umbraco versions are supported is declared by the dependency range, which can express a range; a
  version number cannot.

### Fixed
- **The declared Umbraco dependency is now bounded** (`[17.5.0, 19.0.0)`). Previous releases declared
  `>= 17.5.0` / `>= 18.0.0` with no upper bound, so the Marketplace advertised them as supporting every
  future Umbraco major, including majors they crash on.
- Backoffice **Installed packages** now shows the real package version, instead of always `1.0.0`.

### Added
- appsettings IntelliSense/validation for the `LinkAudit` section — the package ships a JSON schema and
  registers it with Umbraco's schema pipeline, so there is nothing to set up by hand.

### Upgrading
Remove any `--version` pin (or `Version="17.x"` / `"18.x"` in your csproj) and take the latest. No
configuration or content changes are needed.

> **Maintainer note:** `17.5.0` and `18.0.0` must be **unlisted** on nuget.org for `2.x` to resolve as
> latest — NuGet picks the highest *listed* version, and `18.0.0` > `2.0.0`. Unlisting is not deletion:
> anyone pinned to an old version keeps restoring it.

## [17.5.0] / [18.0.0] — 2026-07-07

First stable release. Same feature set on both majors; `17.5.0` targets Umbraco 17.5+, `18.0.0` targets Umbraco 18.

### Added
- Scheduled and on-demand audit of published content for **broken external links** (404/410, with
  timeouts and other unverifiable responses reported as warnings) and **flagged-host links** (absolute
  links to hosts you would rather keep internal, e.g. `*.umbraco.io`).
- Read-only **Link Audit dashboard** in the Content section, with a scan summary and a **Rescan now** button.
- Reads the published content cache directly (no HTTP crawl of the rendered site); scans each property's
  raw source value, so links inside rich-text and block editors are covered.
- Configuration via an optional `LinkAudit` section in `appsettings.json` (flagged/ignored hosts,
  external-check toggle, ignored status codes, timeouts, concurrency, interval, startup delay).
- Latest report held in memory — no database schema, no migrations.

### Notes
- Built for **.NET 10**. The scheduled crawl uses `RecurringBackgroundJobBase.RunJobAsync(CancellationToken)`,
  new in Umbraco 17.5.0, so the `17.x` build requires **17.5.0 or later**.
- A build only runs on the Umbraco major it targets — hence the per-major versions. Install the one matching
  your site, or take `2.0.1` or later, where this no longer applies.

## [1.0.0-beta.1] — 2026-07-06

- Initial beta release.

[Unreleased]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/compare/v2.0.1...HEAD
[2.0.1]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v2.0.1
[2.0.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v2.0.0
[17.5.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v17.5.0
[18.0.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v18.0.0
[1.0.0-beta.1]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v1.0.0-beta.1
