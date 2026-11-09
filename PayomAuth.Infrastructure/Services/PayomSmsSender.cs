using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PayomAuth.Core.Interfaces;

namespace PayomAuth.Infrastructure.Services;

public sealed class PayomSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly PayomOptions _options;
    private readonly ILogger<PayomSmsSender> _logger;

    private readonly static JsonSerializerOptions jsonSerializer = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PayomSmsSender(
        HttpClient httpClient,
        PayomOptions options,
        ILogger<PayomSmsSender> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task SendVerificationCodeAsync(
        string telephone,
        string code,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(_options.BaseUri, "/api/message");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.JwtToken);

        request.Content = JsonContent.Create(new SendMessagePayload(
            telephone,
            _options.MessageTemplate.Replace("{code}", code, StringComparison.Ordinal),
            _options.SenderName), options: jsonSerializer);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Payom rejected an SMS request with HTTP status {StatusCode}.",
                (int)response.StatusCode);
            throw new PayomApiException("The SMS provider did not accept the message.");
        }
    }

    private sealed record SendMessagePayload(string Telephone, string Text, string SenderName)
    {
        public string Type => "SMS";
    }
}
