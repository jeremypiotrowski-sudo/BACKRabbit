---
paths:
  - "knowledge-base/**"
  - "docs/**"
  - "**/*.md"
---
# Documentation Rules

- Documentation is append-only unless explicitly asked to update.
- `knowledge-base/` contains operational and reference material, not source-of-truth architecture.
- The architecture source of truth is `.clinerules/00-identity.md`.
- The current build/test state source of truth is `.clinerules/02-current-state.md`.
- When updating docs, cross-check against the actual codebase and current-state file to avoid stale information.