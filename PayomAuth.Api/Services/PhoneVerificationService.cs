using System.Security.Cryptography;
using System.Text;
using PayomAuth.Core.Interfaces;
using PayomAuth.Infrastructure.Services;

namespace PayomAuth.Api.Services;

public sealed class PhoneVerificationService : IPhoneVerificationService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 5;

    private readonly IPhoneVerificationStore _store;
    private readonly ISmsSender _smsSender;
    private readonly byte[] _hashKey;
    private readonly TimeProvider _timeProvider;

    public PhoneVerificationService(
        IPhoneVerificationStore store,
        ISmsSender smsSender,
        byte[] hashKey,
        TimeProvider timeProvider)
    {
        _store = store;
        _smsSender = smsSender;
        _hashKey = hashKey;
        _timeProvider = timeProvider;
    }

    public async Task<bool> StartAsync(string telephone, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var now = _timeProvider.GetUtcNow();
        var challengeId = Guid.NewGuid();
        var codeHash = HashCode(telephone, code);

        var created = await _store.TryCreateChallengeAsync(
            telephone,
            challengeId,
            codeHash,
            now.Add(CodeLifetime),
            now.Add(ResendDelay),
            now,
            cancellationToken);

        if (!created)
        {
            return false;
        }

        try
        {
            await _smsSender.SendVerificationCodeAsync(telephone, code, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or PayomApiException or OperationCanceledException)
        {
            await _store.RemoveChallengeAsync(challengeId, CancellationToken.None);
            throw;
        }

        return true;
    }

    public Task<bool> VerifyAsync(
        string telephone,
        string code,
        CancellationToken cancellationToken)
    {
        return _store.TryVerifyCodeAsync(
            telephone,
            HashCode(telephone, code),
            _timeProvider.GetUtcNow(),
            MaxAttempts,
            cancellationToken);
    }

    private byte[] HashCode(string telephone, string code)
    {
        return HMACSHA256.HashData(
            _hashKey,
            Encoding.UTF8.GetBytes($"{telephone}:{code}"));
    }
}
