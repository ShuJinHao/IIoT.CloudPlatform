; Unshipped analyzer release tracking

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
CLOUDARCH007 | IIoT.Architecture | Error | Security-sensitive permission/device identity reads cannot reach ICacheService through direct or transitive calls.
CLOUDARCH008 | IIoT.Architecture | Error | Production code cannot parse unvalidated JWT claims with JwtSecurityTokenHandler.ReadJwtToken.
CLOUDARCH009 | IIoT.Architecture | Error | Production types cannot recreate the retired IIoT.Services.Common namespace or its descendants.
CLOUDARCH010 | IIoT.Architecture | Error | Connection resource literals are owned exclusively by ConnectionResourceNames.
