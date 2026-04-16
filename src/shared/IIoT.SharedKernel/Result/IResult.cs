using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.SharedKernel.Result;

public interface IResult
{
    IEnumerable<string>? Errors { get; }

    bool IsSuccess { get; }

    ResultStatus Status { get; }

    object? GetValue();
}
