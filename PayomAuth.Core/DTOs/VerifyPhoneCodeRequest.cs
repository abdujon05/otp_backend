using System.ComponentModel.DataAnnotations;

namespace PayomAuth.Core.DTOs;

public sealed class VerifyPhoneCodeRequest
{
    [Required]
    [RegularExpression(@"^\+992\d{9}$")]
    public string Telephone { get; init; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{6}$")]
    public string Code { get; init; } = string.Empty;
}
