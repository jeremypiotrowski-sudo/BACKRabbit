#!/usr/bin/env dotnet-script
// Quick FUS API probe — tests auth requirements for SM-F966U1/XAA
#r "System.Net.Http"

using System.Net.Http;

var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd("FOTA");
client.Timeout = TimeSpan.FromSeconds(15);

// Test 1: Bare GET (no auth)
Console.WriteLine("=== Test 1: Bare GET (no auth) ===");
try
{
    var url = "https://fota-cloud-dn.ospserver.net/firmware/SM-F966U1/XAA/version.xml";
    var response = await client.GetAsync(url);
    Console.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Body ({body.Length} chars): {body.Substring(0, Math.Min(body.Length, 500))}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
}

// Test 2: Try with Authorization header (dummy token)
Console.WriteLine("\n=== Test 2: With dummy Bearer token ===");
try
{
    var url = "https://fota-cloud-dn.ospserver.net/firmware/SM-F966U1/XAA/version.xml";
    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test_token_12345");
    var response = await client.SendAsync(request);
    Console.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Body ({body.Length} chars): {body.Substring(0, Math.Min(body.Length, 500))}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
}

// Test 3: Try SM-F966U1 (Z Fold 6, known-working model)
Console.WriteLine("\n=== Test 3: SM-F966U1 (Z Fold 6) bare GET ===");
try
{
    var url = "https://fota-cloud-dn.ospserver.net/firmware/SM-F966U1/XAA/version.xml";
    var response = await client.GetAsync(url);
    Console.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Body ({body.Length} chars): {body.Substring(0, Math.Min(body.Length, 500))}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine("\nDone.");