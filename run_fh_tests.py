import subprocess
import sys

result = subprocess.run(
    ['dotnet', 'test', 'BACKRabbit.Protocol.Firehose.Tests', '--verbosity', 'normal'],
    capture_output=True, text=True,
    cwd=r'c:\Users\jp\Documents\BACKRabbit'
)

output = result.stdout + '\n' + result.stderr

with open('fh_test_output.txt', 'w', encoding='utf-8') as f:
    f.write(output)

# Print summary lines
for line in output.split('\n'):
    if any(kw in line for kw in ['Total tests', 'Passed:', 'Failed:', 'RescueOrchestrator']):
        print(line.strip())