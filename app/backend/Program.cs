using LetterTranslation.Api.Services;
using LetterTranslation.Shared.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<VersionOperations>();
builder.Services.AddScoped<IDataService, DataService>();
builder.Services.AddScoped<IProjectService, ProjectService>();

// Register Storage Service based on config
var storageProvider = builder.Configuration["StorageProvider"];
if (string.Equals(storageProvider, "Local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IStorageService, LocalDiskStorageService>();
}
else if (string.Equals(storageProvider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IStorageService, AzureBlobStorageService>();
}
else
{
    // Default fallback to Local Disk
    builder.Services.AddScoped<IStorageService, LocalDiskStorageService>();
}

// Add CORS
builder.Services.AddCors();

// Configure Authentication using Google JWTs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://accounts.google.com",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Authentication:Google:ClientId"],
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Log configuration summary at startup
Log.Information("=== Backend Configuration ===");
Log.Information("  Environment:      {Environment}", app.Environment.EnvironmentName);
Log.Information("  StorageProvider:   {StorageProvider}", builder.Configuration["StorageProvider"] ?? "Local (default)");
Log.Information("  DataStoragePath:   {DataStoragePath}", builder.Configuration["DataStoragePath"] ?? "(not set)");
Log.Information("  Google ClientId:   {ClientId}", string.IsNullOrEmpty(builder.Configuration["Authentication:Google:ClientId"]) ? "(not set)" : "****");
Log.Information("  AllowedUsers:      {Count} configured", builder.Configuration.GetSection("AllowedUsers").GetChildren().Count(c => !string.IsNullOrEmpty(c.Value)));
Log.Information("  AzureBlob Conn:    {ConnStr}", string.IsNullOrEmpty(builder.Configuration["AzureBlob:ConnectionString"]) ? "(not set)" : "****");
Log.Information("  AzureBlob Container: {Container}", builder.Configuration["AzureBlob:ContainerName"] ?? "(not set)");
Log.Information("=============================");

if (app.Environment.IsDevelopment())
{
    app.UseCors(policy => 
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
}

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Make Program class public for Integration Tests WebApplicationFactory
public partial class Program { }
