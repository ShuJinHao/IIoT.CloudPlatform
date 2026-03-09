using System.Security.Claims;
using IIoT.HttpApi.Infrastructure;
using IIoT.HttpApi.Models;
using IIoT.IdentityService.Commands;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Route("/api/identity")]
public class IdentityController : ApiControllerBase
{
    [HttpPost("test")]
    public IActionResult Test()
    {
        return Ok(new
        {
            IsAuthenticated = User.Identity?.IsAuthenticated,
            // 🌟 之前在 JwtTokenGenerator 里，我们把 JwtRegisteredClaimNames.UniqueName 设为了工号
            EmployeeNo = User.FindFirstValue(ClaimTypes.Name)
        });
    }
}