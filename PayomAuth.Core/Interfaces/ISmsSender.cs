namespace PayomAuth.Core.Interfaces;

public interface ISmsSender
{
    Task SendVerificationCodeAsync(
        string telephone,
        string code,
        CancellationToken cancellationToken);
}
