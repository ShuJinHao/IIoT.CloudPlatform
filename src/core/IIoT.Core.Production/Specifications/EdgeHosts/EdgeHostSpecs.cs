using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.EdgeHosts;

public sealed class EdgeHostByIdSpec : Specification<EdgeHost>
{
    public EdgeHostByIdSpec(Guid edgeHostId, bool includeBindings = true)
    {
        FilterCondition = host => host.Id == edgeHostId;
        if (includeBindings)
        {
            AddInclude(host => host.PlcBindings);
        }
    }
}

public sealed class EdgeHostByDeviceIdentitySpec : Specification<EdgeHost>
{
    public EdgeHostByDeviceIdentitySpec(Guid deviceId, string clientCode, bool includeBindings = true)
    {
        var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
        FilterCondition = host => host.DeviceId == deviceId && host.ClientCode == normalizedClientCode;
        if (includeBindings)
        {
            AddInclude(host => host.PlcBindings);
        }
    }
}

public sealed class EdgeHostPagedSpec : Specification<EdgeHost>
{
    public EdgeHostPagedSpec(
        int skip,
        int take,
        string? keyword = null,
        bool isPaging = true)
    {
        var keywordValue = keyword?.Trim();
        var normalizedKeyword = keywordValue?.ToUpperInvariant();

        FilterCondition = host =>
            string.IsNullOrEmpty(normalizedKeyword)
            || host.HostName.Contains(keywordValue!)
            || host.ClientCode.Contains(normalizedKeyword)
            || host.PlcBindings.Any(binding =>
                binding.PlcCode.Contains(normalizedKeyword)
                || binding.PlcName.Contains(keywordValue!));

        AddInclude(host => host.PlcBindings);
        SetOrderBy(host => host.HostName);

        if (isPaging)
        {
            SetPaging(skip, take);
        }
    }
}
