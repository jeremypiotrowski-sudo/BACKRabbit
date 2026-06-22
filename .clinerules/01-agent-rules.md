# Agent Rules — Always Active

## Do NOT

- Do NOT assume `adb.exe` or `fastboot.exe` exist on PATH. Use `AdbClient` and `FastbootClient`.
- Do NOT invent APIs (`ILogger`, `GetAllDevices()`, etc.). Check the codebase first.
- Do NOT suggest Samsung FUS downloads (`firmware source`). The API returns HTTP 403.
- Do NOT treat the rescue pipeline as a Magisk-removal wrapper. NAND normalization is the thesis.
- Do NOT scope-skip hard blockers for easier wins.

## Do

- Build before committing: `dotnet build BACKRabbit.slnx` must pass with 0 errors.
- Report exact test counts after any code change.
- When fixing protocol parsing, add tests with REAL response data, not only mocks.
- Verify independently. Evidence > claims.
- Update `02-current-state.md` when build/test state changes.
- Agents may edit their own `.clinerules` when asked.