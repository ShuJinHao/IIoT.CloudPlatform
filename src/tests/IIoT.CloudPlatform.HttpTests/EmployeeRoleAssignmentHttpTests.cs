using System.Text.Json;
using IIoT.HttpApi.Controllers;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class EmployeeRoleAssignmentHttpTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void MissingRoleName_ShouldBeRejectedDuringRequestDeserialization()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateEmployeeRoleRequest>(
                "{}",
                SerializerOptions));
    }

    [Fact]
    public void ExplicitNullRoleName_ShouldRemainDistinctFromMissingProperty()
    {
        var request = JsonSerializer.Deserialize<UpdateEmployeeRoleRequest>(
            """{"roleName":null}""",
            SerializerOptions);

        Assert.NotNull(request);
        Assert.Null(request.RoleName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankRoleName_ShouldRemainAvailableForApplicationValidation(string roleName)
    {
        var payload = JsonSerializer.Serialize(new { roleName }, SerializerOptions);
        var request = JsonSerializer.Deserialize<UpdateEmployeeRoleRequest>(
            payload,
            SerializerOptions);

        Assert.NotNull(request);
        Assert.Equal(roleName, request.RoleName);
    }

    [Fact]
    public void BodyEmployeeId_ShouldBeRejectedSoRouteIdRemainsOnlyTargetSource()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateEmployeeRoleRequest>(
                $$"""{"roleName":"HrAdmin","employeeId":"{{Guid.NewGuid()}}"}""",
                SerializerOptions));
    }
}
