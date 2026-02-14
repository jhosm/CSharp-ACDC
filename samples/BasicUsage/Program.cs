using CSharpAcdc.Client;
using CSharpAcdc.Extensions;
using Microsoft.Extensions.DependencyInjection;

// Register the ACDC HTTP client with zero configuration
var services = new ServiceCollection();
services.AddLogging();
services.AddAcdcHttpClient();

var sp = services.BuildServiceProvider();

// Resolve the client and make a request
var client = sp.GetRequiredService<AcdcHttpClient>();
var response = await client.GetAsync("https://httpbin.org/get");

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(await response.Content.ReadAsStringAsync());
