using System.ComponentModel.DataAnnotations;

namespace PayomAuth.Core.DTOs;

public sealed class StartPhoneVerificationRequest
{
    [Required]
    [RegularExpression(@"^\+992\d{9}$")]
    public string Telephone { get; init; } = string.Empty;
}
