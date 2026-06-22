---
paths:
  - "**/*Tests*.cs"
  - "**/*Test*.cs"
---
# Testing Rules

- Build before committing: `dotnet build BACKRabbit.slnx` must pass with 0 errors.
- Report exact test counts after any code change (total, passed, failed, skipped).
- When fixing protocol parsing, add tests with REAL response data, not only mocks.
- Do not modify test assertions to match broken behavior. Fix the code.
- Pre-existing failures must be explicitly called out in the report.