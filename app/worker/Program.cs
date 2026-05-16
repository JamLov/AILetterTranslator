using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Services;
using Microsoft.Extensions.Configuration;
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
        loggerConfig
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Console());

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

    // Log configuration summary at startup
    logger.LogInformation("=== Worker Configuration ===");
    logger.LogInformation("  StorageProvider:   {StorageProvider}", builder.Configuration["StorageProvider"] ?? "Local (default)");
    logger.LogInformation("  DataStoragePath:   {DataStoragePath}", builder.Configuration["DataStoragePath"] ?? "(not set)");
    logger.LogInformation("  Gemini ApiKey:     {ApiKey}", string.IsNullOrEmpty(builder.Configuration["Gemini:ApiKey"]) ? "(not set)" : "****");
    logger.LogInformation("  Gemini Model:      {Model}", builder.Configuration["Gemini:Model"] ?? "gemini-2.5-pro (default)");
    logger.LogInformation("  AzureBlob Conn:    {ConnStr}", string.IsNullOrEmpty(builder.Configuration["AzureBlob:ConnectionString"]) ? "(not set)" : "****");
    logger.LogInformation("  AzureBlob Container: {Container}", builder.Configuration["AzureBlob:ContainerName"] ?? "(not set)");
    logger.LogInformation("============================");

    var discoveryService = scope.ServiceProvider.GetRequiredService<IJobDiscoveryService>();
    var processorService = scope.ServiceProvider.GetRequiredService<IJobProcessorService>();

    var pendingJobs = await discoveryService.FindPendingJobsAsync();

    if (pendingJobs.Count == 0)
    {
        logger.LogInformation("No pending jobs found.");
    }
    else
    {
        logger.LogInformation("Found {Count} pending job(s) to process", pendingJobs.Count);

        foreach (var job in pendingJobs)
        {
            await processorService.ProcessJobAsync(job);
        }

        logger.LogInformation("All pending jobs processed.");
    }

    // Backfill pass: regenerate Transcribed_With_Notes.md for Finished jobs from before this feature.
    // Bounded per run; failures never flip Status (degraded UI is acceptable, hard error is not).
    var backfillEnabled = builder.Configuration.GetValue<bool>("Backfill:Enabled", true);
    var backfillLimit = builder.Configuration.GetValue<int>("Backfill:MaxJobsPerRun", 5);

    if (backfillEnabled && backfillLimit > 0)
    {
        var candidates = await discoveryService.FindJobsMissingTranscribedWithNotesAsync(backfillLimit);
        if (candidates.Count == 0)
        {
            logger.LogInformation("Backfill: no Finished jobs missing Transcribed_With_Notes.md.");
        }
        else
        {
            int success = 0;
            foreach (var job in candidates)
            {
                try
                {
                    await processorService.BackfillTranscribedWithNotesAsync(job);
                    success++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Backfill failed for job {JobId}; status unchanged, will retry next run", job.JobId);
                }
            }
            logger.LogInformation("Backfilled {Success}/{Total} jobs missing Transcribed_With_Notes.md", success, candidates.Count);
        }
    }
    else
    {
        logger.LogInformation("Backfill pass disabled (Backfill:Enabled={Enabled}, Backfill:MaxJobsPerRun={Limit}).",
            backfillEnabled, backfillLimit);
    }

    logger.LogInformation("Worker run complete. Exiting.");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
    Console.Error.WriteLine($"FATAL: Worker terminated unexpectedly: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
