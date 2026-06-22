# BACKRabbit Firehose — Build & Deployment Guide

> **Purpose**: Enable reproduction of artifacts — critical for offline maintenance.
> **Generated**: 2026-06-21 | **Commit**: `9d328fe`

---

## Producing a Distributable EXE (Windows)

### Prerequisites (One-Time Setup)
- .NET 8.0 SDK
- Qualcomm USB Drivers (for EDL testing — not needed for build)

### Build Steps

```powershell
# 1. Restore NuGet packages
dotnet restore

# 2. Build all projects
dotnet build -c Release

# 3. Run tests
dotnet test -c Release

# 4. Publish self-contained single-file EXE
dotnet publish BACKRabbit.CLI/BACKRabbit.CLI.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o ./dist

# 5. Verify EXE runs
dist\backrabbit.exe --help
```

### Expected Output

```
dist/backrabbit.exe          (~45MB self-contained)
dist/Loaders/                 (Firehose programmer ELF files)
```

---

## Project Dependency Graph

```
BACKRabbit.CLI (EXE)
├── BACKRabbit.MagiskCore     ← Boot image parser, repacker, CPIO
│   └── SharpCompress 0.37.2
├── BACKRabbit.Protocol.Firehose ← EDL/Sahara/Firehose + Rescue
│   ├── BACKRabbit.Usb        ← LibUsbDotNet 2.2.8
│   ├── BACKRabbit.MagiskCore
│   └── System.IO.Ports 8.0.0
├── BACKRabbit.Protocol.Adb
├── BACKRabbit.Protocol.Fastboot
├── BACKRabbit.Protocol.DownloadMode
├── BACKRabbit.Firmware       ← .tar.md5 extraction + FUS sourcing
│   └── SharpCompress 0.38.0
├── Spectre.Console 0.49.0    ← TUI framework
├── System.CommandLine 2.0.0-beta4.22272.1
└── Serilog 5.0.1
```

---

## NuGet Packages

| Package | Version | Used By | Purpose |
|---------|---------|---------|---------|
| SharpCompress | 0.37.2 | MagiskCore | .tar.md5 extraction, compression |
| SharpCompress | 0.38.0 | Firmware | .tar.md5 extraction |
| LibUsbDotNet | 2.2.8 | Usb, Protocols | USB device enumeration |
| Spectre.Console | 0.49.0 | CLI | TUI (tables, prompts, progress bars) |
| System.CommandLine | 2.0.0-beta4.22272.1 | CLI | CLI argument parsing |
| System.IO.Ports | 8.0.0 | Firehose | COM port transport (Windows) |
| xUnit | 2.5.3 | Tests | Unit testing |
| coverlet.collector | 6.0.0 | Tests | Code coverage |

---

## Critical Build Stick Points

### 1. Native Libraries: LibUsbDotNet

Firehose uses `LibUsbDotNet` → requires `libusb-1.0.dll` at runtime.

**Fix**: `PublishSingleFile=true` with `IncludeNativeLibrariesForSelfExtract=true` extracts it automatically.

**Verification**: Check `dist/` for `libusb-1.0.dll` alongside the EXE.

### 2. System.IO.Ports Is Windows-Only

`WinUsbTransport.cs` guards serial port usage with `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`.

**Linux/macOS fallback**: Uses `FileStream` on `/dev/tty*` devices.

**Build warning**: NU1701 for LibUsbDotNet (targets .NET Framework 4.6.1, works on .NET 8 via compatibility shim). This is expected and harmless.

### 3. Trimming

ILLinker breaks reflection in Spectre.Console. Do NOT enable aggressive trimming.

**Safe setting**: `<TrimMode>copyused</TrimMode>` in Release configuration (not currently set — EXE is ~45MB).

### 4. Strong Naming

Avoid if distributing EXE — causes load delays. Current projects do not use strong naming (`<SignAssembly>false</SignAssembly>` is default).

---

## Creating an Offline NuGet Mirror

```powershell
# Download ALL packages once with internet
dotnet nuget locals all --clear

# Mirror critical packages
dotnet nuget mirror System.CommandLine `
  SharpCompress `
  Spectre.Console `
  LibUsbDotNet `
  System.IO.Ports `
  xunit `
  --source https://api.nuget.org/v3/index.json `
  --output ./knowledge_base/nuget_mirror `
  --packages `
    System.CommandLine:2.0.0-beta4.22272.1 `
    SharpCompress:0.37.2 `
    SharpCompress:0.38.0 `
    Spectre.Console:0.49.0 `
    LibUsbDotNet:2.2.8 `
    System.IO.Ports:8.0.0 `
    xunit:2.5.3

# Now builds work 100% offline using:
dotnet restore --configfile ./knowledge_base/nuget_mirror/NuGet.Config
```

---

## Test Suite

### Running Tests

```powershell
# All tests
dotnet test -c Release

# Firehose-specific (32 tests)
dotnet test BACKRabbit.Protocol.Firehose.Tests -c Release

# MagiskCore tests (includes pre-existing failures)
dotnet test BACKRabbit.Tests -c Release

# Specific test filter
dotnet test BACKRabbit.Tests -c Release --filter "VendorBoot"
dotnet test BACKRabbit.Tests -c Release --filter "FirmwareSourcer"
```

### Test Status (as of 9d328fe)

| Suite | Total | Passed | Failed | Skipped |
|-------|-------|--------|--------|---------|
| Firehose + Rescue | 32 | 32 | 0 | 0 |
| FirmwareSourcer auth | 3 | 3 | 0 | 0 |
| FUS query/download | 2 | 0 | 0 | 2 (env var) |
| BACKRabbit.Tests | 55 | 39 | 16 | 0 |

**Pre-existing failures**: 6 F966U1 (missing firmware zip), 2 synthetic vendor boot (test-design), 1 ChromeOS (test-design), 7 others (test-data issues). Zero production code bugs remain.

---

## CI/CD (GitHub Actions)

Workflow: `.github/workflows/` — build, test, CodeQL.

```yaml
# Simplified — actual workflow in repo
steps:
  - uses: actions/checkout@v4
  - uses: actions/setup-dotnet@v4
    with: { dotnet-version: '8.0.x' }
  - run: dotnet restore
  - run: dotnet build -c Release --no-restore
  - run: dotnet test -c Release --no-restore
  - uses: github/codeql-action/analyze
```

---

## Version Identification

```powershell
# Get commit hash
git rev-parse --short HEAD

# Get build timestamp
dotnet build -c Release -p:SourceRevisionId=$(git rev-parse --short HEAD)

# CLI version
dist\backrabbit.exe --version
```

Current: `9d328fe` (2026-06-21)

---

## Cross-References

- **Operations manual**: See `OPERATIONS.md` for Sahara handshake and Firehose XML commands
- **Failure modes**: See `FAILURES.md` for 15 documented failure modes with recovery procedures
- **Design rationale**: See `DESIGN.md` for 11 design decisions including MagiskCore↔Firehose handoff
- **MagiskCore build**: See `../OFFLINE_AGENT_GUIDE.md` for MagiskCore architecture and file ranking
- **Health check**: See `../HEALTH_CHECK.md` for system validation commands