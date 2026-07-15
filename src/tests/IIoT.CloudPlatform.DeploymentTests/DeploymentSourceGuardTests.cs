using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace IIoT.CloudPlatform.DeploymentTests;

public sealed class DeploymentSourceGuardTests
{
    private const string KnownWeakSeedAdminPassword = "Ljh123456!";
    private const string KnownWeakJwtSecret = "iiot-cloud-jwt-secret-2026-04-22";

    [Fact]
    public void AppHostConfiguration_ShouldNotCommitDefaultDatabasePasswordOrRealRegistryValues()
    {
        var appHostSettingsSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "appsettings.json"));
        var rootAspirateSource = File.ReadAllText(CloudRepositoryPath.Find("aspirate.json"));
        var appHostAspirateSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "aspirate.json"));

        appHostSettingsSource.Should().NotContain("pg-password");
        appHostSettingsSource.Should().NotContain("123456");
        rootAspirateSource.Should().Contain("registry.example.com/iiot");
        rootAspirateSource.Should().NotContain("10.98.90.154");
        appHostAspirateSource.Should().Contain("registry.example.com");
        appHostAspirateSource.Should().NotContain("10.98.90.154");
    }

    [Fact]
    public void AppHostLaunchSettings_ShouldNotDefineProxyEnvironmentVariables()
    {
        var launchSettingsSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "Properties", "launchSettings.json"));

        launchSettingsSource.Should().NotContain("HTTP_PROXY");
        launchSettingsSource.Should().NotContain("HTTPS_PROXY");
        launchSettingsSource.Should().NotContain("ALL_PROXY");
        launchSettingsSource.Should().NotContain("http_proxy");
        launchSettingsSource.Should().NotContain("https_proxy");
        launchSettingsSource.Should().NotContain("all_proxy");
        launchSettingsSource.Should().NotContain("NO_PROXY");
        launchSettingsSource.Should().NotContain("no_proxy");
    }

    [Fact]
    public void AppHostTestingSeam_ShouldNotSelfAuthorizeTheDataWorkerEnvironment()
    {
        var appHostSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "AppHost.cs"));
        var fixtureSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "testing", "IIoT.CloudPlatform.IntegrationTestKit", "IIoTAppFixture.cs"));

        appHostSource.Should().Contain("AppHostTestingGuard.EnsureAllowed(");
        appHostSource.Should().Contain("builder.Environment.EnvironmentName");
        appHostSource.Should().NotContain(
            ".WithEnvironment(\"DOTNET_ENVIRONMENT\", \"Testing\")");
        appHostSource.Should().Contain(
            ".WithEnvironment(\"DOTNET_ENVIRONMENT\", builder.Environment.EnvironmentName)");
        fixtureSource.Should().Contain(
            "SetEnvironmentVariable(\"DOTNET_ENVIRONMENT\", \"Testing\")");
    }

    [Fact]
    public void TestRuntimeCredentials_ShouldNotUseKnownWeakDeploymentSecrets()
    {
        var fixtureSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src", "testing", "IIoT.CloudPlatform.IntegrationTestKit", "IIoTAppFixture.cs"));

        fixtureSource.Should().NotContain($"= \"{KnownWeakSeedAdminPassword}\"");
        fixtureSource.Should().NotContain($"= \"{KnownWeakJwtSecret}\"");
        fixtureSource.Should().Contain(
            "SeedAdminPassword = $\"E2eSeed-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}!\";");
        fixtureSource.Should().Contain(
            "TestJwtSecret = $\"iiot-e2e-{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}\";");
        fixtureSource.Should().Contain("SetEnvironmentVariable(\"SEED_ADMIN_PASSWORD\", SeedAdminPassword)");
        fixtureSource.Should().Contain("SetEnvironmentVariable(\"JWTSETTINGS__SECRET\", TestJwtSecret)");
    }

    [Fact]
    public void DeployPrecheck_ShouldRejectKnownWeakDeploymentSecrets()
    {
        var preDeploySource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "pre-deploy-check.sh"));
        var releaseCommonSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "release-common.sh"));

        preDeploySource.Should().Contain("ensure_required_secret_values_changed");
        preDeploySource.Should().Contain("ensure_required_public_values_changed");
        var jwtSecretGuardLine = Regex.Match(
            releaseCommonSource,
            "require_changed_secret_value[\\s\\\\\\r\\n]+JWTSETTINGS__SECRET[\\s\\S]*?iiot-cloud-jwt-secret-2026-04-22").Value;
        var seedAdminPasswordGuardLine = Regex.Match(
            releaseCommonSource,
            "require_changed_secret_value[\\s\\\\\\r\\n]+SEED_ADMIN_PASSWORD[\\s\\S]*?Ljh123456!").Value;

        releaseCommonSource.Should().Contain("PG_PASSWORD");
        releaseCommonSource.Should().Contain("__REPLACE_POSTGRES_PASSWORD__");
        releaseCommonSource.Should().Contain("RABBITMQ_DEFAULT_PASS");
        releaseCommonSource.Should().Contain("__REPLACE_RABBITMQ_PASSWORD__");
        releaseCommonSource.Should().Contain("SEQ_ADMIN_PASSWORD");
        releaseCommonSource.Should().Contain("__REPLACE_SEQ_PASSWORD__");
        releaseCommonSource.Should().Contain("ensure_required_public_values_changed");
        releaseCommonSource.Should().Contain("internal.example");
        jwtSecretGuardLine.Should().Contain("change-me-jwt-secret");
        jwtSecretGuardLine.Should().Contain("__REPLACE_JWT_SECRET__");
        jwtSecretGuardLine.Should().Contain(KnownWeakJwtSecret);
        seedAdminPasswordGuardLine.Should().Contain("change-me-admin-password");
        seedAdminPasswordGuardLine.Should().Contain("__REPLACE_ADMIN_PASSWORD__");
        seedAdminPasswordGuardLine.Should().Contain(KnownWeakSeedAdminPassword);
    }

    [Fact]
    public void ProductionRedeployGuide_ShouldPreserveOnlyClientReleaseHistory()
    {
        var guide = File.ReadAllText(CloudRepositoryPath.Find("docs", "三项目生产清空重部署对齐手册.md"));
        var exportScript = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "export-client-release-history.sh"));
        var importScript = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "import-client-release-history.sh"));

        guide.Should().Contain("唯一需要保留的数据是 Cloud 客户端更新历史元数据");
        guide.Should().Contain("不保留");
        guide.Should().Contain("AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS");
        guide.Should().Contain("RS256/JWKS");
        guide.Should().Contain("JwtSettings.Validate()");
        guide.Should().Contain("连续失败 5 次");
        exportScript.Should().Contain("edge_client_release_components");
        exportScript.Should().Contain("edge_client_release_versions");
        exportScript.Should().Contain("edge_client_release_artifacts");
        exportScript.Should().Contain("edge_client_release_retention_policies");
        importScript.Should().Contain("ALLOW_CLIENT_RELEASE_HISTORY_IMPORT_OVERWRITE");
        importScript.Should().Contain("edge_client_release_versions");
    }

    [Fact]
    public void LocalDeployEnv_WhenPresent_ShouldNotUseKnownWeakDeploymentSecrets()
    {
        var envPath = CloudRepositoryPath.Find("deploy", ".env");
        if (!File.Exists(envPath))
        {
            return;
        }

        var envSource = File.ReadAllText(envPath);

        envSource.Should().NotContain($"JWTSETTINGS__SECRET={KnownWeakJwtSecret}");
        envSource.Should().NotContain($"SEED_ADMIN_PASSWORD={KnownWeakSeedAdminPassword}");
    }

    [Fact]
    public void CloudContainerNonRoot_ShouldHaveExplicitPermissionContractBeforeUserSwitch()
    {
        var contractSource = File.ReadAllText(CloudRepositoryPath.Find("docs", "云端容器非Root权限契约.md"));
        var governanceSource = File.ReadAllText(CloudRepositoryPath.Find("docs", "云端架构治理清单.md"));
        var composeSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "docker-compose.prod.yml"));
        var httpApiDockerfile = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "Dockerfile"));
        var gatewayDockerfile = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.Gateway", "Dockerfile"));
        var dataWorkerDockerfile = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.DataWorker", "Dockerfile"));
        var migrationDockerfile = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.MigrationWorkApp", "Dockerfile"));
        var webDockerfile = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "iiot-web.Dockerfile"));
        var webNginxSource = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "iiot-web.nginx.conf"));
        var nginxSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "nginx", "nginx.conf"));
        var readinessScriptSource = File.ReadAllText(CloudRepositoryPath.Find(
            "deploy",
            "scripts",
            "check-container-nonroot-readiness.sh"));
        var oidcSigningCertScriptSource = File.ReadAllText(CloudRepositoryPath.Find(
            "deploy",
            "scripts",
            "ensure-oidc-signing-cert.sh"));
        var preDeploySource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "pre-deploy-check.sh"));

        contractSource.Should().Contain("当前文档是 C-06 的执行契约");
        contractSource.Should().Contain("OIDC_PROVIDER_CERTS_DIR");
        contractSource.Should().Contain("EDGE_UPDATES_DIR");
        contractSource.Should().Contain("PFX 文件");
        contractSource.Should().Contain("deploy/scripts/check-container-nonroot-readiness.sh");
        contractSource.Should().Contain("web/nginx 容器不再靠 root 绑定容器内 `80`");
        contractSource.Should().Contain("nonroot_readiness_nginx_internal_port=8080");
        contractSource.Should().Contain("nonroot_readiness_log_volume_httpapi_logs");
        contractSource.Should().Contain("nonroot_readiness_edge_updates_subdir_<name>=missing");
        contractSource.Should().Contain("nonroot_readiness_edge_updates_subdir_<name>=failed");
        contractSource.Should().Contain("该诊断不得替代应用启动验收");
        contractSource.Should().Contain("Edge installer bundle 上传");
        contractSource.Should().Contain("plugin package 上传");
        contractSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_VERSION");
        contractSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_MODULE_ID");
        contractSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_VERSION");
        contractSource.Should().Contain("iiot-migration");
        governanceSource.Should().Contain("状态：代码和发布前门禁已实施，仍需生产真实启动验收后关闭");
        governanceSource.Should().Contain("docs/云端容器非Root权限契约.md");
        governanceSource.Should().Contain("deploy/scripts/check-container-nonroot-readiness.sh");
        governanceSource.Should().Contain("pre-deploy-check.sh` 已接入 `check-container-nonroot-readiness.sh");
        governanceSource.Should().Contain("既有 `installers/plugins/velopack` 子目录权限不满足时会 fail-fast");
        governanceSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_VERSION");
        governanceSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_MODULE_ID");
        governanceSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_VERSION");
        governanceSource.Should().Contain("web/nginx 非特权内部端口");
        composeSource.Should().Contain("${OIDC_PROVIDER_CERTS_DIR:-/data/iiot-platform/cloud/deploy/certs}:/app/certs:ro");
        composeSource.Should().Contain("${EDGE_UPDATES_DIR:-/data/iiot-platform/edge-client/edge-updates}:/app/edge-updates:rw");
        composeSource.Should().Contain("${EDGE_UPDATES_DIR:-/data/iiot-platform/edge-client/edge-updates}:/usr/share/nginx/html/edge-updates:ro");
        composeSource.Should().NotContain("/srv/iiot-cloud/deploy");
        composeSource.Should().NotContain("/srv/iiot/edge-updates");
        composeSource.Should().Contain("user: ${NGINX_UID:-101}:${NGINX_GID:-101}");
        composeSource.Should().Contain("${GATEWAY_HTTP_PORT:-80}:8080");
        readinessScriptSource.Should().Contain("CLOUD_CONTAINER_UID");
        readinessScriptSource.Should().Contain("OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH");
        readinessScriptSource.Should().Contain("path_readable_by_container");
        readinessScriptSource.Should().Contain("directory_writable_by_container");
        readinessScriptSource.Should().Contain("digit_has_write()");
        readinessScriptSource.Should().Contain("if digit_has_write \"$(mode_digit \"$mode\" other)\"; then");
        readinessScriptSource.Should().Contain("EDGE_UPDATES_DIR is not writable");
        readinessScriptSource.Should().Contain("/data/iiot-platform/edge-client/edge-updates");
        readinessScriptSource.Should().NotContain("/srv/iiot/edge-updates");
        readinessScriptSource.Should().Contain("for subdir_name in installers plugins velopack; do");
        readinessScriptSource.Should().Contain("check_edge_updates_subdirectory");
        readinessScriptSource.Should().Contain("nonroot_readiness_edge_updates_subdir_%s=missing path=%s");
        readinessScriptSource.Should().Contain("nonroot_readiness_edge_updates_subdir_%s=failed path=%s");
        readinessScriptSource.Should().Contain("EDGE_UPDATES_DIR subdirectory is not writable");
        readinessScriptSource.Should().Contain("must not be world-readable");
        readinessScriptSource.Should().Contain("stat -f '%u %g %Lp'");
        readinessScriptSource.Should().Contain("nginx-gateway must not listen on container port 80");
        readinessScriptSource.Should().Contain("nginx-gateway must not proxy iiot-web on container port 80");
        readinessScriptSource.Should().Contain("nginx-gateway compose port target must not be container port 80");
        readinessScriptSource.Should().Contain("nginx-gateway must listen on container port 8080");
        readinessScriptSource.Should().Contain("nginx-gateway must proxy iiot-web on container port 8080");
        readinessScriptSource.Should().Contain("nginx-gateway compose port target must be container port 8080");
        readinessScriptSource.Should().Contain("compose_nginx_gateway_targets_port()");
        readinessScriptSource.Should().Contain("nonroot_readiness_nginx_internal_port=8080");
        readinessScriptSource.Should().NotContain("nonroot_readiness_warning=nginx services still bind container port 80");
        readinessScriptSource.Should().Contain("log_volume_writability_diagnostic httpapi_logs httpapi-logs");
        readinessScriptSource.Should().Contain("nonroot_readiness_log_volume_%s=not-created");
        readinessScriptSource.Should().Contain("nonroot_readiness_log_volume_%s=exists volume=%s writable_by_container=%s world_writable=%s");
        oidcSigningCertScriptSource.Should().Contain("CONTAINER_UID=${CLOUD_CONTAINER_UID:-10001}");
        oidcSigningCertScriptSource.Should().Contain("CONTAINER_GID=${CLOUD_CONTAINER_GID:-10001}");
        oidcSigningCertScriptSource.Should().Contain("if [ \"$(id -u)\" = \"$CONTAINER_UID\" ]; then");
        oidcSigningCertScriptSource.Should().Contain("chmod 600 \"$certificate\"");
        oidcSigningCertScriptSource.Should().Contain("chgrp \"$CONTAINER_GID\" \"$certificate\"");
        oidcSigningCertScriptSource.Should().Contain("chmod 640 \"$certificate\"");
        oidcSigningCertScriptSource.Should().Contain("path_readable_by_container");
        oidcSigningCertScriptSource.Should().Contain("path_world_readable");
        oidcSigningCertScriptSource.Should().Contain("OIDC signing certificate already exists and is container-readable");
        oidcSigningCertScriptSource.Should().Contain("Fix ownership/mode before deploy");
        oidcSigningCertScriptSource.Should().Contain("rm -f \"$target_certificate\"");
        preDeploySource.Should().Contain("sh \"$SCRIPT_DIR/check-container-nonroot-readiness.sh\"");
        nginxSource.Should().Contain("pid /tmp/nginx.pid;");
        nginxSource.Should().Contain("listen 8080;");
        nginxSource.Should().Contain("proxy_pass http://iiot-web:8080;");
        webNginxSource.Should().Contain("listen 8080;");
        webDockerfile.Should().Contain("EXPOSE 8080");
        webDockerfile.Should().Contain("USER 101:101");

        foreach (var dockerfile in new[] { httpApiDockerfile, gatewayDockerfile, dataWorkerDockerfile, migrationDockerfile })
        {
            dockerfile.Should().Contain("ARG APP_UID=10001");
            dockerfile.Should().Contain("ARG APP_GID=10001");
            dockerfile.Should().Contain("groupadd --gid \"${APP_GID}\" iiot");
            dockerfile.Should().Contain("useradd --uid \"${APP_UID}\" --gid \"${APP_GID}\"");
            dockerfile.Should().Contain("COPY --from=build --chown=${APP_UID}:${APP_GID} /app/publish .");
            dockerfile.Should().Contain("USER ${APP_UID}:${APP_GID}");
        }
    }

    [Fact]
    public void CloudDocs_ShouldPreserveChangeClosureRules()
    {
        var agentsSource = File.ReadAllText(CloudRepositoryPath.Find("AGENTS.md"));
        var cloudRulesSource = File.ReadAllText(CloudRepositoryPath.Find("docs", "云端规则.md"));
        var retrospectiveSource = File.ReadAllText(CloudRepositoryPath.Find("docs", "改动复盘与规则沉淀.md"));
        var combinedDocs = string.Join(
            Environment.NewLine,
            agentsSource,
            cloudRulesSource,
            retrospectiveSource);

        agentsSource.Should().Contain("docs/改动复盘与规则沉淀.md");
        cloudRulesSource.Should().Contain("改动收口门禁");
        cloudRulesSource.Should().Contain("已验收功能默认冻结");
        cloudRulesSource.Should().Contain("最终回复必须列出复盘文档、规则沉淀位置和验证命令");
        retrospectiveSource.Should().Contain("项目滚动复盘入口");
        retrospectiveSource.Should().Contain("改动范围");
        retrospectiveSource.Should().Contain("规则提炼");
        retrospectiveSource.Should().Contain("无新增长期规则");

        combinedDocs.Should().Contain("改动复盘");
        combinedDocs.Should().Contain("规则沉淀");
        combinedDocs.Should().Contain("验证命令");
    }

    [Fact]
    public void DataWorkerDockerfile_ShouldUseHealthcheck_AndMigrationWorkAppShouldRemainOneShot()
    {
        var dataWorkerDockerfile = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.DataWorker", "Dockerfile"));
        var dataWorkerProgram = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.DataWorker", "Program.cs"));
        var migrationDockerfile = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.MigrationWorkApp", "Dockerfile"));
        var composeSource = File.ReadAllText(
            CloudRepositoryPath.Find("deploy", "docker-compose.prod.yml"));

        dataWorkerDockerfile.Should().Contain("HEALTHCHECK");
        dataWorkerDockerfile.Should().Contain("dotnet IIoT.DataWorker.dll --healthcheck");
        dataWorkerProgram.Should().Contain("--healthcheck");
        dataWorkerProgram.Should().Contain("CanConnectAsync");
        migrationDockerfile.Should().NotContain("HEALTHCHECK");
        composeSource.Should().Contain("iiot-migration:");
        composeSource.Should().Contain("restart: \"no\"");
        composeSource.Should().Contain("DOTNET_ENVIRONMENT: Production");
        composeSource.Should().NotContain("DataWorker__Testing__DisableOutboxDispatcher");
    }

    [Fact]
    public void DeploymentTemplates_ShouldExternalizeSecretsAndUseStandardDeployEntry()
    {
        var gitIgnoreSource = File.ReadAllText(CloudRepositoryPath.Find(".gitignore"));
        var envExampleSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", ".env.example"));
        var composeSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "docker-compose.prod.yml"));
        var edgeInstallerPublicBaseUrlSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src",
            "services",
            "IIoT.ProductionService",
            "ClientReleases",
            "EdgeInstallerPublicBaseUrl.cs"));
        var edgeBindingDownloadPanelSource = File.ReadAllText(CloudRepositoryPath.Find(
            "src",
            "ui",
            "iiot-web",
            "src",
            "features",
            "client-releases",
            "EdgeBindingDownloadPanel.vue"));

        gitIgnoreSource.Should().Contain("deploy/.env");
        gitIgnoreSource.Should().Contain("deploy/certs/");
        gitIgnoreSource.Should().Contain("aspirate-state.json");
        envExampleSource.Should().Contain("__REPLACE_POSTGRES_PASSWORD__");
        envExampleSource.Should().NotContain("change-me-postgres-password");
        envExampleSource.Should().NotContain("10.98.90.154");
        envExampleSource.Should().Contain("ALLOW_INTRANET_HTTP_OIDC=false");
        envExampleSource.Should().Contain("cloud.internal.example");
        envExampleSource.Should().Contain("aicopilot.internal.example");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE");
        envExampleSource.Should().Contain("PUBLIC_BASE_URL=");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_ENABLED");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_KNOWNNETWORKS__0");
        envExampleSource.Should().Contain("OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH=/app/certs/cloud-oidc-signing.pfx");
        envExampleSource.Should().Contain("EDGE_UPDATES_DIR=/data/iiot-platform/edge-client/edge-updates");
        envExampleSource.Should().Contain("OIDC_PROVIDER_CERTS_DIR=/data/iiot-platform/cloud/deploy/certs");
        envExampleSource.Should().NotContain("/srv/iiot-cloud/deploy");
        envExampleSource.Should().NotContain("/srv/iiot/edge-updates");
        envExampleSource.Should().NotContain("DEPLOY_HOST=");
        envExampleSource.Should().NotContain("DEPLOY_USER=");
        envExampleSource.Should().NotContain("STACK_NAME=");
        envExampleSource.Should().NotContain("SERVER_IP");
        envExampleSource.Should().NotContain("TLS_CERT_FILE");
        envExampleSource.Should().NotContain("TLS_KEY_FILE");
        envExampleSource.Should().NotContain("GATEWAY_HTTPS_PORT");
        envExampleSource.Should().NotContain("BOOTSTRAP_AUTH_REQUIRE_SECRET");

        composeSource.Should().Contain("iiot-gateway:");
        composeSource.Should().Contain("ReverseProxy__Clusters__httpapi__Destinations__primary__Address");
        composeSource.Should().Contain("ForwardedHeaders__Enabled:");
        composeSource.Should().Contain("ForwardedHeaders__KnownNetworks__0:");
        composeSource.Should().Contain("nginx-gateway:");
        composeSource.Should().Contain("EdgeInstallerArtifacts__RootPath: /app/edge-updates/installers");
        composeSource.Should().Contain("${IIOT_NGINX_IMAGE}");
        composeSource.Should().NotContain("SERVER_IP");
        composeSource.Should().NotContain("TLS_CERT_FILE");
        composeSource.Should().NotContain("GATEWAY_HTTPS_PORT");
        composeSource.Should().NotContain("BootstrapAuth__RequireSecret");
        composeSource.Should().NotContain("ALLOW_ANONYMOUS: \"true\"");
        edgeInstallerPublicBaseUrlSource.Should().NotContain("10.98.90.154");
        edgeInstallerPublicBaseUrlSource.Should().Contain("http://cloud-host:81");
        edgeBindingDownloadPanelSource.Should().NotContain("10.98.90.154");
        edgeBindingDownloadPanelSource.Should().Contain("http://cloud-host:81");
    }

    [Fact]
    public void ProductionRuntimeDeployAndWorkflowFiles_ShouldNotUseSiteIpAsDefault()
    {
        var repositoryRoot = Path.GetDirectoryName(CloudRepositoryPath.Find(".gitignore"))
                             ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var allowedHistoricalRecord = Path.Combine(repositoryRoot, "deploy", "README.md");
        var offenders = new List<string>();

        foreach (var file in EnumerateGuardedTextFiles(repositoryRoot, "src", "deploy", ".github"))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (!line.Contains("10.98.90.154", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(file, allowedHistoricalRecord, StringComparison.Ordinal)
                    && line.Contains("2026-06-22 现场校准", StringComparison.Ordinal)
                    && line.Contains("不是模板默认值", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{Path.GetRelativePath(repositoryRoot, file)}:{lineNumber}: {line.Trim()}");
            }
        }

        offenders.Should().BeEmpty("真实现场 IP 只允许出现在标明不是模板默认值的历史校准记录中");
    }

    [Fact]
    public void RollingReview_ShouldNotKeepCopyableSiteIpDeployCommands()
    {
        var rollingReviewSource = File.ReadAllText(CloudRepositoryPath.Find("docs", "改动复盘与规则沉淀.md"));
        var forbiddenCommandPatterns = new[]
        {
            @"root@10\.98\.90\.154",
            @"DEPLOY_SSH_TARGET=.*10\.98\.90\.154",
            @"--ssh-target\s+\S*10\.98\.90\.154",
            @"curl .*http://10\.98\.90\.154",
            @"ssh root@10\.98\.90\.154"
        };

        foreach (var pattern in forbiddenCommandPatterns)
        {
            Regex.IsMatch(rollingReviewSource, pattern, RegexOptions.CultureInvariant)
                .Should()
                .BeFalse($"rolling review history must not keep copyable site IP/root deploy commands matching {pattern}");
        }
    }

    [Fact]
    public void DeployReadme_ShouldNotUseTemplateDomainsForProductionHttpOidcExample()
    {
        var readmeSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "README.md"));

        readmeSource.Should().Contain("HTTP OIDC");
        readmeSource.Should().Contain("loopback 或 RFC1918 私网 IPv4");
        readmeSource.Should().Contain("PUBLIC_BASE_URL=http://<cloud-host>:81");
        readmeSource.Should().Contain("OIDC_PROVIDER_ISSUER=http://<cloud-private-ip>:81");
        readmeSource.Should().Contain(
            "AICOPILOT_OIDC_REDIRECT_URI=http://<aicopilot-private-ip>:82/api/identity/cloud-oidc/callback");
        readmeSource.Should().Contain(
            "AICOPILOT_OIDC_POST_LOGOUT_REDIRECT_URI=http://<aicopilot-private-ip>:82/login");
        readmeSource.Should().Contain(
            "VITE_AICOPILOT_CHALLENGE_URL=http://<aicopilot-browser-reachable-host>:82/api/identity/cloud-oidc/challenge");
        readmeSource.Should().NotContain("OIDC_PROVIDER_ISSUER=http://cloud.internal.example");
        readmeSource.Should().NotContain("AICOPILOT_OIDC_REDIRECT_URI=http://aicopilot.internal.example");
        readmeSource.Should().NotContain(
            "AICOPILOT_OIDC_POST_LOGOUT_REDIRECT_URI=http://aicopilot.internal.example");
        readmeSource.Should().NotContain(
            "VITE_AICOPILOT_CHALLENGE_URL=http://aicopilot.internal.example");
        readmeSource.Should().NotContain("PUBLIC_BASE_URL=http://cloud.internal.example");
    }

    [Fact]
    public void LegacyAspirateDeploymentArtifacts_ShouldBeRemovedFromRepository()
    {
        var repositoryRoot = Path.GetDirectoryName(CloudRepositoryPath.Find(".gitignore"))
                             ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var legacyArtifacts = new[]
        {
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", ".env.example"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "build-push.ps1"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "deploy.ps1"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "docker-compose.yaml"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "nginx.conf"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "部署操作手册.txt")
        };

        legacyArtifacts.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public void GeneratedAspirateStateFiles_ShouldBeIgnoredAndRemovedFromRepository()
    {
        var repositoryRoot = Path.GetDirectoryName(CloudRepositoryPath.Find(".gitignore"))
                             ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var rootStatePath = Path.Combine(repositoryRoot, "aspirate-state.json");
        var appHostStatePath = Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-state.json");

        File.Exists(rootStatePath).Should().BeFalse();
        File.Exists(appHostStatePath).Should().BeFalse();
    }

    [Fact]
    public void DeploymentArtifacts_ShouldIgnoreGeneratedBackupOutputs()
    {
        var gitIgnoreSource = File.ReadAllText(CloudRepositoryPath.Find(".gitignore"));

        gitIgnoreSource.Should().Contain("deploy/backups/");
        gitIgnoreSource.Should().Contain("deploy/releases/");
    }

    [Fact]
    public void StandardDeploymentDocs_ShouldUseLocalBuildPushAndSshDeployPath()
    {
        var readmeSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "README.md"));
        var operationsSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "OPERATIONS.md"));
        var edgeGoLiveSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "EDGE_INSTALLER_GO_LIVE.md"));
        var runnerSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "RUNNER.md"));
        var buildAndPushSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "build-and-push.sh"));
        var localReleaseSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "local-release.sh"));
        var imageWorkflowSource = File.ReadAllText(CloudRepositoryPath.Find(".github", "workflows", "cloud-image.yml"));
        var deployWorkflowSource = File.ReadAllText(CloudRepositoryPath.Find(".github", "workflows", "cloud-deploy.yml"));

        readmeSource.Should().Contain("工作区外部日常入口是 `pwsh ./deploy/Deploy-Changed.ps1 -Targets Cloud`");
        readmeSource.Should().Contain("`cloud-image` / `cloud-deploy` 只保留灾备手动入口，必须输入确认词");
        readmeSource.Should().Contain("单个镜像 build/push 默认 15 分钟超时");
        readmeSource.Should().Contain("日常 `Deploy-Changed.ps1` 内部调用的 `Deploy.ps1` 不安装任何远端 support files");
        readmeSource.Should().Contain("服务器端 `deploy-release.sh` 不再是日常或手工入口");
        readmeSource.Should().Contain("`cloud-ci / build-test`");
        readmeSource.Should().Contain("16 个物理 runner / 661 case");
        operationsSource.Should().Contain("pwsh ./deploy/Deploy-Changed.ps1 -Targets Cloud");
        operationsSource.Should().Contain("project-local `local-release.sh` is a legacy maintenance implementation");
        operationsSource.Should().Contain("`cloud-ci / build-test`");
        operationsSource.Should().Contain("16 physical runners / 661 cases");
        edgeGoLiveSource.Should().Contain("pwsh ./deploy/Deploy-Changed.ps1 -Targets Edge");
        edgeGoLiveSource.Should().Contain("Windows 安装器/Velopack/插件构建");
        edgeGoLiveSource.Should().Contain(
            "scripts/tests/Invoke-CloudTestInventory.ps1 -Mode Required -Configuration Release -NoBuild");
        edgeGoLiveSource.Should().Contain("16 个物理 runner / 661 case");
        runnerSource.Should().Contain("/data/github-runner/cloud");
        runnerSource.Should().Contain("github-runner");
        runnerSource.Should().Contain("self-hosted runner 仅用于 CI 辅助和历史运维");
        runnerSource.Should().Contain("旧 `cloud-deploy` workflow 不产生新契约及 run-bound image manifest");
        runnerSource.Should().Contain("GitHub OIDC + Vault");
        runnerSource.Should().Contain("production environment protection");
        buildAndPushSource.Should().Contain("Cloud local image build requires explicit --services or --all.");
        buildAndPushSource.Should().Contain("IIOT_ROUTINE_BUILD_PROTOCOL=1");
        buildAndPushSource.Should().Contain("REGISTRY must include an explicit Harbor registry host");
        buildAndPushSource.Should().Contain("HARBOR_PROJECT must be a single Harbor project segment");
        buildAndPushSource.Should().Contain("BUILD_TIMEOUT_SECONDS=\"${BUILD_TIMEOUT_SECONDS:-900}\"");
        buildAndPushSource.Should().Contain("HARBOR_TIMEOUT_SECONDS=\"${HARBOR_TIMEOUT_SECONDS:-120}\"");
        buildAndPushSource.Should().Contain("artifact_dir=\"${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy}\"");
        buildAndPushSource.Should().Contain("cloud-built-services.txt");
        buildAndPushSource.Should().Contain("IIOT_HTTPAPI_IMAGE");
        localReleaseSource.Should().Contain("DEPLOY_SSH_TARGET");
        localReleaseSource.Should().Contain("SSH_TIMEOUT_SECONDS=\"${SSH_TIMEOUT_SECONDS:-2400}\"");
        localReleaseSource.Should().Contain("Cloud expected SHA is no longer the freshly fetched $remote/$branch tip");
        localReleaseSource.Should().Contain("scripts/workspace-release-transaction.sh");
        localReleaseSource.Should().Contain("exec bash \\\"\\$transaction_script\\\"");
        imageWorkflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        imageWorkflowSource.Should().Contain("emergency_confirm:");
        imageWorkflowSource.Should().Contain("EMERGENCY_CLOUD_IMAGE_BUILD");
        imageWorkflowSource.Should().NotContain("\n  push:");
        deployWorkflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        deployWorkflowSource.Should().Contain("emergency_confirm:");
        deployWorkflowSource.Should().Contain("EMERGENCY_CLOUD_DEPLOY");
        deployWorkflowSource.Should().Contain("Use deploy/Deploy-Changed.ps1 -Targets Cloud from the workspace root");

        var retiredTestEntrypoints = new[]
        {
            "IIoT.ServiceLayer.Tests",
            "IIoT.ProductionService.Tests",
            "IIoT.EndToEndTests",
            "ConfigurationGuardTests",
            "--filter"
        };
        foreach (var documentationSource in new[] { readmeSource, operationsSource, edgeGoLiveSource })
        {
            foreach (var retiredTestEntrypoint in retiredTestEntrypoints)
            {
                documentationSource.Should().NotContain(
                    retiredTestEntrypoint,
                    "active deployment documentation must use the physical inventory runner instead of retired test buckets or filters");
            }
        }

        foreach (var source in new[] { readmeSource, runnerSource, imageWorkflowSource, deployWorkflowSource, buildAndPushSource, localReleaseSource })
        {
            source.Should().NotContain("appleboy/ssh-action");
            source.Should().NotContain("appleboy/scp-action");
            source.Should().NotContain("DEPLOY_HOST");
            source.Should().NotContain("ghcr.io");
        }
    }

    [Fact]
    public void DeployTemplates_ShouldDocumentSingleNodeSecretsAndSmokePolicy()
    {
        var readmeSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "README.md"));
        var envExampleSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", ".env.example"));
        var composeSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "docker-compose.prod.yml"));

        readmeSource.Should().Contain("single-machine production starter");
        readmeSource.Should().Contain("`release_tag = sha-*`");
        readmeSource.Should().Contain("日常部署必须由 `Deploy-Changed.ps1` 确认 clean/main 并自动 push GitHub");
        readmeSource.Should().Contain("pwsh ./deploy/Deploy-Changed.ps1 -Targets Cloud");
        readmeSource.Should().Contain("禁止把 `cloud-image` 或 `cloud-deploy` 当成日常部署入口");
        readmeSource.Should().Contain("传入 `services` 时只拉取并重启指定服务");
        readmeSource.Should().Contain("版本集合只来自 Cloud release 记录");
        readmeSource.Should().Contain("文件系统只校验已登记 artifact");
        readmeSource.Should().Contain("不得扫描目录补出未登记版本");
        readmeSource.Should().NotContain("Cloud catalog 会扫描");
        readmeSource.Should().Contain("专用非 root 用户");
        readmeSource.Should().Contain("Docker Hub 不作为生产依赖源");
        readmeSource.Should().Contain("Edge 客户端安装素材不进 Harbor");
        readmeSource.Should().Contain("应用镜像仓库只保留当前生产 `sha-*` tag");
        readmeSource.Should().Contain("EdgeInstallerArtifacts__RootPath=/app/edge-updates/installers");
        readmeSource.Should().Contain("EdgeInstallerArtifacts__VelopackReleasesBaseUrl=${PUBLIC_BASE_URL}/edge-updates/velopack");
        readmeSource.Should().Contain("[RUNNER.md](./RUNNER.md)");
        readmeSource.Should().Contain("GET /internal/healthz");
        readmeSource.Should().Contain("[OPERATIONS.md](./OPERATIONS.md)");
        readmeSource.Should().Contain("deploy/scripts/deploy-release.sh");
        readmeSource.Should().Contain("deploy/scripts/rollback-release.sh");
        readmeSource.Should().Contain("deploy/releases/current-release.env");
        readmeSource.Should().Contain("deploy/cron/iiot-backup.cron.example");
        readmeSource.Should().Contain("deploy/cron/iiot-backup-verify.cron.example");
        readmeSource.Should().Contain("deploy/cron/iiot-post-release-cleanup.cron.example");
        readmeSource.Should().Contain("daily backup at `02:30`");
        readmeSource.Should().Contain("weekly restore verification at `03:30` every Sunday");
        readmeSource.Should().Contain("`latest` 不能作为生产应用版本");

        envExampleSource.Should().Contain("Must replace: Harbor application image repositories");
        envExampleSource.Should().Contain("Must replace: runtime secrets");
        envExampleSource.Should().Contain("Template defaults: single-machine published ports");
        envExampleSource.Should().Contain("operator-managed .env");
        envExampleSource.Should().Contain("pushed to Harbor");
        envExampleSource.Should().Contain("Infrastructure__EventBus__EndpointPrefix=");
        envExampleSource.Should().Contain("IIOT_HTTPAPI_IMAGE=harbor.example.com/iiot/iiot-httpapi:sha-");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE=harbor.example.com/iiot/iiot-gateway:sha-");
        envExampleSource.Should().Contain("IIOT_DATAWORKER_IMAGE=harbor.example.com/iiot/iiot-dataworker:sha-");
        envExampleSource.Should().Contain("IIOT_MIGRATION_IMAGE=harbor.example.com/iiot/iiot-migrationworkapp:sha-");
        envExampleSource.Should().Contain("IIOT_WEB_IMAGE=harbor.example.com/iiot/iiot-web:sha-");
        envExampleSource.Should().NotContain("IIOT_HTTPAPI_IMAGE=harbor.example.com/iiot/iiot-httpapi:latest");
        envExampleSource.Should().Contain("IIOT_NGINX_IMAGE=harbor.example.com/mirror/nginx:1.27-alpine");
        envExampleSource.Should().Contain("IIOT_POSTGRES_IMAGE=harbor.example.com/mirror/timescaledb:latest-pg17");
        envExampleSource.Should().Contain("IIOT_REDIS_IMAGE=harbor.example.com/mirror/redis:7.4-alpine");
        envExampleSource.Should().Contain("IIOT_RABBITMQ_IMAGE=harbor.example.com/mirror/rabbitmq:3-management-alpine");
        envExampleSource.Should().Contain("IIOT_SEQ_IMAGE=harbor.example.com/mirror/seq:2024.3");
        envExampleSource.Should().NotContain("IIOT_NGINX_IMAGE=nginx:");
        envExampleSource.Should().NotContain("IIOT_POSTGRES_IMAGE=timescale/");
        envExampleSource.Should().NotContain("IIOT_REDIS_IMAGE=redis:");
        envExampleSource.Should().NotContain("IIOT_RABBITMQ_IMAGE=rabbitmq:");
        envExampleSource.Should().NotContain("IIOT_SEQ_IMAGE=datalust/");
        envExampleSource.Should().Contain("BACKUP_RETENTION_DAYS=14");
        envExampleSource.Should().Contain("BACKUP_MAX_AGE_HOURS=24");
        envExampleSource.Should().Contain("BACKUP_VERIFY_MAX_AGE_DAYS=7");
        envExampleSource.Should().Contain("GATEWAY_HTTP_PORT=81");
        envExampleSource.Should().NotContain("BOOTSTRAP_AUTH_REQUIRE_SECRET");
        envExampleSource.Should().Contain("X-IIoT-Bootstrap-Secret");
        envExampleSource.Should().Contain("installers/stable/{version}");
        envExampleSource.Should().Contain("POSTGRES_MEM_LIMIT=4g");
        envExampleSource.Should().Contain("HTTPAPI_MEM_LIMIT=1536m");
        envExampleSource.Should().Contain("DATAWORKER_MEM_LIMIT=1536m");
        envExampleSource.Should().Contain("OUTBOX_BATCH_SIZE=500");
        envExampleSource.Should().Contain("OUTBOX_POLLING_INTERVAL_SECONDS=1");
        envExampleSource.Should().Contain("PASS_STATION_CONSUMER_CONCURRENCY=16");
        envExampleSource.Should().Contain("RATE_LIMIT_PASS_STATION_UPLOAD_TOKEN_LIMIT=3000");

        composeSource.Should().Contain("Single-machine production starter for IIoT.CloudPlatform.");
        composeSource.Should().Contain("Single-node launch keeps one explicit upstream destination.");
        composeSource.Should().Contain("Infrastructure__EventBus__EndpointPrefix:");
        composeSource.Should().NotContain("BootstrapAuth__RequireSecret");
        composeSource.Should().Contain("EdgeInstallerArtifacts__RootPath: /app/edge-updates/installers");
        composeSource.Should().Contain("EdgeInstallerArtifacts__VelopackReleasesBaseUrl: ${PUBLIC_BASE_URL}/edge-updates/velopack");
        composeSource.Should().Contain("${EDGE_UPDATES_DIR:-/data/iiot-platform/edge-client/edge-updates}:/app/edge-updates:rw");
        composeSource.Should().Contain("${EDGE_UPDATES_DIR:-/data/iiot-platform/edge-client/edge-updates}:/usr/share/nginx/html/edge-updates:ro");
        composeSource.Should().Contain("${OIDC_PROVIDER_CERTS_DIR:-/data/iiot-platform/cloud/deploy/certs}:/app/certs:ro");
        composeSource.Should().NotContain("/srv/iiot-cloud/deploy");
        composeSource.Should().NotContain("/srv/iiot/edge-updates");
        composeSource.Should().Contain("postgres:");
        composeSource.Should().Contain("mem_limit: ${POSTGRES_MEM_LIMIT:-4g}");
        composeSource.Should().Contain("cpus: ${POSTGRES_CPUS:-2.0}");
        composeSource.Should().Contain("redis-cache:");
        composeSource.Should().Contain("mem_limit: ${REDIS_MEM_LIMIT:-512m}");
        composeSource.Should().Contain("rabbitmq:");
        composeSource.Should().Contain("mem_limit: ${RABBITMQ_MEM_LIMIT:-1g}");
        composeSource.Should().Contain("seq:");
        composeSource.Should().Contain("nginx-gateway:");
        composeSource.Should().Contain("mem_limit: ${NGINX_MEM_LIMIT:-256m}");
        composeSource.Should().Contain("mem_limit: ${HTTPAPI_MEM_LIMIT:-1536m}");
        composeSource.Should().Contain("mem_limit: ${DATAWORKER_MEM_LIMIT:-1536m}");
        composeSource.Should().Contain("Outbox__BatchSize: ${OUTBOX_BATCH_SIZE:-500}");
        composeSource.Should().Contain("Outbox__PollingIntervalSeconds: ${OUTBOX_POLLING_INTERVAL_SECONDS:-1}");
        composeSource.Should().Contain("Infrastructure__EventBus__Consumers__PassStationConcurrentMessageLimit: ${PASS_STATION_CONSUMER_CONCURRENCY:-16}");
        composeSource.Should().Contain("RateLimiting__PassStationUpload__TokenLimit: ${RATE_LIMIT_PASS_STATION_UPLOAD_TOKEN_LIMIT:-3000}");
    }

    [Fact]
    public void HarborDeployReadme_ShouldDocumentRegistryOwnershipAndPrivateServerFlow()
    {
        var documentationSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "README.md"));
        var envExampleSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", ".env.example"));

        documentationSource.Should().Contain("Harbor 变量");
        documentationSource.Should().Contain("OCI_REGISTRY");
        documentationSource.Should().Contain("OCI_NAMESPACE");
        documentationSource.Should().Contain("OCI_REGISTRY_USERNAME");
        documentationSource.Should().Contain("OCI_REGISTRY_PASSWORD");
        documentationSource.Should().Contain("iiot-linux-prod");
        documentationSource.Should().Contain("github-runner");
        documentationSource.Should().Contain("Docker Hub 不作为生产依赖源");
        documentationSource.Should().Contain("mirror-third-party-images.sh");
        documentationSource.Should().Contain("docker login <OCI_REGISTRY>");
        documentationSource.Should().Contain("docker compose pull");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE=harbor.example.com/iiot/iiot-gateway:sha-");
        envExampleSource.Should().Contain("IIOT_REDIS_IMAGE=harbor.example.com/mirror/redis:7.4-alpine");
    }

    [Fact]
    public void NginxTemplate_ShouldRouteApiTrafficThroughGatewayOverHttp()
    {
        var source = File.ReadAllText(CloudRepositoryPath.Find("deploy", "nginx", "nginx.conf"));
        var composeSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "docker-compose.prod.yml"));
        var programSource = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "Program.cs"));

        source.Should().Contain("listen 8080;");
        source.Should().NotContain("listen 80;");
        source.Should().Contain("Content-Security-Policy");
        source.Should().Contain("limit_req_zone");
        source.Should().Contain("upstream gateway_pool");
        source.Should().Contain("server iiot-gateway:8080;");
        source.Should().Contain("proxy_pass http://gateway_pool;");
        source.Should().Contain("proxy_pass http://iiot-web:8080;");
        source.Should().Contain("location /api/v1/bootstrap/");
        source.Should().NotContain("include /etc/nginx/proxy_params;");
        source.Should().NotContain("listen 443 ssl http2;");
        source.Should().NotContain("Strict-Transport-Security");
        source.Should().NotContain("ssl_certificate");
        composeSource.Should().Contain("${GATEWAY_HTTP_PORT:-80}:8080");
        composeSource.Should().Contain("user: ${NGINX_UID:-101}:${NGINX_GID:-101}");
        composeSource.Should().NotContain("GATEWAY_HTTPS_PORT");
        composeSource.Should().NotContain(":443");
        programSource.Should().NotContain("UseHttpsRedirection");
        source.Should().Contain("proxy_set_header X-Real-IP $remote_addr;");
        source.Should().Contain("proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        source.Should().Contain("proxy_set_header X-Forwarded-Proto $scheme;");
        source.Should().Contain("proxy_set_header X-Forwarded-Host $http_host;");
        source.Should().Contain("proxy_set_header X-Request-Id $request_id;");
    }

    [Fact]
    public void NginxTemplate_ShouldCompressTextAndJsonWithoutHttpApiDoubleCompression()
    {
        var source = File.ReadAllText(CloudRepositoryPath.Find("deploy", "nginx", "nginx.conf"));
        var programSource = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "Program.cs"));
        var dependencyInjectionSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "DependencyInjection.cs"));
        var gzipTypes = Regex.Match(
            source,
            @"gzip_types(?<types>.*?);",
            RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Groups["types"].Value;

        source.Should().Contain("gzip on;");
        source.Should().Contain("gzip_min_length 1024;");
        source.Should().Contain("gzip_vary on;");
        gzipTypes.Should().Contain("application/json");
        gzipTypes.Should().Contain("application/javascript");
        gzipTypes.Should().Contain("text/css");
        gzipTypes.Should().Contain("image/svg+xml");
        gzipTypes.Should().NotContain("application/zip");
        gzipTypes.Should().NotContain("application/octet-stream");
        gzipTypes.Should().NotContain("application/x-msdownload");
        gzipTypes.Should().NotContain("application/x-nupkg");
        programSource.Should().NotContain("UseResponseCompression");
        dependencyInjectionSource.Should().NotContain("AddResponseCompression");
    }

    [Fact]
    public void DeployNginxTemplate_ShouldUseGatewayPoolStructuredLogsAndRequestIdForwarding()
    {
        var source = File.ReadAllText(CloudRepositoryPath.Find("deploy", "nginx", "nginx.conf"));

        source.Should().Contain("upstream gateway_pool");
        source.Should().Contain("keepalive 32;");
        source.Should().Contain("access_log /dev/stdout iiot_gateway;");
        source.Should().Contain("error_log /dev/stderr warn;");
        source.Should().Contain("request_id=$request_id");
        source.Should().Contain("upstream_addr=\"$upstream_addr\"");
        source.Should().Contain("upstream_status=\"$upstream_status\"");
        source.Should().Contain("upstream_response_time=\"$upstream_response_time\"");
        source.Should().Contain("request_time=$request_time");
        source.Should().Contain("route_path=\"$uri\"");
        source.Should().Contain("proxy_http_version 1.1;");
        source.Should().Contain("proxy_set_header Connection \"\";");
        source.Should().Contain("proxy_set_header X-Request-Id $request_id;");
        source.Should().Contain("proxy_pass http://gateway_pool;");
        source.Should().Contain("location = /internal/healthz");
        source.Should().Contain("allow 127.0.0.1;");
        source.Should().Contain("allow ::1;");
        source.Should().Contain("deny all;");
        source.Should().NotContain("proxy_set_header Connection \"upgrade\";");
        source.Should().NotContain("proxy_set_header Upgrade $http_upgrade;");
    }

    [Fact]
    public void CloudCiWorkflow_ShouldRunDynamicRequiredInventoryAndKeepManualRunnersSeparated()
    {
        var workflowSource = File.ReadAllText(CloudRepositoryPath.Find(".github", "workflows", "cloud-ci.yml"));
        var repositoryRoot = Path.GetDirectoryName(CloudRepositoryPath.Find("IIoT.CloudPlatform.slnx"))
            ?? throw new DirectoryNotFoundException("Could not resolve Cloud repository root.");
        var directoryBuildPropsSource = File.ReadAllText(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var globalAnalyzerConfigSource = File.ReadAllText(Path.Combine(repositoryRoot, ".globalconfig"));
        var solutionSource = File.ReadAllText(CloudRepositoryPath.Find("IIoT.CloudPlatform.slnx"));
        var analyzerProjectSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "analyzers", "IIoT.CloudPlatform.Analyzers", "IIoT.CloudPlatform.Analyzers.csproj"));
        var analyzerTestsProjectSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "tests", "IIoT.CloudPlatform.AnalyzerTests", "IIoT.CloudPlatform.AnalyzerTests.csproj"));
        var fixtureScriptSource = File.ReadAllText(
            CloudRepositoryPath.Find("scripts", "tests", "TestCloudArchitectureAnalyzerFixtures.sh"));
        var realWebE2eRunnerSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "ui", "iiot-web", "e2e", "run-real-e2e.mjs"));
        var realWebE2eHostSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "testing", "IIoT.CloudPlatform.WebE2ETestKit", "Program.cs"));
        var inventoryScriptSource = File.ReadAllText(
            CloudRepositoryPath.Find("scripts", "tests", "Invoke-CloudTestInventory.ps1"));
        var migrationScriptSource = File.ReadAllText(
            CloudRepositoryPath.Find("scripts", "tests", "Test-CloudTestMigration.ps1"));
        var migrationBaselineSource = File.ReadAllText(
            CloudRepositoryPath.Find("scripts", "tests", "baselines", "cloud-test-migration.json"));
        var qualityBaselineBehaviorSource = File.ReadAllText(
            CloudRepositoryPath.Find("scripts", "tests", "Test-CloudQualityBaselineProtectionBehavior.ps1"));
        var coverageScriptSource = File.ReadAllText(
            CloudRepositoryPath.Find("scripts", "tests", "Test-CloudCoverage.ps1"));
        var inventoryManifestSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "tests", "cloud-test-inventory.json"));
        var analyzerFixtureStep = GetNamedGitHubWorkflowStep(
            workflowSource,
            "Run architecture analyzer fixture gate");
        var requiredInventoryStep = GetNamedGitHubWorkflowStep(
            workflowSource,
            "Run and reconcile required test inventory");
        var migrationLedgerStep = GetNamedGitHubWorkflowStep(
            workflowSource,
            "Enforce test declaration migration ledger");
        var fullEndToEndStep = GetNamedGitHubWorkflowStep(
            workflowSource,
            "Run end-to-end tests");

        workflowSource.Should().Contain("workflow_dispatch:");
        workflowSource.Should().Contain("include_end_to_end:");
        workflowSource.Should().NotContain("--filter");
        workflowSource.Should().MatchRegex(@"(?ms)^  build-test:\n.*?^    timeout-minutes: 25$");
        workflowSource.Should().MatchRegex(@"(?ms)^  full-end-to-end:\n.*?^    timeout-minutes: 90$");
        directoryBuildPropsSource.Should().Contain(
            "src/analyzers/IIoT.CloudPlatform.Analyzers/IIoT.CloudPlatform.Analyzers.csproj");
        directoryBuildPropsSource.Should().Contain("OutputItemType=\"Analyzer\"");
        directoryBuildPropsSource.Should().Contain("ReferenceOutputAssembly=\"false\"");
        directoryBuildPropsSource.Should().Contain("IsAspireProjectResource=\"false\"");
        directoryBuildPropsSource.Should().Contain("EndsWith('Tests')");
        directoryBuildPropsSource.Should().Contain("EndsWith('TestKit')");
        directoryBuildPropsSource.Should().Contain(
            "'$(MSBuildProjectName)' != 'IIoT.CloudPlatform.PortFakes'");
        directoryBuildPropsSource.Should().Contain("StartsWith('IIoT.CloudPlatform.Analyzer')");
        fixtureScriptSource.Should().Contain("IIoT.CloudPlatform.PortFakes");
        globalAnalyzerConfigSource.Should().Contain(
            "dotnet_diagnostic.CLOUDARCH003.allowed_projects = IIoT.MigrationWorkApp");
        globalAnalyzerConfigSource.Should().NotContain(
            "dotnet_diagnostic.CLOUDARCH003.allowed_projects = IIoT.DataWorker");
        globalAnalyzerConfigSource.Should().Contain("IIoT.DataWorker::Program");
        globalAnalyzerConfigSource.Should().Contain(
            "IIoT.HttpApi::IIoT.HttpApi.DesignTimeDbContextFactory");
        globalAnalyzerConfigSource.Should().Contain(
            "IIoT.HttpApi::IIoT.HttpApi.Infrastructure.PostgresReadinessHealthCheck");
        analyzerProjectSource.Should().Contain("<TargetFramework>netstandard2.0</TargetFramework>");
        analyzerProjectSource.Should().Contain(
            "<PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"5.6.0\" PrivateAssets=\"all\" />");
        analyzerTestsProjectSource.Should().Contain(
            "<PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"5.6.0\" PrivateAssets=\"all\" />");
        solutionSource.Should().Contain(
            "src/analyzers/IIoT.CloudPlatform.Analyzers/IIoT.CloudPlatform.Analyzers.csproj");
        solutionSource.Should().Contain(
            "src/tests/IIoT.CloudPlatform.AnalyzerTests/IIoT.CloudPlatform.AnalyzerTests.csproj");
        solutionSource.Should().Contain(
            "src/tests/IIoT.CloudPlatform.WorkspaceAlignmentTests/IIoT.CloudPlatform.WorkspaceAlignmentTests.csproj");
        analyzerFixtureStep.Should().Contain("shell: bash");
        analyzerFixtureStep.Should().Contain("bash scripts/tests/TestCloudArchitectureAnalyzerFixtures.sh");
        analyzerFixtureStep.Should().NotContain("if:");
        analyzerFixtureStep.Should().NotContain("continue-on-error:");
        fixtureScriptSource.Should().Contain("ARCHITECTURE_FIXTURES_OK valid=8 invalid=15 callGraphBypass=20");
        fixtureScriptSource.Should().Contain("build_valid");
        fixtureScriptSource.Should().Contain("build_invalid");
        realWebE2eRunnerSource.Should().Contain("mkdtemp(join(tmpdir(), 'iiot-cloud-web-e2e-'))");
        realWebE2eRunnerSource.Should().Contain("chmod(stateDirectory, 0o700)");
        realWebE2eRunnerSource.Should().NotContain("artifacts/test-results/cloud-web-e2e-state.json");
        realWebE2eHostSource.Should().Contain("Path.GetRelativePath(temporaryRoot, statePath)");
        realWebE2eHostSource.Should().Contain("File.SetUnixFileMode");
        workflowSource.Should().Contain("!artifacts/test-results/**/*state*.json");
        workflowSource.Should().Contain("!artifacts/test-results/**/*credential*.json");
        requiredInventoryStep.Should().Contain(
            "./scripts/tests/Invoke-CloudTestInventory.ps1 -Mode Required -Configuration Release -NoBuild");
        requiredInventoryStep.Should().Contain("timeout-minutes: 15");
        requiredInventoryStep.Should().NotContain("if:");
        requiredInventoryStep.Should().NotContain("continue-on-error:");
        migrationLedgerStep.Should().Contain("./scripts/tests/Test-CloudTestMigration.ps1");
        migrationLedgerStep.Should().NotContain("if:");
        migrationLedgerStep.Should().NotContain("continue-on-error:");
        workflowSource.Should().Contain("Verify quality baseline behavior");
        workflowSource.Should().Contain("./scripts/tests/Test-CloudQualityBaselineProtectionBehavior.ps1");
        qualityBaselineBehaviorSource.Should().Contain("CLOUD_QUALITY_BASELINE_PROTECTION_OK scenarios=6");
        qualityBaselineBehaviorSource.Should().Contain("initialExactCandidate=1");
        migrationScriptSource.Should().Contain("CLOUD_TEST_MIGRATION_OK");
        migrationScriptSource.Should().Contain("Unmapped base test declaration");
        migrationScriptSource.Should().Contain("currentDeltaCount");
        migrationScriptSource.Should().Contain("negativeFixtures");
        migrationBaselineSource.Should().Contain("\"expectedBaseDeclarationCount\": 529");
        migrationBaselineSource.Should().Contain("\"baseCommit\": \"88c41109fbcf0b87b18939a139e0bff751e03d07\"");
        migrationBaselineSource.Should().Contain("\"baseSourcePath\": \"src/tests\"");
        migrationBaselineSource.Should().Contain("\"baseTestsTreeObject\": \"604fc2c1f2b9e4746657f314191a4514ccf75530\"");
        migrationBaselineSource.Should().Contain("\"baseDeclarationDigestSha256\": \"d75d5529e418be31819676abeb210a79550149e14896fbe1857c30a83149c77c\"");
        inventoryScriptSource.Should().Contain("CLOUD_TEST_RUNNER_OK");
        inventoryScriptSource.Should().Contain(
            "discovered=$discovered total=$total executed=$executed passed=$passed failed=0 skipped=0");
        inventoryScriptSource.Should().Contain("Sync-over-async policy fixture was not rejected");
        inventoryScriptSource.Should().Contain("Task.CompletedTask.GetAwaiter" + "().GetResult()");
        inventoryScriptSource.Should().Contain("Task.WaitAll" + "(work)");
        inventoryScriptSource.Should().Contain("response.Result");
        inventoryManifestSource.Should().Contain("\"migrationBaselineCases\": 591");
        inventoryManifestSource.Should().Contain("\"baselineCases\": 725");
        inventoryManifestSource.Should().Contain("\"requiredCases\": 661");
        inventoryManifestSource.Should().Contain("\"regressionBaselines\"");
        inventoryManifestSource.Should().NotContain("\"regressionId\": null");
        inventoryScriptSource.Should().Contain("RegressionId coverage fell below its non-zero floor");
        inventoryScriptSource.Should().Contain("regressionZero=$regressionZeroCount");
        inventoryManifestSource.Should().Contain("\"runtime\": \"Aspire\"");
        inventoryManifestSource.Should().Contain("\"runtimeDependencies\": [\"Aspire\", \"AICopilotWorkspace\"]");
        inventoryManifestSource.Should().Contain("\"cadence\": \"Release\"");
        inventoryManifestSource.Should().Contain("\"profile\": \"WorkspaceAlignment\"");
        workflowSource.Should().Contain(
            "ARCHITECTURE_FIXTURES_OK valid=8 invalid=15 callGraphBypass=20 suppressionBypass=3 diagnostics=CLOUDARCH001,CLOUDARCH002,CLOUDARCH003,CLOUDARCH004,CLOUDARCH005,CLOUDARCH006,CLOUDARCH007,CLOUDARCH008,CLOUDARCH009,CLOUDARCH010");
        workflowSource.Should().Contain("Validate deploy script syntax");
        workflowSource.Should().Contain("sh -n deploy/scripts/release-common.sh");
        workflowSource.Should().Contain("sh -n deploy/scripts/pre-deploy-check.sh");
        workflowSource.Should().Contain("sh -n deploy/scripts/post-deploy-check.sh");
        workflowSource.Should().Contain("sh -n deploy/scripts/ops-check.sh");
        workflowSource.Should().Contain("sh -n deploy/scripts/deploy-release.sh");
        workflowSource.Should().Contain("sh -n deploy/scripts/verify-edge-installer-catalog.sh");
        workflowSource.Should().Contain("bash -n deploy/scripts/build-and-push.sh");
        workflowSource.Should().Contain("bash -n deploy/scripts/local-release.sh");
        workflowSource.Should().Contain("bash -n deploy/scripts/post-release-cleanup.sh");
        workflowSource.Should().Contain("pwsh -NoProfile -Command");
        workflowSource.Should().Contain("Parser]::ParseFile(\"deploy/scripts/InvokeEdgeInstallerPackageDownload.ps1\"");
        workflowSource.Should().Contain(
            "if: github.event_name == 'schedule' || (github.event_name == 'workflow_dispatch' && inputs.include_end_to_end == true)");
        workflowSource.Should().Contain(
            "if: github.event_name == 'schedule' || (github.event_name == 'workflow_dispatch' && inputs.include_mutation == true)");
        workflowSource.Should().Contain("CLOUD_QUALITY_BASE_REF: HEAD^");
        workflowSource.Should().NotContain("quality_base_ref");
        workflowSource.Should().NotContain("CLOUD_QUALITY_SCHEDULE_BASE_REF");
        coverageScriptSource.Should().Contain("Assert-CoverageRunnerBinaryEvidence");
        coverageScriptSource.Should().Contain("Coverage runner used stale production assembly/PDB");
        coverageScriptSource.Should().Contain("Assert-PortablePdbDocumentChecksum");
        coverageScriptSource.Should().Contain("Current production source does not match its built portable PDB document checksum");
        coverageScriptSource.Should().Contain("Assert-CoverageReportIdentitySet");
        inventoryScriptSource.Should().Contain("Coverage runner contains stale production assembly/PDB before execution");
        inventoryScriptSource.Should().Contain("Cobertura production packages do not have exact collection-time DLL/PDB evidence");
        fullEndToEndStep.Should().Contain("timeout-minutes: 60");
        fullEndToEndStep.Should().Contain(
            "./scripts/tests/Invoke-CloudTestInventory.ps1 -Mode EndToEnd -Configuration Release -NoBuild");
    }

    [Fact]
    public void CloudDeployWorkflow_ShouldUseReleaseTagInputAndSharedDeployScript()
    {
        var workflowSource = File.ReadAllText(CloudRepositoryPath.Find(".github", "workflows", "cloud-deploy.yml"));

        workflowSource.Should().Contain("release_tag:");
        workflowSource.Should().Contain("Emergency release tag from local Harbor build or disaster-recovery cloud-image (sha-*)");
        workflowSource.Should().Contain("Emergency only. Use the workspace deploy entrypoint for routine releases; empty means full emergency release only.");
        workflowSource.Should().Contain("emergency_confirm:");
        workflowSource.Should().Contain("EMERGENCY_CLOUD_DEPLOY");
        workflowSource.Should().Contain("cloud-deploy is emergency-only. Use deploy/Deploy-Changed.ps1 -Targets Cloud from the workspace root for routine application releases.");
        workflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        workflowSource.Should().Contain("if [[ ! \"$release_tag\" =~ ^sha-[0-9a-f]+$ ]]");
        workflowSource.Should().Contain("RELEASE_TAG: ${{ inputs.release_tag }}");
        workflowSource.Should().Contain("DEPLOY_GIT_SHA: ${{ github.sha }}");
        workflowSource.Should().Contain("DEPLOY_TRIGGERED_BY: ${{ github.actor }}");
        workflowSource.Should().Contain("DEPLOY_TARGET_DIR: ${{ secrets.DEPLOY_TARGET_DIR }}");
        workflowSource.Should().Contain("DEPLOY_ENV_FILE: ${{ secrets.DEPLOY_ENV_FILE }}");
        workflowSource.Should().Contain("SEED_ADMIN_PASSWORD: ${{ secrets.SEED_ADMIN_PASSWORD }}");
        workflowSource.Should().Contain("Self-hosted runner must not run as root.");
        workflowSource.Should().Contain("OCI_REGISTRY still uses the documentation example domain");
        workflowSource.Should().Contain("OCI_REGISTRY must include an explicit Harbor registry host");
        workflowSource.Should().Contain("rsync -a --delete");
        workflowSource.Should().Contain("printf '%s\\n' \"$DEPLOY_ENV_FILE\" > \"$DEPLOY_TARGET_DIR/.env\"");
        workflowSource.Should().Contain("chmod 600 \"$DEPLOY_TARGET_DIR/.env\"");
        workflowSource.Should().Contain("replace_env_value \"$DEPLOY_TARGET_DIR/.env\" SEED_ADMIN_PASSWORD \"$SEED_ADMIN_PASSWORD\"");
        workflowSource.Should().Contain("docker login \"${{ secrets.OCI_REGISTRY }}\"");
        workflowSource.Should().Contain("services:");
        workflowSource.Should().Contain("DEPLOY_SERVICES: ${{ inputs.services }}");
        workflowSource.Should().Contain("deploy_args=(\"$RELEASE_TAG\")");
        workflowSource.Should().Contain("deploy_args+=(--services \"$DEPLOY_SERVICES\")");
        workflowSource.Should().Contain("./scripts/deploy-release.sh \"${deploy_args[@]}\"");
        workflowSource.Should().NotContain("runs-on: ubuntu-latest");
        workflowSource.Should().NotContain("appleboy/ssh-action");
        workflowSource.Should().NotContain("appleboy/scp-action");
        workflowSource.Should().NotContain("ghcr.io");
        workflowSource.Should().NotContain("envs: GITHUB_ACTOR,GITHUB_TOKEN,RELEASE_TAG,DEPLOY_GIT_SHA,DEPLOY_TRIGGERED_BY");
        workflowSource.Should().NotContain("compose run --rm iiot-migration");
        workflowSource.Should().NotContain("probe_status \"${public_base_url}/internal/healthz\" \"200\"");
    }

    [Fact]
    public void CloudAdminRepairWorkflow_ShouldProtectDeployEnvFileAndRequireExplicitRepair()
    {
        var workflowSource = File.ReadAllText(CloudRepositoryPath.Find(".github", "workflows", "cloud-admin-repair.yml"));

        workflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        workflowSource.Should().Contain("environment: production");
        workflowSource.Should().Contain("reset_seed_admin_password:");
        workflowSource.Should().Contain("reset_seed_admin_password must be true for this explicit repair workflow.");
        workflowSource.Should().Contain("Self-hosted runner must not run as root.");
        workflowSource.Should().Contain("OCI_REGISTRY still uses the documentation example domain");
        workflowSource.Should().Contain("OCI_REGISTRY must include an explicit Harbor registry host");
        workflowSource.Should().Contain("DEPLOY_ENV_FILE: ${{ secrets.DEPLOY_ENV_FILE }}");
        workflowSource.Should().Contain("umask 077");
        workflowSource.Should().Contain("printf '%s\\n' \"$DEPLOY_ENV_FILE\" > \"$DEPLOY_TARGET_DIR/.env\"");
        workflowSource.Should().Contain("chmod 600 \"$DEPLOY_TARGET_DIR/.env\"");
        workflowSource.Should().Contain("replace_env_value \"$DEPLOY_TARGET_DIR/.env\" SEED_ADMIN_PASSWORD \"$SEED_ADMIN_PASSWORD\"");
        workflowSource.Should().Contain("temp_env=\"$(mktemp \"$DEPLOY_TARGET_DIR/.admin-repair-env.XXXXXX\")\"");
        workflowSource.Should().Contain("chmod 600 \"$temp_env\"");
        workflowSource.Should().Contain("SEED_ADMIN_RESET_PASSWORD=true");
        workflowSource.Should().NotContain("runs-on: ubuntu-latest");
        workflowSource.Should().NotContain("appleboy/ssh-action");
        workflowSource.Should().NotContain("appleboy/scp-action");
    }

    [Fact]
    public void CloudImageWorkflow_ShouldUseIntranetRunnerAndHarborMirrorBaseImages()
    {
        var workflowSource = File.ReadAllText(CloudRepositoryPath.Find(".github", "workflows", "cloud-image.yml"));
        var harborRetentionScript = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "harbor-retention.sh"));
        var webDockerfileSource = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "iiot-web.Dockerfile"));
        var backendDockerfileSources = new[]
        {
            File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "Dockerfile")),
            File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.Gateway", "Dockerfile")),
            File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.DataWorker", "Dockerfile")),
            File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.MigrationWorkApp", "Dockerfile")),
        };

        workflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        workflowSource.Should().Contain("environment: production");
        workflowSource.Should().Contain("Self-hosted runner must not run as root.");
        workflowSource.Should().Contain("workflow_dispatch:");
        workflowSource.Should().Contain("emergency_confirm:");
        workflowSource.Should().Contain("EMERGENCY_CLOUD_IMAGE_BUILD");
        workflowSource.Should().Contain("cloud-image is no longer a routine production release path. Use local build-and-push + SSH deploy.");
        workflowSource.Should().Contain("OCI_REGISTRY still uses the documentation example domain");
        workflowSource.Should().Contain("OCI_REGISTRY must include an explicit Harbor registry host");
        workflowSource.Should().Contain("OCI_NAMESPACE must be a single Harbor project/namespace segment");
        Regex.Matches(workflowSource, @"\*\.example\*\|\*internal\.example\*")
            .Count.Should().BeGreaterThanOrEqualTo(2, "cloud-image must reject documentation example domains for both OCI_REGISTRY and VITE_AICOPILOT_CHALLENGE_URL");
        workflowSource.Should().Contain("docker buildx build");
        workflowSource.Should().Contain("Prune old Harbor image tags");
        workflowSource.Should().Contain("bash deploy/scripts/harbor-retention.sh \"${{ matrix.image }}\"");
        workflowSource.Should().Contain("--build-arg \"DOTNET_SDK_IMAGE=${{ steps.registry.outputs.registry }}/mirror/dotnet-sdk:10.0.301\"");
        workflowSource.Should().Contain("--build-arg \"DOTNET_ASPNET_IMAGE=${{ steps.registry.outputs.registry }}/mirror/dotnet-aspnet:10.0.9\"");
        workflowSource.Should().Contain("VITE_AICOPILOT_CHALLENGE_URL is required");
        workflowSource.Should().Contain("VITE_AICOPILOT_CHALLENGE_URL still uses the documentation example domain");
        workflowSource.Should().NotContain("http://10.98.90.154:82/api/identity/cloud-oidc/challenge");
        workflowSource.Should().Contain("--build-arg \"NODE_BASE_IMAGE=${{ steps.registry.outputs.registry }}/mirror/node:22-slim\"");
        workflowSource.Should().Contain("--build-arg \"NGINX_BASE_IMAGE=${{ steps.registry.outputs.registry }}/mirror/nginx:1.27-alpine\"");
        workflowSource.Should().NotContain("\n  push:");
        workflowSource.Should().NotContain("runs-on: ubuntu-latest");
        workflowSource.Should().NotContain("ghcr.io");
        workflowSource.Should().NotContain("docker/build-push-action");
        workflowSource.Should().NotContain("docker/metadata-action");
        workflowSource.Should().NotContain("docker/setup-buildx-action");

        harborRetentionScript.Should().Contain("HARBOR_KEEP_SHA_TAGS");
        harborRetentionScript.Should().Contain("HARBOR_KEEP_SHA_TAG");
        harborRetentionScript.Should().Contain("HARBOR_PROJECT/OCI_NAMESPACE must be a single Harbor project segment");
        harborRetentionScript.Should().Contain("sha-[0-9a-f]");
        harborRetentionScript.Should().Contain("Harbor GC must run");

        webDockerfileSource.Should().Contain("ARG NODE_BASE_IMAGE=node:22-slim");
        webDockerfileSource.Should().Contain("FROM ${NODE_BASE_IMAGE} AS build");
        webDockerfileSource.Should().Contain("ARG NGINX_BASE_IMAGE=nginx:1.27-alpine");
        webDockerfileSource.Should().Contain("FROM ${NGINX_BASE_IMAGE} AS final");
        webDockerfileSource.Should().Contain("EXPOSE 8080");
        webDockerfileSource.Should().Contain("USER 101:101");

        foreach (var dockerfileSource in backendDockerfileSources)
        {
            dockerfileSource.Should().Contain("ARG DOTNET_SDK_IMAGE=");
            dockerfileSource.Should().Contain("ARG DOTNET_ASPNET_IMAGE=");
            dockerfileSource.Should().Contain("harbor.internal.example/mirror/");
            dockerfileSource.Should().Contain("FROM ${DOTNET_SDK_IMAGE} AS build");
            dockerfileSource.Should().Contain("FROM ${DOTNET_ASPNET_IMAGE} AS final");
            dockerfileSource.Should().NotContain("mcr.microsoft.com");
            dockerfileSource.Should().NotContain("10.98.90.154");
        }
    }

    [Fact]
    public void WebNginxTemplate_ShouldAvoidStaleSpaChunkFallbacks()
    {
        var source = File.ReadAllText(CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "iiot-web.nginx.conf"));

        source.Should().Contain("listen 8080;");
        source.Should().Contain("location = /index.html");
        source.Should().Contain("Cache-Control \"no-cache, no-store, must-revalidate\"");
        source.Should().Contain("location /assets/");
        source.Should().Contain("Cache-Control \"public, max-age=31536000, immutable\"");
        source.Should().Contain("try_files $uri =404;");
        source.Should().Contain("try_files $uri $uri/ /index.html;");
    }

    [Fact]
    public void DeployOperationsScripts_ShouldExistAndUseExpectedCommands()
    {
        var backupSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "postgres-backup.sh"));
        var restoreSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "postgres-restore.sh"));
        var verifySource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "postgres-verify-backup.sh"));
        var opsCheckSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "ops-check.sh"));
        var releaseCommonSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "release-common.sh"));
        var preDeploySource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "pre-deploy-check.sh"));
        var mirrorImagesSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "mirror-third-party-images.sh"));
        var deployReleaseSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "deploy-release.sh"));
        var postDeploySource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "post-deploy-check.sh"));
        var rollbackSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "rollback-release.sh"));
        var configUpdateSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "update-deploy-env.sh"));

        backupSource.Should().Contain("pg_dump -h 127.0.0.1 -Fc -U postgres -d iiot-db");
        backupSource.Should().Contain("backups/postgres");
        backupSource.Should().Contain(".sha256");
        backupSource.Should().Contain("latest-successful-backup.txt");
        backupSource.Should().Contain("BACKUP_RETENTION_DAYS");
        backupSource.Should().Contain("find \"$BACKUP_DIR\"");
        backupSource.Should().Contain("sha256sum");

        restoreSource.Should().Contain("CHECKSUM_FILE=\"$DUMP_FILE.sha256\"");
        restoreSource.Should().Contain("sha256sum -c");
        restoreSource.Should().Contain("compose stop nginx-gateway iiot-web iiot-gateway iiot-httpapi iiot-dataworker");
        restoreSource.Should().Contain("pg_restore -h 127.0.0.1 --clean --if-exists --no-owner --no-privileges -U postgres -d iiot-db");
        restoreSource.Should().Contain("compose run --rm iiot-migration");
        restoreSource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");

        verifySource.Should().Contain("latest-successful-verify.txt");
        verifySource.Should().Contain("locate_latest_dump");
        verifySource.Should().Contain("CHECKSUM_FILE=\"$DUMP_FILE.sha256\"");
        verifySource.Should().Contain("sha256sum -c");
        verifySource.Should().Contain("iiot-restore-verify-");
        verifySource.Should().Contain("createdb -h 127.0.0.1 -U postgres");
        verifySource.Should().Contain("dropdb -h 127.0.0.1 --if-exists -U postgres");
        verifySource.Should().Contain("select count(*) from devices;");
        verifySource.Should().Contain("select count(*) from employees;");
        verifySource.Should().Contain("select count(*) from recipes;");
        verifySource.Should().Contain("select count(*) from outbox_messages;");
        verifySource.Should().Contain("select count(*) from \"__EFMigrationsHistory\";");

        opsCheckSource.Should().Contain("curl --silent --show-error --output /dev/null --write-out '%{http_code}'");
        opsCheckSource.Should().Contain("/internal/healthz");
        opsCheckSource.Should().Contain("select count(*) from outbox_messages where processed_at_utc is null;");
        opsCheckSource.Should().Contain("rabbitmqctl list_queues -q name messages");
        opsCheckSource.Should().Contain("latest-successful-backup.txt");
        opsCheckSource.Should().Contain("latest-successful-verify.txt");
        opsCheckSource.Should().Contain("BACKUP_MAX_AGE_HOURS");
        opsCheckSource.Should().Contain("BACKUP_VERIFY_MAX_AGE_DAYS");
        opsCheckSource.Should().Contain("REQUIRE_BACKUP=${REQUIRE_BACKUP:-1}");
        opsCheckSource.Should().Contain("if [ \"$REQUIRE_BACKUP\" = \"1\" ]; then");
        opsCheckSource.Should().Contain("latest_backup_age_hours=");
        opsCheckSource.Should().Contain("latest_backup_verified_age_days=");
        opsCheckSource.Should().Contain("latest_backup_file=");
        opsCheckSource.Should().Contain("stat -c %Y");
        opsCheckSource.Should().Contain("iiot-pass-station-batches");
        opsCheckSource.Should().Contain("iiot-device-logs");
        opsCheckSource.Should().Contain("iiot-hourly-capacities");
        opsCheckSource.Should().Contain("REQUIRE_DATAWORKER_HEALTHCHECK=${REQUIRE_DATAWORKER_HEALTHCHECK:-1}");
        opsCheckSource.Should().Contain("require_healthy_service \"iiot-dataworker\"");
        opsCheckSource.Should().Contain("dataworker_healthcheck=skipped");
        opsCheckSource.Should().Contain("REQUIRE_DATAWORKER_HEALTHCHECK must be true or false");
        opsCheckSource.Should().Contain("exit 1");
        opsCheckSource.Should().Contain("exit 2");

        releaseCommonSource.Should().Contain("CURRENT_RELEASE_FILE");
        releaseCommonSource.Should().Contain("PREVIOUS_RELEASE_FILE");
        releaseCommonSource.Should().Contain("STAGED_RELEASE_FILE");
        releaseCommonSource.Should().Contain("RELEASE_HISTORY_DIR");
        releaseCommonSource.Should().Contain("ensure_release_tag");
        releaseCommonSource.Should().Contain("Application image may not use :latest");
        releaseCommonSource.Should().Contain("INFRA_IMAGE_KEYS");
        releaseCommonSource.Should().Contain("ensure_image_values_not_template");
        releaseCommonSource.Should().Contain("*.example*");
        releaseCommonSource.Should().Contain("Image value still uses a documentation example registry");
        releaseCommonSource.Should().Contain("require_min_secret_length");
        releaseCommonSource.Should().Contain("Required secret is too short");
        releaseCommonSource.Should().Contain("require_min_secret_length JWTSETTINGS__SECRET 32");
        releaseCommonSource.Should().Contain("require_min_secret_length PG_PASSWORD 12");
        releaseCommonSource.Should().Contain("ensure_rate_limit_values_bounded");
        releaseCommonSource.Should().Contain("require_positive_integer_at_most");
        releaseCommonSource.Should().Contain("max_edge_upload_rate_per_minute=12000");
        releaseCommonSource.Should().Contain("RATE_LIMIT_PASS_STATION_UPLOAD_TOKEN_LIMIT");
        releaseCommonSource.Should().Contain("require_decimal_range");
        releaseCommonSource.Should().Contain("must be decimal digits in range");
        releaseCommonSource.Should().Contain("category=forbidden-control-key");
        releaseCommonSource.Should().Contain("category=invalid-key");
        releaseCommonSource.Should().Contain("category=malformed-entry");
        releaseCommonSource.Should().Contain("atomic_write_file() (");
        releaseCommonSource.Should().Contain("atomic_copy_file() (");
        releaseCommonSource.Should().NotContain("atomic_compare_exchange_file");
        releaseCommonSource.Should().Contain("atomic_append_file() (");
        releaseCommonSource.Should().Contain("acquire_strict_managed_lock() {");
        releaseCommonSource.Should().Contain("RELEASE_IMAGE_ENV_FILE=\"$RELEASES_DIR/current-images.env\"");
        releaseCommonSource.Should().Contain("--env-file \"$RELEASE_IMAGE_ENV_FILE\"");
        releaseCommonSource.Should().Contain("ensure_app_images_have_explicit_registry");
        releaseCommonSource.Should().Contain("Application image must be pushed to Harbor");
        releaseCommonSource.Should().Contain("Application image must include an explicit Harbor registry");
        releaseCommonSource.Should().Contain("ensure_infra_images_not_docker_hub");
        releaseCommonSource.Should().Contain("Infrastructure image must be mirrored to Harbor");
        releaseCommonSource.Should().Contain("Infrastructure image must include an explicit Harbor registry");
        releaseCommonSource.Should().Contain("ensure_deploy_operator_not_root");
        releaseCommonSource.Should().Contain("ALLOW_ROOT_DEPLOY_PREFLIGHT=emergency");
        releaseCommonSource.Should().Contain("ensure_bootstrap_secret_not_disabled");
        releaseCommonSource.Should().Contain("BOOTSTRAP_AUTH_REQUIRE_SECRET BootstrapAuth__RequireSecret");
        releaseCommonSource.Should().Contain("ensure_oidc_http_boundary");
        releaseCommonSource.Should().Contain("is_loopback_or_rfc1918_ipv4_host");
        releaseCommonSource.Should().Contain("HTTP OIDC value must use loopback or RFC1918 IPv4 host");
        releaseCommonSource.Should().Contain("ensure_deploy_disk_headroom");
        releaseCommonSource.Should().Contain("PRE_DEPLOY_DISK_WARN_PERCENT");
        releaseCommonSource.Should().Contain("PRE_DEPLOY_DISK_BLOCK_PERCENT");
        releaseCommonSource.Should().Contain("print_http_only_preflight_summary");
        releaseCommonSource.Should().Contain("runtime_controls=${1:-runtime-check-not-declared}");
        releaseCommonSource.Should().Contain("preflight_transport_baseline=http-only");
        releaseCommonSource.Should().Contain("preflight_compensation_controls=");
        releaseCommonSource.Should().Contain("image-registry-checked");
        releaseCommonSource.Should().Contain("require_healthy_service()");
        releaseCommonSource.Should().Contain(".State.Health.Status");
        releaseCommonSource.Should().Contain("Service does not define a Docker health check");
        releaseCommonSource.Should().Contain("Service did not become healthy");
        releaseCommonSource.Should().Contain("write_release_manifest");
        releaseCommonSource.Should().Contain("record_release_history");
        releaseCommonSource.Should().Contain("apply_app_images_to_dotenv");
        releaseCommonSource.Should().Contain("resolve_release_images_for_keys");
        releaseCommonSource.Should().Contain("apply_app_images_to_dotenv_for_keys");

        var localReleaseSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "local-release.sh"));
        var buildAndPushSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "build-and-push.sh"));
        var verifyInstallerCatalogSource = File.ReadAllText(
            CloudRepositoryPath.Find("deploy", "scripts", "verify-edge-installer-catalog.sh"));
        var installerDownloadSource = File.ReadAllText(
            CloudRepositoryPath.Find("deploy", "scripts", "InvokeEdgeInstallerPackageDownload.ps1"));

        localReleaseSource.Should().Contain("refuses root SSH by default");
        localReleaseSource.Should().Contain("ALLOW_ROOT_SSH_DEPLOY=emergency");
        localReleaseSource.Should().Contain("SSH target still uses the documentation example domain");
        localReleaseSource.Should().Contain("github-runner@<shared-host>");
        localReleaseSource.Should().NotContain("deploy@cloud.internal.example");
        localReleaseSource.Should().NotContain("--ssh-target root@10.98.90.154");
        buildAndPushSource.Should().Contain("REGISTRY is required");
        buildAndPushSource.Should().Contain("REGISTRY still uses the documentation example domain");
        buildAndPushSource.Should().Contain("VITE_AICOPILOT_CHALLENGE_URL is required for web image builds");
        buildAndPushSource.Should().Contain("VITE_AICOPILOT_CHALLENGE_URL still uses the documentation example domain");
        buildAndPushSource.Should().Contain("case \" $SELECTED_SERVICES \"");
        buildAndPushSource.Should().NotContain("REGISTRY=harbor.internal.example");
        buildAndPushSource.Should().NotContain(
            "VITE_AICOPILOT_CHALLENGE_URL=\"${VITE_AICOPILOT_CHALLENGE_URL:-http://aicopilot.internal.example");
        buildAndPushSource.Should().NotContain("REGISTRY=\"${REGISTRY:-10.98.90.154:80}\"");
        buildAndPushSource.Should().NotContain("http://10.98.90.154:82");
        verifyInstallerCatalogSource.Should().Contain("BASE_URL is required");
        verifyInstallerCatalogSource.Should().Contain("BASE_URL still uses the documentation example domain");
        verifyInstallerCatalogSource.Should().NotContain("BASE_URL=http://cloud.internal.example");
        verifyInstallerCatalogSource.Should().NotContain("BASE_URL=${BASE_URL:-http://10.98.90.154:81}");
        verifyInstallerCatalogSource.Should().Contain("Velopack RELEASES");
        verifyInstallerCatalogSource.Should().Contain("releases.$CHANNEL.json");
        verifyInstallerCatalogSource.Should().Contain("assets.$CHANNEL.json");
        verifyInstallerCatalogSource.Should().Contain("No Velopack .nupkg URL found");
        verifyInstallerCatalogSource.Should().Contain("check_get_download \"$velopack_nupkg_url\"");
        verifyInstallerCatalogSource.Should().Contain("EXPECTED_PLUGIN_MODULE_ID=${EXPECTED_PLUGIN_MODULE_ID:-}");
        verifyInstallerCatalogSource.Should().Contain("EXPECTED_PLUGIN_VERSION=${EXPECTED_PLUGIN_VERSION:-}");
        verifyInstallerCatalogSource.Should().Contain("from urllib.parse import urljoin");
        verifyInstallerCatalogSource.Should().Contain("base_url = os.environ[\"BASE_URL\"].rstrip(\"/\") + \"/\"");
        verifyInstallerCatalogSource.Should().Contain("print(urljoin(base_url, download_url))");
        verifyInstallerCatalogSource.Should().Contain("EXPECTED_PLUGIN_MODULE_ID and EXPECTED_PLUGIN_VERSION must be provided together");
        verifyInstallerCatalogSource.Should().Contain("expected plugin module not found in catalog");
        verifyInstallerCatalogSource.Should().Contain("expected plugin version mismatch");
        verifyInstallerCatalogSource.Should().Contain("expected plugin module not found in installer artifact");
        verifyInstallerCatalogSource.Should().Contain("expected plugin version mismatch in installer artifact");
        installerDownloadSource.Should().Contain("[Parameter(Mandatory = $true)]");
        installerDownloadSource.Should().Contain("http://<cloud-host>:81");
        installerDownloadSource.Should().NotContain("http://cloud.internal.example:81");
        installerDownloadSource.Should().NotContain("CloudApiBaseUrl = 'http://10.98.90.154:81/api/v1'");

        mirrorImagesSource.Should().Contain("mcr.microsoft.com/dotnet/sdk:10.0.301");
        mirrorImagesSource.Should().Contain("MIRROR_REGISTRY=<harbor-registry>");
        mirrorImagesSource.Should().NotContain("MIRROR_REGISTRY=harbor.internal.example");
        mirrorImagesSource.Should().Contain("dotnet-sdk:10.0.301");
        mirrorImagesSource.Should().Contain("mcr.microsoft.com/dotnet/aspnet:10.0.9");
        mirrorImagesSource.Should().Contain("dotnet-aspnet:10.0.9");

        preDeploySource.Should().Contain("ensure_release_tag \"$RELEASE_TAG\"");
        preDeploySource.Should().Contain("ensure_deploy_operator_not_root");
        preDeploySource.Should().Contain("compose config -q");
        preDeploySource.Should().Contain("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"");
        preDeploySource.Should().Contain("ensure_bootstrap_secret_not_disabled");
        preDeploySource.Should().Contain("ensure_oidc_http_boundary");
        preDeploySource.Should().Contain("ensure_rate_limit_values_bounded");
        preDeploySource.Should().Contain("require_infra_image_values");
        preDeploySource.Should().Contain("ensure_image_values_not_template");
        preDeploySource.Should().Contain("ensure_app_images_have_explicit_registry");
        preDeploySource.Should().Contain("ensure_infra_images_not_docker_hub");
        preDeploySource.Should().Contain("resolve_release_images \"$RELEASE_TAG\"");
        preDeploySource.Should().Contain("ensure_target_images_not_latest");
        preDeploySource.Should().Contain("ensure_deploy_disk_headroom");
        preDeploySource.Should().Contain("probe_status \"${public_base_url}/internal/healthz\" \"200\" 3");
        preDeploySource.Should().Contain("REQUIRE_BACKUP=0");
        preDeploySource.Should().Contain("REQUIRE_DATAWORKER_HEALTHCHECK=${PRE_DEPLOY_REQUIRE_DATAWORKER_HEALTHCHECK:-0}");
        preDeploySource.Should().Contain("BACKUP_MAX_AGE_HOURS=${PRE_DEPLOY_BACKUP_MAX_AGE_HOURS:-999999}");
        preDeploySource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");
        preDeploySource.Should().Contain("runtime_preflight_controls=runtime-check-skipped-no-current-release");
        preDeploySource.Should().Contain("runtime_preflight_controls=\"healthz-http-local ops-check-runtime\"");
        preDeploySource.Should().Contain("print_http_only_preflight_summary \"$runtime_preflight_controls\"");
        preDeploySource.IndexOf("ensure_required_secret_values_changed", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("ensure_required_public_values_changed", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("ensure_oidc_http_boundary", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("ensure_rate_limit_values_bounded", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("ensure_image_values_not_template", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("ensure_app_images_have_explicit_registry", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("resolve_release_images \"$RELEASE_TAG\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("ensure_app_images_have_explicit_registry", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("compose config -q", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal));
        preDeploySource.IndexOf("\"$SCRIPT_DIR/ensure-oidc-signing-cert.sh\"", StringComparison.Ordinal)
            .Should().BeLessThan(preDeploySource.IndexOf("sh \"$SCRIPT_DIR/check-container-nonroot-readiness.sh\"", StringComparison.Ordinal));

        mirrorImagesSource.Should().Contain("MIRROR_REGISTRY is required");
        mirrorImagesSource.Should().Contain("MIRROR_REGISTRY still uses the documentation example domain");
        mirrorImagesSource.Should().Contain("MIRROR_REGISTRY must include an explicit Harbor registry host");
        mirrorImagesSource.Should().NotContain("MIRROR_REGISTRY=${MIRROR_REGISTRY:-10.98.90.154:80}");
        mirrorImagesSource.Should().Contain("MIRROR_NAMESPACE=${MIRROR_NAMESPACE:-mirror}");
        mirrorImagesSource.Should().Contain("MIRROR_NAMESPACE must be a single Harbor project/namespace segment");
        mirrorImagesSource.Should().Contain("timescale/timescaledb:latest-pg17");
        mirrorImagesSource.Should().Contain("redis:7.4-alpine");
        mirrorImagesSource.Should().Contain("rabbitmq:3-management-alpine");
        mirrorImagesSource.Should().Contain("datalust/seq:2024.3");
        mirrorImagesSource.Should().Contain("nginx:1.27-alpine");
        mirrorImagesSource.Should().Contain("node:22-slim");
        mirrorImagesSource.Should().Contain("docker push \"$target_image\"");

        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/pre-deploy-check.sh\" \"$RELEASE_TAG\"");
        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/postgres-backup.sh\"");
        deployReleaseSource.Should().Contain("write_release_manifest");
        deployReleaseSource.Should().Contain("normalize_services");
        deployReleaseSource.Should().Contain("apply_app_images_to_dotenv \"$TEMP_RELEASE_ENV_FILE\"");
        deployReleaseSource.Should().Contain("compose pull $SELECTED_SERVICES");
        deployReleaseSource.Should().Contain("hydrate_unselected_images_from_running_containers");
        deployReleaseSource.Should().Contain("if [ \"$key\" = \"IIOT_MIGRATION_IMAGE\" ]; then");
        deployReleaseSource.Should().Contain("read_manifest_value \"$DEPLOY_DIR/.env\" \"$key\"");
        deployReleaseSource.Should().Contain("warning: keeping placeholder image for unselected one-shot migration service");
        deployReleaseSource.Should().Contain("compose up -d --no-deps $RUNTIME_SELECTED_SERVICES");
        deployReleaseSource.Should().Contain("compose up -d $RUNTIME_SELECTED_SERVICES");
        deployReleaseSource.Should().Contain("ensure_nginx_gateway_if_needed");
        deployReleaseSource.Should().Contain("Starting nginx-gateway because the selected release affects browser traffic");
        deployReleaseSource.Should().Contain("compose up -d nginx-gateway");
        deployReleaseSource.Should().Contain("compose run -T --rm iiot-migration");
        deployReleaseSource.Should().Contain("requires_edge_installer_catalog_verification");
        deployReleaseSource.Should().Contain("*\" iiot-httpapi \"*|*\" iiot-gateway \"*|*\" iiot-web \"*)");
        deployReleaseSource.Should().Contain("Edge installer catalog verification is required for selected Cloud download services");
        deployReleaseSource.Should().Contain("post_deploy_verify_edge_installer_catalog=1");
        deployReleaseSource.Should().Contain("post_deploy_require_edge_installer_catalog=1");
        deployReleaseSource.Should().Contain("POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG=\"$post_deploy_verify_edge_installer_catalog\"");
        deployReleaseSource.Should().Contain("POST_DEPLOY_REQUIRE_EDGE_INSTALLER_CATALOG=\"$post_deploy_require_edge_installer_catalog\"");
        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/post-deploy-check.sh\"");
        deployReleaseSource.Should().Contain("atomic_copy_file \"$CURRENT_RELEASE_FILE\" \"$PREVIOUS_RELEASE_FILE\"");
        deployReleaseSource.Should().Contain("atomic_copy_file \"$STAGED_RELEASE_FILE\" \"$CURRENT_RELEASE_FILE\"");
        deployReleaseSource.Should().Contain("DEPLOY_ORIGINAL_ENV_SHA256");
        deployReleaseSource.Should().Contain("acquire_strict_managed_lock");
        deployReleaseSource.Should().Contain("write_release_image_env \"$STAGED_RELEASE_IMAGE_ENV_FILE\"");
        deployReleaseSource.Should().Contain("atomic_copy_file \"$STAGED_RELEASE_IMAGE_ENV_FILE\" \"$RELEASE_IMAGE_ENV_FILE\"");
        deployReleaseSource.Should().NotContain("atomic_copy_file \"$TEMP_RELEASE_ENV_FILE\" \"$DEPLOY_DIR/.env\"");
        deployReleaseSource.Should().Contain("DEPLOY_TRANSACTION_MARKER_PATH");
        deployReleaseSource.Should().Contain("record_release_history");

        postDeploySource.Should().Contain("for service_name in nginx-gateway iiot-gateway iiot-httpapi iiot-dataworker iiot-web");
        postDeploySource.Should().Contain("require_running_service \"$service_name\"");
        postDeploySource.Should().Contain("require_healthy_service \"iiot-dataworker\"");
        postDeploySource.Should().NotContain("PRE_DEPLOY_REQUIRE_DATAWORKER_HEALTHCHECK");
        postDeploySource.Should().NotContain("REQUIRE_DATAWORKER_HEALTHCHECK=0");
        postDeploySource.Should().Contain("probe_status \"${public_base_url}/\" \"200\"");
        postDeploySource.Should().Contain("probe_status \"${public_base_url}/internal/healthz\" \"200\"");
        postDeploySource.Should().Contain("probe_status \"${public_base_url}/.well-known/openid-configuration\" \"200\"");
        postDeploySource.Should().Contain("probe_status \"${public_base_url}/.well-known/jwks\" \"200\"");
        postDeploySource.Should().Contain("POST_DEPLOY_VERIFY_OIDC_TOKEN");
        postDeploySource.Should().Contain("POST_DEPLOY_OIDC_CLIENT_ID");
        postDeploySource.Should().Contain("POST_DEPLOY_OIDC_REDIRECT_URI");
        postDeploySource.Should().Contain("POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE");
        postDeploySource.Should().Contain("POST_DEPLOY_OIDC_CODE_VERIFIER_FILE");
        postDeploySource.Should().Contain("OIDC code/verifier must be passed as files");
        postDeploySource.Should().Contain("tr -d '\\r\\n'");
        postDeploySource.Should().Contain("stat -f '%Lp'");
        postDeploySource.Should().Contain("must not grant group or other permissions");
        postDeploySource.Should().Contain("use chmod 600");
        postDeploySource.Should().Contain("--data-urlencode 'grant_type=authorization_code'");
        postDeploySource.Should().Contain("chmod 600 \"$token_response_file\" \"$authorization_code_file\" \"$code_verifier_file\"");
        postDeploySource.Should().Contain("--data-urlencode \"code@$authorization_code_file\"");
        postDeploySource.Should().Contain("--data-urlencode \"code_verifier@$code_verifier_file\"");
        postDeploySource.Should().NotContain("authorization_code=${POST_DEPLOY_OIDC_AUTHORIZATION_CODE:-}");
        postDeploySource.Should().NotContain("code_verifier=${POST_DEPLOY_OIDC_CODE_VERIFIER:-}");
        postDeploySource.Should().NotContain("--data-urlencode \"code=$authorization_code\"");
        postDeploySource.Should().NotContain("--data-urlencode \"code_verifier=$code_verifier\"");
        postDeploySource.Should().Contain("post_deploy_oidc_token=verified");
        postDeploySource.Should().Contain("post_deploy_enabled");
        postDeploySource.Should().Contain("POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG");
        postDeploySource.Should().Contain("POST_DEPLOY_REQUIRE_EDGE_INSTALLER_CATALOG");
        postDeploySource.Should().Contain("BASE_URL=\"$public_base_url\"");
        postDeploySource.Should().Contain("POST_DEPLOY_EDGE_CHANNEL");
        postDeploySource.Should().Contain("POST_DEPLOY_EDGE_TARGET_RUNTIME");
        postDeploySource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_VERSION");
        postDeploySource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_MODULE_ID");
        postDeploySource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_VERSION");
        postDeploySource.Should().Contain("\"$SCRIPT_DIR/verify-edge-installer-catalog.sh\"");
        postDeploySource.Should().Contain("post_deploy_edge_installer_catalog=verified");
        postDeploySource.Should().Contain("post_deploy_edge_installer_catalog=skipped required=1");
        postDeploySource.Should().Contain("Edge catalog verification is required for this deployment and cannot be skipped");
        postDeploySource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");

        rollbackSource.Should().Contain("resolve_release_file_path");
        rollbackSource.Should().Contain("load_release_images_from_manifest");
        rollbackSource.Should().Contain("compose pull iiot-httpapi iiot-gateway iiot-dataworker iiot-web");
        rollbackSource.Should().Contain("\"$SCRIPT_DIR/post-deploy-check.sh\"");
        rollbackSource.Should().Contain("acquire_managed_lock");
        rollbackSource.Should().Contain("acquire_strict_managed_lock");
        rollbackSource.Should().Contain("release_managed_lock");
        rollbackSource.Should().Contain("atomic_copy_file \"$CURRENT_RELEASE_FILE\" \"$PREVIOUS_RELEASE_FILE\"");
        rollbackSource.Should().Contain("atomic_copy_file \"$STAGED_RELEASE_IMAGE_ENV_FILE\" \"$RELEASE_IMAGE_ENV_FILE\"");
        rollbackSource.Should().Contain("atomic_copy_file \"$STAGED_RELEASE_FILE\" \"$CURRENT_RELEASE_FILE\"");
        rollbackSource.Should().Contain("DEPLOY_ORIGINAL_ENV_SHA256");
        rollbackSource.Should().Contain("operator changes were preserved");
        rollbackSource.Should().NotContain("atomic_copy_file \"$TEMP_RELEASE_ENV_FILE\" \"$DEPLOY_DIR/.env\"");
        rollbackSource.Should().Contain("write_release_manifest");
        rollbackSource.Should().Contain("record_release_history");
        rollbackSource.Should().NotContain("iiot-migration");
        rollbackSource.Should().NotContain("postgres-restore.sh");
        configUpdateSource.Should().Contain("acquire_strict_managed_lock");
        configUpdateSource.Should().Contain("EXPECTED_SHA256");
        configUpdateSource.Should().Contain("atomic_copy_file \"$CANDIDATE_ENV\" \"$DEPLOY_DIR/.env\"");
    }

    [Fact]
    public void DeployOperationsEntryScripts_ShouldBeExecutableOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executableScripts = new[]
        {
            "build-and-push.sh",
            "check-container-nonroot-readiness.sh",
            "deploy-release.sh",
            "ensure-oidc-signing-cert.sh",
            "harbor-retention.sh",
            "local-release.sh",
            "mirror-third-party-images.sh",
            "ops-check.sh",
            "post-deploy-check.sh",
            "post-release-cleanup.sh",
            "postgres-backup.sh",
            "postgres-restore.sh",
            "postgres-verify-backup.sh",
            "pre-deploy-check.sh",
            "rollback-release.sh",
            "update-deploy-env.sh",
            "verify-edge-installer-catalog.sh"
        };

        foreach (var script in executableScripts)
        {
            AssertUnixUserExecutable(script);
        }

        var releaseCommonMode = File.GetUnixFileMode(CloudRepositoryPath.Find("deploy", "scripts", "release-common.sh"));
        (releaseCommonMode & UnixFileMode.UserExecute)
            .Should().Be(0, "release-common.sh is a sourced library, not an operator entrypoint");
    }

    [Fact]
    public void OperationsManual_ShouldDocumentHealthBackupRestoreAndExitCodes()
    {
        var operationsSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "OPERATIONS.md"));

        operationsSource.Should().Contain("GET /internal/healthz");
        operationsSource.Should().Contain("127.0.0.1");
        operationsSource.Should().Contain("gateway_http_port=$(sed -n 's/^GATEWAY_HTTP_PORT=//p' .env | tail -n 1)");
        operationsSource.Should().Contain("http://127.0.0.1:${gateway_http_port}/internal/healthz");
        operationsSource.Should().Contain("http://127.0.0.1:${gateway_http_port}/edge-updates/velopack/stable/RELEASES");
        operationsSource.Should().NotContain("http://127.0.0.1:80/");
        operationsSource.Should().Contain("./scripts/postgres-backup.sh");
        operationsSource.Should().Contain("./scripts/postgres-restore.sh");
        operationsSource.Should().Contain("./scripts/postgres-verify-backup.sh");
        operationsSource.Should().Contain("./scripts/ops-check.sh");
        operationsSource.Should().Contain("./scripts/deploy-release.sh");
        operationsSource.Should().Contain("./scripts/rollback-release.sh");
        operationsSource.Should().Contain("iiot-linux-prod");
        operationsSource.Should().Contain("github-runner");
        operationsSource.Should().Contain("Docker Hub is not a production dependency source");
        operationsSource.Should().Contain("POST_DEPLOY_VERIFY_EDGE_INSTALLER_CATALOG=1 ./scripts/post-deploy-check.sh");
        operationsSource.Should().Contain("approved Cloud release APIs");
        operationsSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_VERSION");
        operationsSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_MODULE_ID");
        operationsSource.Should().Contain("POST_DEPLOY_EDGE_EXPECTED_PLUGIN_VERSION");
        operationsSource.Should().Contain("The default post-deploy smoke verifies `/`, `/internal/healthz`, OIDC discovery, JWKS, the DataWorker Docker healthcheck, and `ops-check.sh`");
        operationsSource.Should().Contain("POST_DEPLOY_VERIFY_OIDC_TOKEN=1");
        operationsSource.Should().Contain("POST_DEPLOY_OIDC_AUTHORIZATION_CODE_FILE");
        operationsSource.Should().Contain("POST_DEPLOY_OIDC_CODE_VERIFIER_FILE");
        operationsSource.Should().Contain("read -rsp 'OIDC authorization code: '");
        operationsSource.Should().Contain("read -rsp 'OIDC PKCE verifier: '");
        operationsSource.Should().Contain("real one-time authorization code and matching PKCE verifier");
        operationsSource.Should().Contain("process environment");
        operationsSource.Should().Contain("then prints skip lines for the OIDC token gate and Edge catalog gate");
        operationsSource.Should().Contain("current-release.env");
        operationsSource.Should().Contain("previous-release.env");
        operationsSource.Should().Contain("staged-release.env");
        operationsSource.Should().Contain("history/");
        operationsSource.Should().Contain("latest-successful-backup.txt");
        operationsSource.Should().Contain("latest-successful-verify.txt");
        operationsSource.Should().Contain(".sha256");
        operationsSource.Should().Contain("02:30");
        operationsSource.Should().Contain("03:30");
        operationsSource.Should().Contain("latest_backup_age_hours");
        operationsSource.Should().Contain("latest_backup_verified_age_days");
        operationsSource.Should().Contain("latest_backup_file");
        operationsSource.Should().Contain("`0`");
        operationsSource.Should().Contain("`1`");
        operationsSource.Should().Contain("`2`");
        operationsSource.Should().Contain("Redis is treated as cache");
        operationsSource.Should().Contain("RabbitMQ queue state is not covered");
        operationsSource.Should().Contain("It only rolls back the 5 application images.");
        operationsSource.Should().Contain("It does not run database downgrade logic.");
        operationsSource.Should().Contain("Transfer to the existing database recovery flow");
        operationsSource.Should().Contain("Do not replay while `/internal/healthz` is failing");
    }

    [Fact]
    public void DeployCronTemplates_ShouldUseFixedSchedulesForBackupAndRestoreVerification()
    {
        var backupCronSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "cron", "iiot-backup.cron.example"));
        var verifyCronSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "cron", "iiot-backup-verify.cron.example"));
        var cleanupCronSource = File.ReadAllText(CloudRepositoryPath.Find("deploy", "cron", "iiot-post-release-cleanup.cron.example"));

        backupCronSource.Should().Contain("30 2 * * *");
        backupCronSource.Should().Contain("./scripts/postgres-backup.sh");
        backupCronSource.Should().Contain("/data/iiot-platform/cloud/deploy");
        backupCronSource.Should().NotContain("/srv/iiot-cloud/deploy");

        verifyCronSource.Should().Contain("30 3 * * 0");
        verifyCronSource.Should().Contain("./scripts/postgres-verify-backup.sh");
        verifyCronSource.Should().Contain("/data/iiot-platform/cloud/deploy");
        verifyCronSource.Should().NotContain("/srv/iiot-cloud/deploy");

        cleanupCronSource.Should().Contain("30 4 * * 0");
        cleanupCronSource.Should().Contain("./scripts/post-release-cleanup.sh");
        cleanupCronSource.Should().Contain("/data/iiot-platform/cloud/deploy");
        cleanupCronSource.Should().NotContain("/srv/iiot-cloud/deploy");
    }

    private static void AssertRequestFolderConvention(
        string category,
        string audience,
        string expectedInterface)
    {
        var servicesRoot = CloudRepositoryPath.Find("src", "services");
        var invalidDeclarations = new List<string>();

        foreach (var file in Directory.GetFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(path => IsUnderRequestFolder(path, category, audience)))
        {
            var source = File.ReadAllText(file);
            var declarations = ParseRequestDeclarations(source);

            if (declarations.Count == 0)
            {
                continue;
            }

            foreach (var declaration in declarations)
            {
                if (!string.Equals(declaration.InterfaceName, expectedInterface, StringComparison.Ordinal))
                {
                    invalidDeclarations.Add(
                        $"{Path.GetFileName(file)}:{declaration.RecordName} -> {declaration.InterfaceName} (expected {expectedInterface})");
                }
            }
        }

        invalidDeclarations.Should().BeEmpty();
    }

    private static bool IsUnderRequestFolder(string filePath, string category, string audience)
    {
        var separator = Path.DirectorySeparatorChar;
        var normalized = filePath.Replace(Path.AltDirectorySeparatorChar, separator);

        return normalized.Contains($"{separator}{category}{separator}{audience}{separator}", StringComparison.Ordinal)
               && !normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
               && !normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string rootSegment, string searchPattern)
    {
        var root = CloudRepositoryPath.Find(rootSegment);

        return Directory.GetFiles(root, searchPattern, SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateGuardedTextFiles(string repositoryRoot, params string[] rootSegments)
    {
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".csproj",
            ".conf",
            ".css",
            ".env",
            ".example",
            ".html",
            ".js",
            ".json",
            ".jsonc",
            ".md",
            ".mjs",
            ".props",
            ".ps1",
            ".sh",
            ".targets",
            ".ts",
            ".tsx",
            ".vue",
            ".yaml",
            ".yml"
        };

        foreach (var rootSegment in rootSegments)
        {
            var root = Path.Combine(repositoryRoot, rootSegment);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in EnumerateFilesPruningGeneratedDirectories(root))
            {
                var extension = Path.GetExtension(file);
                var fileName = Path.GetFileName(file);
                if (textExtensions.Contains(extension)
                    || fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".env.example", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesPruningGeneratedDirectories(string root)
    {
        var ignoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vite",
            "bin",
            "coverage",
            "dist",
            "node_modules",
            "obj",
            "tests"
        };
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            {
                if (!ignoredDirectories.Contains(Path.GetFileName(childDirectory)))
                {
                    stack.Push(childDirectory);
                }
            }
        }
    }

    private static List<RequestDeclaration> ParseRequestDeclarations(string source)
    {
        var matches = Regex.Matches(
            source,
            @"public\s+(?:sealed\s+)?record\s+(?<name>\w+(?:<[^>]+>)?)\s*(?:\([^;]*?\))?\s*:\s*(?<iface>I\w+)\s*<",
            RegexOptions.Singleline);

        return matches
            .Select(match => new RequestDeclaration(
                match.Groups["name"].Value,
                match.Groups["iface"].Value))
            .ToList();
    }

    private sealed record RequestDeclaration(string RecordName, string InterfaceName);

    private static string GetNamedGitHubWorkflowStep(string workflowSource, string stepName)
    {
        var lines = workflowSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var expectedName = $"- name: {stepName}";
        var start = Array.FindIndex(
            lines,
            line => string.Equals(line.Trim(), expectedName, StringComparison.Ordinal));
        if (start < 0)
        {
            throw new InvalidOperationException($"GitHub workflow step '{stepName}' was not found.");
        }

        var indentation = lines[start].Length - lines[start].TrimStart().Length;
        var nextStepPrefix = $"{new string(' ', indentation)}- ";
        var end = Array.FindIndex(
            lines,
            start + 1,
            line => line.StartsWith(nextStepPrefix, StringComparison.Ordinal));
        if (end < 0)
        {
            end = lines.Length;
        }

        return string.Join('\n', lines[start..end]);
    }

    private static void AssertUnixUserExecutable(string scriptName)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(CloudRepositoryPath.Find("deploy", "scripts", scriptName));
        (mode & UnixFileMode.UserExecute)
            .Should().NotBe(0, $"{scriptName} is invoked directly by deployment docs or scripts");
    }

    private static bool IsAicopilotBackendCallbackRedirectUri(string redirectUri)
    {
        return Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
               && string.Equals(
                   uri.AbsolutePath,
                   "/api/identity/cloud-oidc/callback",
                   StringComparison.Ordinal);
    }

}
