using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LetterTranslation.Api.IntegrationTests;

public class IsolatedTestWebAppFactory : IAsyncLifetime
{
    private readonly TestWebAppFactory _factory = new();
    private string _testRootPath = string.Empty;
    
    public HttpClient Client { get; private set; } = null!;
    public IConfiguration Configuration => _factory.TestConfiguration!;
    public string TestRootPath => _testRootPath;
    
    public Task InitializeAsync()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "lt-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRootPath);

        Client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "DataStoragePath", _testRootPath }
                });
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        var directoryInfo = new DirectoryInfo(_testRootPath);
        if (directoryInfo.Exists)
        {
            directoryInfo.Delete(true);
        }

        Client.Dispose();
        _factory.Dispose();
        
        return Task.CompletedTask;
    }
}
