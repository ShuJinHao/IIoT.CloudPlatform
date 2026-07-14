using Microsoft.CodeAnalysis;

namespace IIoT.CloudPlatform.Analyzers;

internal static class CloudArchitectureDiagnostics
{
    private const string Category = "IIoT.Architecture";
    private const string HelpBase = "https://github.com/ShuJinHao/IIoT.CloudPlatform/blob/main/docs/contracts/cloud-architecture.md";

    internal static readonly DiagnosticDescriptor LayerDependency = new(
        id: "CLOUDARCH001",
        title: "Cloud 分层依赖方向错误",
        messageFormat: "程序集 '{0}' 不得依赖外层程序集 '{1}'。原因：内层必须保持独立；最短修复：把端口下沉到内层并由外层实现。精确例外：Host 组合根可以依赖服务和基础设施",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "SharedKernel 不依赖其它 Cloud 项目，Core 不依赖 Service/Infrastructure/Host，Service 不依赖 Infrastructure/Host，Infrastructure 不依赖 Host.",
        helpLinkUri: HelpBase + "#cloudarch001-分层依赖",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor AggregateBoundary = new(
        id: "CLOUDARCH002",
        title: "聚合根和通用仓储边界错误",
        messageFormat: "符号 '{0}' 违反聚合边界：{1}。原因：通用仓储只服务显式聚合根；最短修复：把聚合放入 Core 并实现 IAggregateRoot，或改用专用 store/read service。精确例外：无；投影、状态、审计、Outbox 和 token 不能伪装成聚合根",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "聚合根只能由 Core 声明，IRepository<T>/IReadRepository<T> 的 T 必须实现 IAggregateRoot.",
        helpLinkUri: HelpBase + "#cloudarch002-聚合与仓储");

    internal static readonly DiagnosticDescriptor DatabaseOwner = new(
        id: "CLOUDARCH003",
        title: "数据库访问越过基础设施 owner",
        messageFormat: "'{0}' 直接使用数据库 API '{1}'。原因：Core/Service 不能拥有 EF Core、Dapper、Npgsql 或 ADO.NET 访问；最短修复：定义内层端口并移动实现到 Infrastructure。精确例外：Infrastructure 和专用 Host 组合/迁移入口",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Core 和 Service 层禁止直接创建或调用数据库 provider API.",
        helpLinkUri: HelpBase + "#cloudarch003-数据库-owner");

    internal static readonly DiagnosticDescriptor AiReadWritePath = new(
        id: "CLOUDARCH004",
        title: "AiRead 只读处理器到达写路径",
        messageFormat: "AiRead handler '{0}' 经调用 '{1}' 到达写操作 '{2}'。原因：AiRead 只能读取 Cloud；最短修复：移除写调用并改用只读 query port。精确例外：授权/审计等横切写入必须位于独立 pipeline behavior，不得藏在 handler/helper",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "通过 Roslyn operation 调用图跨 helper/文件追踪 AiRead handler 到 IRepository 写入、SaveChanges、数据库 Execute 或 ICommand 发送.",
        helpLinkUri: HelpBase + "#cloudarch004-airead-只读",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    internal static readonly DiagnosticDescriptor AiReadAuthorization = new(
        id: "CLOUDARCH005",
        title: "Cloud 请求分类与授权元数据错误",
        messageFormat: "请求 '{0}' 的分类/授权元数据无效：{1}。原因：HTTP request-kind 和授权特性必须在编译期一致；最短修复：AiRead 只用 [AuthorizeAiRead(\"AiRead.*\")]，Human 才可使用 AuthorizeRequirement/AdminOnly，且每个请求只实现一个 request-kind marker。精确例外：无",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IAiReadRequest 必须携带 AuthorizeAiReadAttribute；Human 授权特性不得用于 Device/Bootstrap/Public/AiRead；请求不得混用 request-kind marker.",
        helpLinkUri: HelpBase + "#cloudarch005-授权元数据");

    internal static readonly DiagnosticDescriptor ProductionTestReference = new(
        id: "CLOUDARCH006",
        title: "生产项目引用测试程序集",
        messageFormat: "生产程序集 '{0}' 引用了测试程序集 '{1}'。原因：测试替身不得进入生产依赖图；最短修复：删除生产 ProjectReference/PackageReference 并把替身留在测试项目。精确例外：InternalsVisibleTo 不是程序集引用，允许保留",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "生产 Cloud 项目禁止引用 *Tests、*.Testing、*TestKit* 和常见测试框架程序集.",
        helpLinkUri: HelpBase + "#cloudarch006-生产依赖测试资产",
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
