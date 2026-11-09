namespace PayomAuth.Infrastructure.Services;

public sealed class PayomApiException : Exception
{
    public PayomApiException(string message) : base(message)
    {
    }
}
