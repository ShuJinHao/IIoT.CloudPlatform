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
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail([FromRoute] Guid id)
    {
        var result = await Sender.Send(new GetEmployeeDetailQuery(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("{id}/access")]
    public async Task<IActionResult> GetAccess([FromRoute] Guid id)
    {
        var result = await Sender.Send(new GetEmployeeAccessQuery(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPost]
    public async Task<IActionResult> Onboard([FromBody] OnboardEmployeeCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("{id}/profile")]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid id, [FromBody] UpdateEmployeeProfileCommand command)
    {
        var result = await Sender.Send(command with { EmployeeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("{id}/access")]
    public async Task<IActionResult> UpdateAccess([FromRoute] Guid id, [FromBody] UpdateEmployeeAccessCommand command)
    {
        var result = await Sender.Send(command with { EmployeeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id)
    {
        var result = await Sender.Send(new DeactivateEmployeeCommand(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Terminate([FromRoute] Guid id)
    {
        var result = await Sender.Send(new TerminateEmployeeCommand(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
