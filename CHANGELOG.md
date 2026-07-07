# Changelog

All notable changes to Link Audit are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

This package versions **per Umbraco major**: `17.x` targets Umbraco 17, `18.x` targets Umbraco 18.
A single change set therefore ships as a matched pair of versions (e.g. `17.5.0` **and** `18.0.0`),
built against each major. **Install the version matching your Umbraco major.**

## [Unreleased]

### Fixed
- Backoffice **Installed packages** now shows the real package version. `umbraco-package.json`'s
  `version` is stamped from the build version at pack time (`17.5.0` / `18.0.0`), instead of being
  hardcoded to `1.0.0` independent of the NuGet version.

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

[Unreleased]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/compare/v18.0.0...HEAD
[17.5.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v17.5.0
[18.0.0]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v18.0.0
[1.0.0-beta.1]: https://github.com/ChalmersLibrary/Umbraco.Community.LinkAudit/releases/tag/v1.0.0-beta.1
