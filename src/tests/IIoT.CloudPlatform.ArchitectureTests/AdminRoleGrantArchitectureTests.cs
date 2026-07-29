using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IIoT.CloudPlatform.ArchitectureTests;

public sealed class AdminRoleGrantArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
        AllowedUserManagerRoleGrantMethods =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs"] =
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["CreateFirstAdminAsync"] = "SystemRoles.Admin"
                    },
                ["src/infrastructure/IIoT.EntityFrameworkCore/Identity/IdentityAccountStore.cs"] =
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["AssignRoleAsync"] = "normalizedRoleName",
                        ["ReplaceAssignableRoleAsync"] = "canonicalRoleName"
                    }
            };

    [Fact]
    public void ProductionAdminRoleGrant_ShouldRemainOnControlledSeedPath()
    {
        var sourceRoot = CloudRepositoryPath.Find("src");
        var grants = EnumerateProductionSources(sourceRoot)
            .SelectMany(source => FindRoleGrantViolations(
                source.RelativePath,
                source.Content))
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(grants);

        var seedPath =
            "src/hosts/IIoT.MigrationWorkApp/SeedData/SystemInitData.cs";
        var seedSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src",
            "hosts",
            "IIoT.MigrationWorkApp",
            "SeedData",
            "SystemInitData.cs"));
        var seedCalls = FindAddToRoleCalls(seedPath, seedSource);
        var seedCall = Assert.Single(seedCalls);
        Assert.Equal("CreateFirstAdminAsync", seedCall.MethodName);
        Assert.Equal("AddToRoleAsync", seedCall.ApiName);
        Assert.Equal("SystemRoles.Admin", seedCall.RoleArgument);

        AssertAssignableRoleMethodsKeepAdminLikeGuard();
    }

    [Fact]
    public void AdminRoleGrantGuard_ShouldRejectNewDirectOrRawAssignments()
    {
        const string directGrant =
            """
            public sealed class RogueSeeder
            {
                public Task Grant(UserManager<ApplicationUser> userManager, ApplicationUser user)
                    => userManager.AddToRoleAsync(user, SystemRoles.Admin);
            }
            """;
        const string directJoinWrite =
            """
            public sealed class RogueStore
            {
                public void Grant(IIoTDbContext dbContext, Guid userId, Guid roleId)
                    => dbContext.UserRoles.Add(new IdentityUserRole<Guid>
                    {
                        UserId = userId,
                        RoleId = roleId
                    });
            }
            """;
        const string bulkGrant =
            """
            public sealed class RogueBulkSeeder
            {
                public Task Grant(UserManager<ApplicationUser> userManager, ApplicationUser user)
                    => userManager.AddToRolesAsync(user, [SystemRoles.Admin]);
            }
            """;
        const string rawSqlWrite =
            """
            public sealed class RogueSqlStore
            {
                private const string Sql =
                    "INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES (@u, @r)";
            }
            """;
        const string migrationInsertData =
            """
            public sealed class RogueMigration
            {
                protected void Up(MigrationBuilder migrationBuilder)
                {
                    migrationBuilder.InsertData(
                        table: "AspNetUserRoles",
                        columns: ["UserId", "RoleId"],
                        values: new object[] { Guid.NewGuid(), Guid.NewGuid() });
                }
            }
            """;

        Assert.Single(FindRoleGrantViolations(
            "src/services/RogueSeeder.cs",
            directGrant));
        Assert.NotEmpty(FindRoleGrantViolations(
            "src/infrastructure/RogueStore.cs",
            directJoinWrite));
        Assert.Single(FindRoleGrantViolations(
            "src/services/RogueBulkSeeder.cs",
            bulkGrant));
        Assert.Single(FindRoleGrantViolations(
            "src/infrastructure/RogueSqlStore.cs",
            rawSqlWrite));
        Assert.Single(FindRoleGrantViolations(
            "src/infrastructure/IIoT.EntityFrameworkCore/Migrations/RogueMigration.cs",
            migrationInsertData));
    }

    private static IEnumerable<SourceFile> EnumerateProductionSources(
        string sourceRoot)
    {
        var repositoryRoot = Path.GetDirectoryName(
                                 CloudRepositoryPath.Find(
                                     "IIoT.CloudPlatform.slnx"))
                             ?? throw new DirectoryNotFoundException(
                                 "Could not locate the Cloud repository root.");
        foreach (var path in Directory.GetFiles(
                     sourceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var normalized = path.Replace('\\', '/');
            var isGeneratedMigrationMetadata = normalized.Contains(
                    "/IIoT.EntityFrameworkCore/Migrations/",
                    StringComparison.Ordinal)
                && (normalized.EndsWith(
                        ".Designer.cs",
                        StringComparison.Ordinal)
                    || normalized.EndsWith(
                        "/IIoTDbContextModelSnapshot.cs",
                        StringComparison.Ordinal));
            if (normalized.Contains("/src/tests/", StringComparison.Ordinal)
                || normalized.Contains("/src/testing/", StringComparison.Ordinal)
                || normalized.Contains("/bin/", StringComparison.Ordinal)
                || normalized.Contains("/obj/", StringComparison.Ordinal)
                || isGeneratedMigrationMetadata)
            {
                continue;
            }

            yield return new SourceFile(
                Path.GetRelativePath(
                        repositoryRoot,
                        path)
                    .Replace('\\', '/'),
                File.ReadAllText(path));
        }
    }

    private static IEnumerable<string> FindRoleGrantViolations(
        string relativePath,
        string source)
    {
        foreach (var call in FindAddToRoleCalls(relativePath, source))
        {
            if (!AllowedUserManagerRoleGrantMethods.TryGetValue(
                    relativePath,
                    out var allowedMethods)
                || !allowedMethods.TryGetValue(
                    call.MethodName,
                    out var expectedRoleArgument)
                || call.ApiName != "AddToRoleAsync"
                || call.RoleArgument != expectedRoleArgument)
            {
                yield return
                    $"{relativePath}:{call.Line}:{call.ApiName}:{call.MethodName}:{call.RoleArgument}";
            }
        }

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var invocation in root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            if (IsUserRoleInsertData(invocation))
            {
                var insertLine = invocation.GetLocation()
                    .GetLineSpan()
                    .StartLinePosition.Line + 1;
                yield return
                    $"{relativePath}:{insertLine}:AspNetUserRolesInsertData";
                continue;
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax member
                || member.Name.Identifier.ValueText is not ("Add" or "AddAsync")
                || !member.Expression.ToString().EndsWith(
                    "UserRoles",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var line = invocation.GetLocation()
                .GetLineSpan()
                .StartLinePosition.Line + 1;
            yield return $"{relativePath}:{line}:DirectUserRolesWrite";
        }

        if (root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Any(creation => creation.Type.ToString().Contains(
                "IdentityUserRole",
                StringComparison.Ordinal)))
        {
            yield return $"{relativePath}:IdentityUserRoleConstruction";
        }

        var compactSource = string.Concat(
                source.Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Where(character => !char.IsWhiteSpace(character)))
            .Replace("\"", string.Empty, StringComparison.Ordinal);
        if (compactSource.Contains(
                "INSERTINTOAspNetUserRoles",
                StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{relativePath}:RawAspNetUserRolesInsert";
        }
    }

    private static bool IsUserRoleInsertData(
        InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member
            || member.Name.Identifier.ValueText != "InsertData")
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var tableArgument = arguments.FirstOrDefault(argument =>
                                argument.NameColon?.Name.Identifier.ValueText
                                == "table")
                            ?? arguments.FirstOrDefault();
        return tableArgument?.Expression is LiteralExpressionSyntax literal
               && literal.Token.Value is string
               && string.Equals(
                   literal.Token.ValueText,
                   "AspNetUserRoles",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<RoleGrantCall> FindAddToRoleCalls(
        string relativePath,
        string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText is
                    ("AddToRoleAsync" or "AddToRolesAsync"))
            .Select(invocation =>
            {
                var member = (MemberAccessExpressionSyntax)invocation.Expression;
                var methodName = invocation.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.ValueText
                    ?? "<unknown>";
                var roleArgument = invocation.ArgumentList.Arguments.Count > 1
                    ? invocation.ArgumentList.Arguments[1].Expression.ToString()
                    : "<missing>";
                var line = invocation.GetLocation()
                    .GetLineSpan()
                    .StartLinePosition.Line + 1;
                return new RoleGrantCall(
                    relativePath,
                    methodName,
                    member.Name.Identifier.ValueText,
                    roleArgument,
                    line);
            })
            .ToArray();
    }

    private static void AssertAssignableRoleMethodsKeepAdminLikeGuard()
    {
        var storeSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src",
            "infrastructure",
            "IIoT.EntityFrameworkCore",
            "Identity",
            "IdentityAccountStore.cs"));
        var root = CSharpSyntaxTree.ParseText(storeSource).GetRoot();

        foreach (var methodName in new[]
                 {
                     "AssignRoleAsync",
                     "ReplaceAssignableRoleAsync"
                 })
        {
            var method = Assert.Single(
                root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>(),
                candidate => candidate.Identifier.ValueText == methodName);
            Assert.Contains(
                method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation =>
                    invocation.Expression is MemberAccessExpressionSyntax member
                    && member.Expression.ToString() == "SystemRoles"
                    && member.Name.Identifier.ValueText is
                        ("IsAdminLike" or "ContainsAdminLike"));
        }
    }

    private sealed record SourceFile(string RelativePath, string Content);

    private sealed record RoleGrantCall(
        string RelativePath,
        string MethodName,
        string ApiName,
        string RoleArgument,
        int Line);
}
