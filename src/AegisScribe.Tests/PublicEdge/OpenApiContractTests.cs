using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AegisScribe.ApiService;
using AegisScribe.ApiService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AegisScribe.Tests.PublicEdge;

// In-process WebApplicationFactory rather than the Aspire-orchestrated fixture used elsewhere in
// this project: these tests need to flip IWebHostEnvironment per test (Development vs Production),
// and the Aspire-launched api always runs Development per its launchSettings.json.
public class OpenApiContractTests
{
    [Fact]
    public async Task OpenApiDocument_MatchesCommittedContract()
    {
        using var factory = CreateFactory(environment: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var live = Format(await response.Content.ReadAsStringAsync());
        var contractPath = ResolveContractPath();

        if (!File.Exists(contractPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contractPath)!);
            await File.WriteAllTextAsync(contractPath, live);
            Assert.Fail(
                $"contract/openapi.v1.json did not exist; generated it at {contractPath}. " +
                "Review the diff and commit it, then re-run this test.");
        }

        var committed = await File.ReadAllTextAsync(contractPath);
        Assert.Equal(committed, live);
    }

    [Fact]
    public async Task OpenApiDocument_404sInProduction()
    {
        using var factory = CreateFactory(environment: "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<TestEntryPoint> CreateFactory(string? environment)
    {
        return new WebApplicationFactory<TestEntryPoint>().WithWebHostBuilder(builder =>
        {
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
            }

            if (environment == "Production")
            {
                // The non-Development branch in Program.cs loads real signing/encryption certs
                // from config — feed it throwaway self-signed ones so the host can boot at all;
                // this test only cares whether /openapi/v1.json is mapped, not the OIDC keys.
                var pfx = Convert.ToBase64String(CreateThrowawayPfx());
                builder.UseSetting("Oidc:EncryptionCertificate", pfx);
                builder.UseSetting("Oidc:SigningCertificate", pfx);
            }

            // No real SQL Server is available to a bare WebApplicationFactory host the way the
            // Aspire-orchestrated fixture has one, and Program.cs's startup-time RoleSeeder call
            // needs *some* working store — swap in EF Core's InMemory provider for it. Aspire's
            // AddSqlServerDbContext pools the context, which registers more than just
            // DbContextOptions<T>, so remove everything keyed to AegisScribeDbContext rather than
            // guessing at EF's internal pooling service types.
            builder.ConfigureServices(services =>
            {
                var toRemove = services
                    .Where(d => (d.ServiceType.FullName ?? d.ServiceType.ToString()).Contains(nameof(AegisScribeDbContext)))
                    .ToList();
                foreach (var descriptor in toRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AegisScribeDbContext>(options =>
                    options.UseInMemoryDatabase(Guid.NewGuid().ToString()).UseOpenIddict());
            });
        });
    }

    private static byte[] CreateThrowawayPfx()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
        return cert.Export(X509ContentType.Pfx);
    }

    private static string Format(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    // src/AegisScribe.Tests/PublicEdge/OpenApiContractTests.cs -> repo root -> contract/openapi.v1.json.
    // Resolved from the compiled-in source path rather than the test runner's working directory,
    // which varies by tool and CI runner.
    private static string ResolveContractPath([CallerFilePath] string sourceFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));
        return Path.Combine(repoRoot, "contract", "openapi.v1.json");
    }
}
