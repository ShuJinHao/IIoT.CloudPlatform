using IIoT.EmployeeService.Commands.Employees;
using IIoT.EmployeeService.Queries.Employees;
using IIoT.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/employees")]
[ApiController]
[Tags("Human Employees")]
public class HumanEmployeeController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList(
        [FromQuery] GetEmployeePagedListQuery query,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(query, cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEmployeeDetailQuery(id), cancellationToken));
    }

    [HttpGet("{id}/access")]
    public async Task<IActionResult> GetAccess(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEmployeeAccessQuery(id), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Onboard(
        [FromBody] OnboardEmployeeCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            employeeId => $"/api/v1/human/employees/{employeeId}");
    }

    [HttpPut("{id}/profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromRoute] Guid id,
        [FromBody] UpdateEmployeeProfileCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with { EmployeeId = id }, cancellationToken));
    }

    [HttpPut("{id}/access")]
    public async Task<IActionResult> UpdateAccess(
        [FromRoute] Guid id,
        [FromBody] UpdateEmployeeAccessCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with { EmployeeId = id }, cancellationToken));
    }

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new DeactivateEmployeeCommand(id), cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Terminate(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new TerminateEmployeeCommand(id), cancellationToken));
    }
}
