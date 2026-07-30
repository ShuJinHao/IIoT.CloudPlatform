namespace IIoT.Services.Contracts;

public abstract class EmployeeMutationException(
    string safeMessage,
    string problemCode)
    : Exception(safeMessage)
{
    public string SafeMessage { get; } = safeMessage;

    public string ProblemCode { get; } = problemCode;
}

public sealed class EmployeeRoleUpdateConflictException()
    : EmployeeMutationException(PublicMessage, Code)
{
    public const string PublicMessage = "员工角色状态已发生变化，请刷新后重试。";
    public const string Code = "employee_role_update_conflict";
}

public sealed class EmployeeRoleUpdateCommitUnknownException()
    : EmployeeMutationException(PublicMessage, Code)
{
    public const string PublicMessage = "员工角色提交结果暂时无法确认，请勿重复操作并联系管理员核验。";
    public const string Code = "employee_role_update_commit_unknown";
}

public sealed class EmployeeActivationConflictException()
    : EmployeeMutationException(PublicMessage, Code)
{
    public const string PublicMessage = "员工启用状态已发生变化，请刷新后重试。";
    public const string Code = "employee_activation_conflict";
}

public sealed class EmployeeActivationCommitUnknownException()
    : EmployeeMutationException(PublicMessage, Code)
{
    public const string PublicMessage = "员工启用提交结果暂时无法确认，请勿重复操作并联系管理员核验。";
    public const string Code = "employee_activation_commit_unknown";
}

public sealed class EmployeeWriteConflictException()
    : EmployeeMutationException(PublicMessage, Code)
{
    public const string PublicMessage = "员工写入状态已发生变化，请刷新后重试。";
    public const string Code = "employee_write_conflict";
}

public sealed class EmployeeWriteCommitUnknownException()
    : EmployeeMutationException(PublicMessage, Code)
{
    public const string PublicMessage = "员工写入提交结果暂时无法确认，请勿重复操作并联系管理员核验。";
    public const string Code = "employee_write_commit_unknown";
}
