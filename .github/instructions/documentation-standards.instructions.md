---
applyTo: "**/*.md,**/CHANGELOG.md"
---

# Documentation & Changelog Standards (DigitalMIDP)

Guidance for updating repository documentation and change history.

## Changelog Policy (`CHANGELOG.md`)

- Location: `CHANGELOG.md` at repository root.
- For every verified change, add an entry under:
  `## [YYYY-MM-DD] - <short title>`
- Use standard categorization subsections:
  - `### Added`: for new features or capabilities.
  - `### Changed`: for changes in existing functionality.
  - `### Deprecated`: for soon-to-be removed features.
  - `### Removed`: for removed features.
  - `### Fixed`: for bug fixes.
  - `### Security`: for vulnerabilities or hardened security/auth.

## Documentation Tree (`docs/`)

- Keep docs concise, implementation-true, and synchronized with verified changes.
- Key locations:
  - `docs/architecture/`: ADRs, data models, system design.
  - `docs/integrations/`: APS and Desktop Connector references.
  - `docs/CODING-STANDARDS.md`: C#/.NET 8 conventions.
  - `docs/TESTING.md`: Test strategy.

## Source Code Documentation

- Keep XML doc comments (`/// <summary>`) on public interfaces, use cases, and service contracts accurate and up to date when signatures change.
