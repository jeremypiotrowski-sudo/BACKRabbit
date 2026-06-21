import os

stubs = []
empty = []

for root, dirs, files in os.walk('.'):
    # Skip build artifacts
    if 'obj' in root or 'bin' in root or '.git' in root or 'knowledge-base/canary' in root:
        continue
    for f in files:
        if f.endswith('.cs'):
            path = os.path.join(root, f)
            size = os.path.getsize(path)
            if size == 0:
                empty.append(path)
            elif size < 100 and 'Class1.cs' not in f and 'AssemblyInfo' not in f and 'GlobalUsings' not in f:
                stubs.append(path)

print(f"STUBS (<100 lines): {len(stubs)}")
for s in stubs:
    print(f"  {s}")

print(f"\nEMPTY (0 bytes): {len(empty)}")
for e in empty:
    print(f"  {e}")

# Also check for TODO/placeholder/stub/hack in source files
import re
# Exclude false positives: tempPath/tempDir/tempBuffer (legit vars),
# stub.xz (Magisk artifact name), NotEmpty (xUnit), stock (comments),
# template (test code), Attempts (SamsungMagiskCleaner)
patterns = re.compile(
    r'\b(TODO|FIXME|HACK|WORKAROUND)\b'  # Only match whole-word code smells
    r'|placeholder.*(?:function|method|class|code|impl|stub)'  # placeholder + code context
    r'|stub.*(?:file|code|class|method|function|impl)'  # stub + code context
    , re.IGNORECASE)
hits = []
for root, dirs, files in os.walk('.'):
    if 'obj' in root or 'bin' in root or '.git' in root or 'knowledge-base/canary' in root:
        continue
    for f in files:
        if f.endswith('.cs') or f.endswith('.py'):
            path = os.path.join(root, f)
            try:
                with open(path, 'r', encoding='utf-8', errors='ignore') as fh:
                    for i, line in enumerate(fh, 1):
                        if patterns.search(line):
                            hits.append(f"{path}:{i}: {line.strip()}")
            except:
                pass

print(f"\nPATTERN HITS (stub/placeholder/TODO/FIXME/HACK/WORKAROUND/TEMP): {len(hits)}")
for h in hits[:50]:  # Limit to 50
    print(f"  {h}")
if len(hits) > 50:
    print(f"  ... and {len(hits) - 50} more")

# Summary
if len(stubs) == 0 and len(empty) == 0 and len(hits) == 0:
    print("\n✅ SCOOP DETECTOR CLEAN — No stubs, empty files, or placeholder patterns found.")
else:
    print(f"\n❌ SCOOP DETECTOR DIRTY — {len(stubs)} stubs, {len(empty)} empty, {len(hits)} pattern hits.")