using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LetterTranslation.Api.IntegrationTests;

// Custom options class to pass test user details to the TestAuthHandler
public class TestAuthOptions : AuthenticationSchemeOptions
{
    public string Subject { get; set; } = "test-subject";
    public string Email { get; set; } = "test@example.com";
}

public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public IConfiguration? TestConfiguration { get; private set; }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Add test-specific configuration
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Find the appsettings.Test.json file in the test assembly's directory
            var testAssembly = Assembly.GetExecutingAssembly();
            var configPath = Path.GetDirectoryName(testAssembly.Location);
            
            TestConfiguration = config.AddJsonFile(Path.Combine(configPath!, "appsettings.Test.json")).Build();

            // The DataStoragePath will be overridden by the test fixture
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DataStoragePath", "" }
            });
        });

        // Replace services for testing
        builder.ConfigureServices(services =>
        {
            // Replace the real authentication handler with a test one,
            // configured with user details from our test settings.
            services.AddAuthentication("Test")
                .AddScheme<TestAuthOptions, TestAuthHandler>("Test", options =>
                {
                    var testUserConfig = TestConfiguration.GetSection("TestUser");
                    options.Subject = testUserConfig["Subject"];
                    options.Email = testUserConfig["Email"];
                });
        });
    }
}

// A test authentication handler that fakes a successful login using the provided options
public class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<TestAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, Options.Email),
            new Claim(ClaimTypes.NameIdentifier, Options.Subject)
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
