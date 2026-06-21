#!/usr/bin/env python3
"""Extract ALL Magisk native boot source files from git mirror into knowledge-base.
Only v25.0+ have native/src/boot/ - earlier versions used Java/shell scripts.
"""

import subprocess
import os
import sys

GIT_DIR = os.path.join("magisk-versions", "magisk-mirror")
OUTPUT_DIR = "."

# Source files to extract (relative to repo root)
SOURCE_FILES = [
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
    "native/src/boot/sign.rs",
]

def get_tags():
    """Get all version tags sorted newest first, excluding manager-* tags."""
    result = subprocess.run(
        ["git", "--git-dir", GIT_DIR, "tag", "--sort=-version:refname"],
        capture_output=True, text=True, cwd=OUTPUT_DIR
    )
    tags = []
    for line in result.stdout.strip().split("\n"):
        line = line.strip()
        if line and (line.startswith("v") and "." in line) or line.startswith("canary-"):
            tags.append(line)
    return tags

def extract_file(tag, filepath):
    """Extract a single file from a specific tag. Returns content or None."""
    ref = f"{tag}:{filepath}"
    result = subprocess.run(
        ["git", "--git-dir", GIT_DIR, "show", ref],
        capture_output=True, text=True, cwd=OUTPUT_DIR
    )
    if result.returncode != 0:
        return None
    content = result.stdout
    # Check for 404 placeholder
    if content.strip() == "404: Not Found":
        return None
    if not content.strip():
        return None
    return content

def main():
    tags = get_tags()
    print(f"Found {len(tags)} version tags to process")
    
    extracted = 0
    skipped = 0
    not_found = 0
    
    for tag in tags:
        safe_name = tag.replace("/", "_")
        tag_extracted = 0
        
        for filepath in SOURCE_FILES:
            filename = os.path.basename(filepath)
            output_path = os.path.join(OUTPUT_DIR, f"{safe_name}_{filename}")
            
            # Skip if already exists and has proper content (multi-line)
            if os.path.exists(output_path):
                with open(output_path, "r", encoding="utf-8") as f:
                    existing = f.read(100)
                # If it's a single-line mess (no newlines in first 100 chars), re-extract
                if "\n" not in existing and len(existing) > 80:
                    print(f"  Re-extracting corrupted: {output_path}")
                else:
                    skipped += 1
                    continue
            
            content = extract_file(tag, filepath)
            if content is None:
                not_found += 1
                continue
            
            # Write with proper newlines preserved
            with open(output_path, "w", encoding="utf-8", newline="") as f:
                f.write(content)
            
            extracted += 1
            tag_extracted += 1
        
        if tag_extracted > 0:
            print(f"  {tag}: extracted {tag_extracted} files")
    
    print(f"\n{'='*50}")
    print(f"Extraction complete!")
    print(f"  Extracted: {extracted}")
    print(f"  Skipped (already exist): {skipped}")
    print(f"  Not found (expected for older versions): {not_found}")
    print(f"{'='*50}")

if __name__ == "__main__":
    main()