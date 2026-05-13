using System;

namespace IIoT.Services.CrossCutting.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class AdminOnlyAttribute : Attribute
{
}
