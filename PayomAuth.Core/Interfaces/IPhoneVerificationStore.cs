namespace PayomAuth.Core.Interfaces;

public interface IPhoneVerificationStore
{
    Task<bool> TryCreateChallengeAsync(
        string telephone,
        Guid challengeId,
        byte[] codeHash,
        DateTimeOffset expiresAt,
        DateTimeOffset resendAllowedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> TryVerifyCodeAsync(
        string telephone,
        byte[] codeHash,
        DateTimeOffset now,
        int maxAttempts,
        CancellationToken cancellationToken);

    Task RemoveChallengeAsync(Guid challengeId, CancellationToken cancellationToken);
}
