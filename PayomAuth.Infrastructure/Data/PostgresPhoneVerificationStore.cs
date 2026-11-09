using Npgsql;
using PayomAuth.Core.Interfaces;

namespace PayomAuth.Infrastructure.Data;

public sealed class PostgresPhoneVerificationStore : IPhoneVerificationStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPhoneVerificationStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS phone_verification_challenges (
                telephone TEXT PRIMARY KEY,
                challenge_id UUID NOT NULL UNIQUE,
                code_hash BYTEA NOT NULL,
                expires_at TIMESTAMPTZ NOT NULL,
                resend_allowed_at TIMESTAMPTZ NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS verified_phones (
                telephone TEXT PRIMARY KEY,
                verified_at TIMESTAMPTZ NOT NULL
            );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryCreateChallengeAsync(
        string telephone,
        Guid challengeId,
        byte[] codeHash,
        DateTimeOffset expiresAt,
        DateTimeOffset resendAllowedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string cleanupSql = """
            DELETE FROM phone_verification_challenges
            WHERE expires_at < @now - INTERVAL '1 day';
            """;
        const string sql = """
            INSERT INTO phone_verification_challenges
                (telephone, challenge_id, code_hash, expires_at, resend_allowed_at, attempts)
            VALUES
                (@telephone, @challenge_id, @code_hash, @expires_at, @resend_allowed_at, 0)
            ON CONFLICT (telephone) DO UPDATE SET
                challenge_id = EXCLUDED.challenge_id,
                code_hash = EXCLUDED.code_hash,
                expires_at = EXCLUDED.expires_at,
                resend_allowed_at = EXCLUDED.resend_allowed_at,
                attempts = 0
            WHERE phone_verification_challenges.resend_allowed_at <= @now
            RETURNING challenge_id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using (var cleanupCommand = new NpgsqlCommand(cleanupSql, connection))
        {
            cleanupCommand.Parameters.AddWithValue("now", now);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("telephone", telephone);
        command.Parameters.AddWithValue("challenge_id", challengeId);
        command.Parameters.AddWithValue("code_hash", codeHash);
        command.Parameters.AddWithValue("expires_at", expiresAt);
        command.Parameters.AddWithValue("resend_allowed_at", resendAllowedAt);
        command.Parameters.AddWithValue("now", now);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid;
    }

    public async Task<bool> TryVerifyCodeAsync(
        string telephone,
        byte[] codeHash,
        DateTimeOffset now,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        const string updateSql = """
            UPDATE phone_verification_challenges
            SET attempts = attempts + CASE WHEN code_hash = @code_hash THEN 0 ELSE 1 END
            WHERE telephone = @telephone
                AND expires_at > @now
                AND attempts < @max_attempts
            RETURNING code_hash = @code_hash;
            """;
        const string saveVerifiedSql = """
            INSERT INTO verified_phones (telephone, verified_at)
            VALUES (@telephone, @now)
            ON CONFLICT (telephone) DO UPDATE SET verified_at = EXCLUDED.verified_at;
            """;
        const string deleteChallengeSql = """
            DELETE FROM phone_verification_challenges WHERE telephone = @telephone;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        updateCommand.Parameters.AddWithValue("telephone", telephone);
        updateCommand.Parameters.AddWithValue("code_hash", codeHash);
        updateCommand.Parameters.AddWithValue("now", now);
        updateCommand.Parameters.AddWithValue("max_attempts", maxAttempts);

        var result = await updateCommand.ExecuteScalarAsync(cancellationToken);
        if (result is not bool matched)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (matched)
        {
            await using var saveVerifiedCommand = new NpgsqlCommand(saveVerifiedSql, connection, transaction);
            saveVerifiedCommand.Parameters.AddWithValue("telephone", telephone);
            saveVerifiedCommand.Parameters.AddWithValue("now", now);
            await saveVerifiedCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var deleteChallengeCommand = new NpgsqlCommand(deleteChallengeSql, connection, transaction);
            deleteChallengeCommand.Parameters.AddWithValue("telephone", telephone);
            await deleteChallengeCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return matched;
    }

    public async Task RemoveChallengeAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM phone_verification_challenges WHERE challenge_id = @challenge_id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("challenge_id", challengeId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
