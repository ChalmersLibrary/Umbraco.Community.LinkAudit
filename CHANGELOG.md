# Changelog

All notable changes to Link Audit are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

From **2.0.0** this package uses plain semver, independent of Umbraco's version numbers. Supported Umbraco
versions are declared by the NuGet dependency range and shown on the Marketplace — not encoded in the
package version. **Install the latest version; there is nothing to pin.**

Releases before 2.0.0 shipped one binary per Umbraco major, versioned to match it (`17.x`, `18.x`).

## [2.0.0] — 2026-08-14

One package for every supported Umbraco major, and plain semver. Functionally identical to `17.5.0`/`18.0.0`
— this release is about packaging.

### Changed
- **A single build now supports Umbraco 17.5 – 18.x.** Previously there was one binary per major and
  installing required `--version`. Now `dotnet add package Umbraco.Community.LinkAudit` is correct on every
  supported version. Only `IPublishedContent.Name`/`.Cultures` differ between the majors (Umbraco 18 moved
  them to `IPublishedElement`); they are resolved by name at runtime, and every other Umbraco member the
  package uses is declaration-identical across majors.
- **Versioning is now plain semver** and carries no information about Umbraco. Encoding the Umbraco major in
  the version left no digit free for LinkAudit's own fixes: patching two majors meant inventing versions
  like `17.5.0.1`. Compatibility belongs in the dependency range, which can express a range — a version
  number cannot.

### Fixed
- **The declared Umbraco dependency is now bounded** (`[17.5.0, 19.0.0)`). Previous releases declared
  `>= 17.5.0` / `>= 18.0.0` with no upper bound, so the Marketplace advertised them as supporting every
  future Umbraco major, including majors they crash on. CI now fails the build if the range is ever
  unbounded again, and a release is gated on booting a real site against every version in the range.
- Backoffice **Installed packages** now shows the real package version. `umbraco-package.json`'s `version`
  is stamped from the build version at pack time, instead of being hardcoded to `1.0.0`.

### Added
- appsettings IntelliSense/validation for the `LinkAudit` section. The package ships a JSON schema and
  registers it with Umbraco's schema pipeline (`buildTransitive`), so the consuming site auto-copies it and
  adds the `$ref` to `appsettings-schema.json` on build — no manual setup.
- `test/boot-matrix.sh`, which boots a real Umbraco site on each supported version and asserts a full audit
  completes. This is the safety net that replaces the per-major builds: with one binary, NuGet can no longer
  catch a cross-major break at restore time.

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
- A build is runtime-compatible only with the Umbraco major it targets (some interfaces move between
  majors, e.g. `IPublishedContent.Cultures` moved to `IPublishedElement` in 18) — hence the per-major versions.

## [1.0.0-beta.1] — 2026-07-06

- Initial beta release.

[Unreleased]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v2.0.0
[17.5.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v17.5.0
[18.0.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v18.0.0
[1.0.0-beta.1]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v1.0.0-beta.1
