using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.Services.CrossCutting.Exceptions;

public class ForbiddenException(string? message) : Exception(message);