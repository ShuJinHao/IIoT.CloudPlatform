using IIoT.EmployeeService.Commands.Employees;
using IIoT.EmployeeService.Queries.Employees;
using IIoT.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/employees")]
[ApiController]
[Tags("Human Employees")]
public class HumanEmployeeController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] GetEmployeePagedListQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new GetEmployeeDetailQuery(id)));
    }

    [HttpGet("{id}/access")]
    public async Task<IActionResult> GetAccess([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new GetEmployeeAccessQuery(id)));
    }

    [HttpPost]
    public async Task<IActionResult> Onboard([FromBody] OnboardEmployeeCommand command)
    {
        return ReturnResult(
            await Sender.Send(command),
            employeeId => $"/api/v1/human/employees/{employeeId}");
    }

    [HttpPut("{id}/profile")]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid id, [FromBody] UpdateEmployeeProfileCommand command)
    {
        return ReturnResult(await Sender.Send(command with { EmployeeId = id }));
    }

    [HttpPut("{id}/access")]
    public async Task<IActionResult> UpdateAccess([FromRoute] Guid id, [FromBody] UpdateEmployeeAccessCommand command)
    {
        return ReturnResult(await Sender.Send(command with { EmployeeId = id }));
    }

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new DeactivateEmployeeCommand(id)));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Terminate([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new TerminateEmployeeCommand(id)));
    }
}
