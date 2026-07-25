# Changelog

All notable public changes will be documented here. Released artifacts are
immutable; corrections use a new version rather than replacing assets.

## [Unreleased]

- Prepared a standalone repository layout with local tests, CI, validation
  schemas, deterministic release packaging, and public support documents.
- Added regression coverage for full-screen/session sampling suspension.
- Added optional background color and background-only opacity, including an
  always-on-top split mode where normal windows cover the background without
  covering metric text.
- Added configurable linear fades on the left and bottom background edges,
  outside the fully opaque metric area, including a seamless blended corner,
  while preserving solid fill by default.

## [0.1.0] - Unreleased preview

- Initial experimental Desktop System Monitor implementation.
- Prepared as a source-only preview; no official prebuilt or signed binary is
  included.
- Metric-accuracy and 24-hour endurance acceptance remain incomplete.
- The project originated in a private parent workspace and is prepared for a
  fresh public history; the private parent history is not part of this release.
