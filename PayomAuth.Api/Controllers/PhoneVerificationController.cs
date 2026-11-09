using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PayomAuth.Core.DTOs;
using PayomAuth.Core.Interfaces;
using PayomAuth.Infrastructure.Services;

namespace PayomAuth.Api.Controllers;

[ApiController]
[Route("api/auth/sms")]
public sealed class PhoneVerificationController : ControllerBase
{
    private readonly IPhoneVerificationService _verificationService;

    public PhoneVerificationController(IPhoneVerificationService verificationService)
    {
        _verificationService = verificationService;
    }

    [HttpPost("send")]
    [EnableRateLimiting("sms-verification")]
    public async Task<IActionResult> SendCode(
        [FromBody] StartPhoneVerificationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var sent = await _verificationService.StartAsync(request.Telephone, cancellationToken);
            if (!sent)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = "A code was recently sent. Please wait before requesting another."
                });
            }

            return Accepted(new { message = "Verification code sent." });
        }
        catch (PayomApiException)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "SMS delivery failed",
                detail: "The SMS provider did not accept the verification message.");
        }
        catch (HttpRequestException)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "SMS provider unavailable",
                detail: "The verification message could not be delivered.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "SMS provider timeout",
                detail: "The verification message could not be delivered.");
        }
    }

    [HttpPost("verify")]
    [EnableRateLimiting("sms-verification")]
    public async Task<IActionResult> VerifyCode(
        [FromBody] VerifyPhoneCodeRequest request,
        CancellationToken cancellationToken)
    {
        var verified = await _verificationService.VerifyAsync(
            request.Telephone,
            request.Code,
            cancellationToken);

        if (!verified)
        {
            return BadRequest(new { message = "The verification code is invalid or expired." });
        }

        return Ok(new { verified = true, telephone = request.Telephone });
    }
}
