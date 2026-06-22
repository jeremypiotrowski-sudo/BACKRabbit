import subprocess
result = subprocess.run(
    ['dotnet', 'test', 'BACKRabbit.Protocol.Firehose.Tests', '-c', 'Release'],
    capture_output=True, text=True, cwd=r'C:\Users\jp\Documents\BACKRabbit'
)
with open(r'C:\Users\jp\Documents\BACKRabbit\test_output.txt', 'w', encoding='utf-8') as f:
    f.write(result.stdout)
    f.write('\n\n--- STDERR ---\n')
    f.write(result.stderr)
print('Done. Reading summary...')

# Parse summary
for line in result.stdout.split('\n'):
    if 'Test summary' in line or 'Total tests' in line or 'Passed' in line or 'Failed' in line:
        print(line.strip())