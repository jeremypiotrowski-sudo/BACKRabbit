$models = @("SM-F966U1", "SM-F956U", "SM-S918U")
$region = "XAA"

# Generate auth token the same way FirmwareSourcer.cs does: MD5(model:region)
foreach ($model in $models) {
    Write-Host "=== Testing $model / $region (with MD5 auth token) ==="
    try {
        $identityString = "$($model):$($region)"
        $md5 = [System.Security.Cryptography.MD5]::Create()
        $hash = $md5.ComputeHash([System.Text.Encoding]::ASCII.GetBytes($identityString))
        $authToken = [System.BitConverter]::ToString($hash).Replace("-", "").ToLower()
        Write-Host "Auth token: $authToken"
        
        $url = "https://fota-cloud-dn.ospserver.net/firmware/$model/$region/version.xml"
        $headers = @{
            "User-Agent" = "FOTA"
            "Authorization" = "Bearer $authToken"
            "X-Auth-Token" = $authToken
        }
        $response = Invoke-WebRequest -Uri $url -Headers $headers -TimeoutSec 15 -ErrorAction Stop
        Write-Host "Status: $($response.StatusCode)"
        $body = $response.Content
        if ($body.Length -gt 500) { $body = $body.Substring(0, 500) }
        Write-Host "Body: $body"
    } catch {
        $statusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "N/A" }
        Write-Host "Error: HTTP $statusCode - $($_.Exception.Message)"
    }
    Write-Host ""
}

# Also test with dummy IMEI-based token
Write-Host "=== Testing SM-F966U1/XAA with IMEI-based token ==="
try {
    $identityString = "SM-F966U1:XAA:351234567890123"
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hash = $md5.ComputeHash([System.Text.Encoding]::ASCII.GetBytes($identityString))
    $authToken = [System.BitConverter]::ToString($hash).Replace("-", "").ToLower()
    Write-Host "Auth token: $authToken"
    
    $url = "https://fota-cloud-dn.ospserver.net/firmware/SM-F966U1/XAA/version.xml"
    $headers = @{
        "User-Agent" = "FOTA"
        "Authorization" = "Bearer $authToken"
        "X-Auth-Token" = $authToken
    }
    $response = Invoke-WebRequest -Uri $url -Headers $headers -TimeoutSec 15 -ErrorAction Stop
    Write-Host "Status: $($response.StatusCode)"
    $body = $response.Content
    if ($body.Length -gt 500) { $body = $body.Substring(0, 500) }
    Write-Host "Body: $body"
} catch {
    $statusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { "N/A" }
    Write-Host "Error: HTTP $statusCode - $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Done."