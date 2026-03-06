using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.SharedKernel.Result;

public enum ResultStatus
{
    Ok,
    Error,
    Forbidden,
    Unauthorized,
    NotFound,
    Invalid
}