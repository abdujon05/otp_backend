using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using PayomAuth.Api.Services;
using PayomAuth.Core.Interfaces;
using PayomAuth.Infrastructure.Data;
using PayomAuth.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Includes appsettings files, User Secrets (in Development), and environment
// variables. Later providers override earlier ones, so deployment secrets do
// not need to be committed to appsettings.json.
var appSettings = builder.Configuration;

var connectionString = appSettings.GetConnectionString("DefaultConnectionString");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:DefaultConnectionString before starting the API.");
}

var payomBaseUrl = RequireSetting(appSettings, "PayomOptions:BaseUri");
if (!payomBaseUrl.Contains("://", StringComparison.Ordinal))
{
    payomBaseUrl = $"https://{payomBaseUrl}";
}

if (!Uri.TryCreate(payomBaseUrl, UriKind.Absolute, out var payomBaseUri)
    || payomBaseUri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException("PayomOptions:BaseUri must be an absolute HTTPS URL.");
}

var payomToken = RequireSetting(appSettings, "PayomOptions:JwtToken");
var senderName = appSettings["PayomOptions:SenderName"] ?? "payom.tj";
if (senderName.Length is < 1 or > 11 || !Regex.IsMatch(senderName, @"^[A-Za-z0-9.]+$"))
{
    throw new InvalidOperationException(
        "PayomOptions:SenderName must contain 1-11 Latin letters, digits, or periods.");
}

var messageTemplate = appSettings["PayomOptions:MessageTemplate"]
    ?? "Your verification code is {code}";
if (!messageTemplate.Contains("{code}", StringComparison.Ordinal))
{
    throw new InvalidOperationException("PayomOptions:MessageTemplate must contain {code}.");
}

var hashKey = Encoding.UTF8.GetBytes(RequireSetting(appSettings, "Otp:HashKey"));
if (hashKey.Length < 32)
{
    throw new InvalidOperationException("Otp:HashKey must contain at least 32 UTF-8 bytes.");
}

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .AllowAnyOrigin()
            .WithMethods("GET", "POST")
            .WithHeaders("Content-Type"));
});
builder.Services.AddSingleton(new PayomOptions(
    payomBaseUri,
    payomToken,
    senderName,
    messageTemplate));
builder.Services.AddScoped<PostgresPhoneVerificationStore>();
builder.Services.AddScoped<IPhoneVerificationStore>(services =>
    services.GetRequiredService<PostgresPhoneVerificationStore>());
builder.Services.AddScoped<IPhoneVerificationService>(services => new PhoneVerificationService(
    services.GetRequiredService<IPhoneVerificationStore>(),
    services.GetRequiredService<ISmsSender>(),
    hashKey,
    services.GetRequiredService<TimeProvider>()));
builder.Services.AddHttpClient<ISmsSender, PayomSmsSender>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("sms-verification", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var verificationStore = scope.ServiceProvider.GetRequiredService<PostgresPhoneVerificationStore>();
    await verificationStore.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

static string RequireSetting(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Configure {key} before starting the API.");
    }

    return value;
}
