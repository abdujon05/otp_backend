namespace PayomAuth.Infrastructure.Services;

public sealed record PayomOptions(
    Uri BaseUri,
    string JwtToken,
    string SenderName,
    string MessageTemplate);
