using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Letter Translation Worker starting");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, loggerConfig) =>
        loggerConfig.ReadFrom.Configuration(builder.Configuration));

    var storageProvider = builder.Configuration["StorageProvider"];
    if (string.Equals(storageProvider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddSingleton<IStorageService, AzureBlobStorageService>();
    else
        builder.Services.AddSingleton<IStorageService, LocalDiskStorageService>();
    builder.Services.AddTransient<IJobDiscoveryService, JobDiscoveryService>();
    builder.Services.AddTransient<IGeminiService, GeminiService>();
    builder.Services.AddTransient<IJobProcessorService, JobProcessorService>();

    var host = builder.Build();

    using var scope = host.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var discoveryService = scope.ServiceProvider.GetRequiredService<IJobDiscoveryService>();
    var processorService = scope.ServiceProvider.GetRequiredService<IJobProcessorService>();

    var pendingJobs = await discoveryService.FindPendingJobsAsync();

    if (pendingJobs.Count == 0)
    {
        logger.LogInformation("No pending jobs found. Exiting.");
    }
    else
    {
        logger.LogInformation("Found {Count} pending job(s) to process", pendingJobs.Count);

        foreach (var job in pendingJobs)
        {
            await processorService.ProcessJobAsync(job);
        }

        logger.LogInformation("All jobs processed. Exiting.");
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
