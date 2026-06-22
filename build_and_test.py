import subprocess, os
os.chdir(r'C:\Users\jp\Documents\BACKRabbit')

# Build
r = subprocess.run(['dotnet','build','BACKRabbit.Protocol.Firehose.Tests','-c','Release'], capture_output=True, text=True)
with open('build_output.txt','w') as f:
    f.write(r.stdout)
    f.write('\n--- STDERR ---\n')
    f.write(r.stderr)

# Test
r2 = subprocess.run(['dotnet','test','BACKRabbit.Protocol.Firehose.Tests','-c','Release','--no-build'], capture_output=True, text=True)
with open('test_output.txt','w') as f:
    f.write(r2.stdout)
    f.write('\n--- STDERR ---\n')
    f.write(r2.stderr)

print('Done. Build return code:', r.returncode)
print('Test return code:', r2.returncode)