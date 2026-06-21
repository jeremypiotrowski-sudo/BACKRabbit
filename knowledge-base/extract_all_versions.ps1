# Extract ALL Magisk versions from git mirror into knowledge-base
# Usage: powershell -ExecutionPolicy Bypass -File extract_all_versions.ps1

$gitDir = "magisk-versions/magisk-mirror"
$outputDir = "."
$ErrorActionPreference = "Continue"

# Source files to extract (relative to repo root)
$sourceFiles = @(
    "native/src/boot/bootimg.cpp",
    "native/src/boot/bootimg.hpp",
    "native/src/boot/compress.cpp",
    "native/src/boot/compress.hpp",
    "native/src/boot/format.cpp",
    "native/src/boot/format.hpp",
    "native/src/boot/cpio.rs",
    "native/src/boot/dtb.cpp",
    "native/src/boot/dtb.hpp",
    "native/src/boot/magiskboot.hpp",
    "native/src/boot/patch.rs",
    "native/src/boot/payload.rs",
    "native/src/boot/ramdisk.rs",
    "native/src/boot/rootdir.cpp",
    "native/src/boot/rootdir.rs",
    "native/src/boot/selinux.cpp",
    "native/src/boot/selinux.hpp",
    "native/src/boot/sign.rs"
)

# Get all version tags (excluding manager-* tags)
Write-Host "Getting all version tags..."
$tags = git --git-dir="$gitDir" tag --sort=-version:refname | Where-Object { $_ -match '^v\d+\.' -or $_ -match '^canary-\d+' }

Write-Host "Found $($tags.Count) version tags to process"

$extracted = 0
$skipped = 0
$failed = 0

foreach ($tag in $tags) {
    # Clean version name for filename (replace / with _)
    $safeName = $tag -replace '/', '_'
    
    Write-Host "Processing $tag ..." -NoNewline
    
    $tagExtracted = 0
    foreach ($file in $sourceFiles) {
        $fileName = Split-Path $file -Leaf
        $outputPath = Join-Path $outputDir "${safeName}_${fileName}"
        
        # Skip if already exists
        if (Test-Path $outputPath) {
            $skipped++
            continue
        }
        
        try {
            $content = git --git-dir="$gitDir" show "${tag}:${file}" 2>$null
            if ($LASTEXITCODE -eq 0 -and $content) {
                # Check for 404 placeholder
                if ($content -eq "404: Not Found") {
                    $failed++
                    continue
                }
                $content | Out-File -FilePath $outputPath -Encoding utf8 -NoNewline
                $extracted++
                $tagExtracted++
            } else {
                # File doesn't exist in this version - that's fine
                $failed++
            }
        } catch {
            $failed++
        }
    }
    
    if ($tagExtracted -gt 0) {
        Write-Host " extracted $tagExtracted files"
    } else {
        Write-Host " (no new files)"
    }
}

Write-Host ""
Write-Host "============================================"
Write-Host "Extraction complete!"
Write-Host "  Extracted: $extracted"
Write-Host "  Skipped (already exist): $skipped"
Write-Host "  Not found (expected): $failed"
Write-Host "============================================"