using System;

namespace IIoT.Services.CrossCutting.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AuthorizeAiReadAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
