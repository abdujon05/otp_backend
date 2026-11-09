namespace PayomAuth.Core.Interfaces;

public interface IPhoneVerificationService
{
    Task<bool> StartAsync(string telephone, CancellationToken cancellationToken);

    Task<bool> VerifyAsync(string telephone, string code, CancellationToken cancellationToken);
}
