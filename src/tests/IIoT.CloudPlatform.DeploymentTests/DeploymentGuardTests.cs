using System.IO;
using System.Diagnostics;
using System.Text;
using FluentAssertions;

namespace IIoT.CloudPlatform.DeploymentTests;

public sealed class DeploymentGuardTests
{
    [Fact]
    public void CloudLocalRelease_ShouldSyncOnlyVersionedSupportFilesAndVerifyRemoteHashes()
    {
        var source = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "local-release.sh"));
        var transaction = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "workspace-release-transaction.sh"));

        source.Should().Contain("SUPPORT_FILES=(");
        source.Should().Contain("docker-compose.prod.yml");
        source.Should().Contain("nginx/nginx.conf");
        source.Should().Contain("scripts/deploy-release.sh");
        source.Should().Contain(".cloud-support-manifest.sha256");
        source.Should().Contain("Cloud deploy support manifest installed");
        source.Should().Contain("check_remote_release_locks");
        source.Should().Contain("readonly_lock_precheck");
        source.Should().Contain("check_remote_support_file_access\ncheck_remote_release_locks");
        source.Should().Contain("Inspect the lock owner before support sync or image build");
        source.Should().Contain("sha256sum -c");
        source.Should().Contain(".support-staging");
        source.Should().Contain("docker compose");
        source.Should().Contain("protected remote paths remain untouched: .env certs/ releases/ backups/");
        source.Should().Contain("BatchMode=yes");
        source.Should().Contain("scripts/workspace-release-transaction.sh");
        source.Should().Contain("exec bash \\\"\\$transaction_script\\\"");
        transaction.Should().Contain("acquire_managed_lock");
        transaction.Should().Contain("DEPLOY_RELEASE_LOCK_PARENT_OWNED=1");
        transaction.Should().Contain("bash \"$DEPLOY_RELEASE_SCRIPT\"");
        transaction.Should().Contain("write_transaction_marker support-install-started");
        transaction.Should().Contain("write_transaction_marker support-installed");
        transaction.Should().Contain("DEPLOY_TRANSACTION_MARKER=\"$TRANSACTION_MARKER\"");
        transaction.Should().Contain("env -i");
        transaction.Should().Contain("start_isolated_child installer");
        transaction.Should().Contain("start_isolated_child release");
        transaction.Should().Contain("terminate_child_group()");
        transaction.Should().Contain("kill -KILL -- \"-$group_pid\"");
        transaction.Should().Contain("process group termination is unconfirmed; restore and lock release are prohibited");
        transaction.Should().Contain("release_managed_lock \"$RELEASE_LOCK_FILE\"");
        source.Should().Contain("EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256");
        source.Should().Contain("Cloud deploy support manifest digest bound to this release");
        source.Should().NotContain("SUPPORT_FILES=(\n  .env");
        source.Should().NotContain("SUPPORT_FILES=(\n  certs");
        source.Should().NotContain("SUPPORT_FILES=(\n  releases");
        source.Should().NotContain("SUPPORT_FILES=(\n  backups");
        source.LastIndexOf("\"$SCRIPT_DIR/build-and-push.sh\"", StringComparison.Ordinal)
            .Should().BeLessThan(
                source.LastIndexOf("stage_support_and_deploy", StringComparison.Ordinal),
                "a failed image build must not install or mutate remote support files");
    }

    [Fact]
    public void CloudReleaseLocks_ShouldFailFastAndRecoverOnlyProvenStaleLocks()
    {
        var common = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "release-common.sh"));
        var preflight = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "pre-deploy-check.sh"));
        var release = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "deploy-release.sh"));
        var transaction = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "workspace-release-transaction.sh"));
        var cleanup = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "post-release-cleanup.sh"));
        var configUpdate = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "update-deploy-env.sh"));

        common.Should().Contain("managed_lock_status_for_dir");
        common.Should().Contain("process-start");
        common.Should().Contain("remove_stale_managed_lock");
        common.Should().Contain("fail-fast without waiting");
        common.Should().Contain("Managed lock acquired");
        common.Should().Contain("acquire_strict_managed_lock() {");
        common.Should().Contain("Strict managed lock already exists and is never auto-removed");
        common.Should().Contain("RELEASE_IMAGE_ENV_FILE=\"$RELEASES_DIR/current-images.env\"");
        common.Should().Contain("--env-file \"$RELEASE_IMAGE_ENV_FILE\"");
        preflight.Should().Contain("preflight_release_lock=available-or-owned");
        preflight.Should().Contain("preflight_cleanup_lock=available");
        preflight.Should().Contain("preflight_support_manifest=verified");
        transaction.Should().Contain("acquire_managed_lock");
        transaction.Should().Contain("DEPLOY_RELEASE_LOCK_PARENT_OWNED=1");
        transaction.Should().Contain("DEPLOY_RELEASE_LOCK_OWNER_PID=\"$$\"");
        transaction.Should().Contain("scan_durable_transaction_state");
        transaction.Should().Contain("handle_parent_signal()");
        transaction.Should().Contain("kill -\"$signal_name\" -- \"-$CHILD_PID\"");
        transaction.Should().Contain("terminate_child_group \"$CHILD_PID\" \"$CHILD_ROLE\"");
        transaction.Should().Contain("[ -n \"$CHILD_PID\" ] && process_group_alive \"$CHILD_PID\"");
        transaction.Should().NotContain("[ -n \"$CHILD_PID\" ] && kill -0 \"$CHILD_PID\"");
        transaction.Should().Contain("blocked-child-rollout-unproven");
        transaction.Should().Contain("transaction_promotion_proven");
        transaction.Should().Contain("cleanup_proven_promoted_transaction");
        transaction.Should().Contain("Promotion proof is authoritative: cleanup must never invoke restore-support");
        transaction.Should().Contain("DEPLOY_ORPHAN_MARKER_PATH");
        common.Should().Contain("category=forbidden-control-key");
        common.Should().Contain("category=invalid-key");
        common.Should().Contain("category=malformed-entry");
        common.Should().NotContain("Invalid dotenv key at line");
        common.Should().NotContain("Malformed dotenv entry at line");
        common.Should().Contain("atomic_write_file() (");
        common.Should().Contain("atomic_copy_file() (");
        common.Should().NotContain("atomic_compare_exchange_file");
        common.Should().Contain("require_decimal_range()");
        release.Should().Contain("DEPLOY_RELEASE_LOCK_OWNER_PID");
        release.Should().Contain("DEPLOY_TRANSACTION_MARKER_PATH");
        release.Should().Contain("DEPLOY_ORIGINAL_ENV_SHA256");
        release.Should().Contain("acquire_strict_managed_lock");
        release.Should().Contain("DEPLOY_CONFIG_LOCK_TEST_HOOK");
        release.Should().Contain("write_release_image_env \"$STAGED_RELEASE_IMAGE_ENV_FILE\"");
        release.Should().Contain("atomic_copy_file \"$STAGED_RELEASE_IMAGE_ENV_FILE\" \"$RELEASE_IMAGE_ENV_FILE\"");
        release.Should().NotContain("atomic_copy_file \"$TEMP_RELEASE_ENV_FILE\" \"$DEPLOY_DIR/.env\"");
        release.Should().Contain("$DEPLOY_ORIGINAL_ENV_SHA256");
        release.Should().Contain("handle_deploy_signal TERM 143");
        release.Should().NotContain("trap cleanup_deploy_process EXIT HUP INT TERM");
        cleanup.Should().Contain("ensure_managed_lock_available");
        cleanup.Should().Contain("handle_cleanup_signal TERM 143");
        cleanup.Should().NotContain("trap release_lock EXIT HUP INT TERM");
        cleanup.Should().NotContain("POST_RELEASE_CLEANUP_LOCK_ATTEMPTS:-180");
        cleanup.Should().NotContain("sleep 5");
        configUpdate.Should().Contain("acquire_strict_managed_lock");
        configUpdate.Should().Contain("EXPECTED_SHA256");
        configUpdate.Should().Contain("atomic_copy_file \"$CANDIDATE_ENV\" \"$DEPLOY_DIR/.env\"");
    }

    [Fact]
    public void DeployRelease_ShouldStreamCleanupAndExplainPartialSuccess()
    {
        var source = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "deploy-release.sh"));

        source.Should().Contain("Post-release cleanup started; output is streamed live.");
        source.Should().Contain("| tee \"$cleanup_log\"");
        source.Should().Contain("Release runtime rollout is healthy, but post-release cleanup failed");
        source.Should().Contain("Do not rerun the full deployment blindly");
    }

    [Fact]
    public void DirectlyInvokedDeployScripts_ShouldBeExecutableOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var script in new[]
                 {
                     "check-container-nonroot-readiness.sh",
                     "check-release-state-access.sh"
                 })
        {
            var mode = File.GetUnixFileMode(CloudRepositoryPath.Find("deploy", "scripts", script));
            (mode & UnixFileMode.UserExecute)
                .Should().NotBe(0, $"{script} is executed directly by pre-deploy-check.sh");
        }
    }

    [Fact]
    public void CloudCi_ShouldSyntaxCheckReleaseStateAccessScript()
    {
        var source = File.ReadAllText(CloudRepositoryPath.Find(".github", "workflows", "cloud-ci.yml"));
        var inventory = File.ReadAllText(CloudRepositoryPath.Find("src", "tests", "cloud-test-inventory.json"));

        source.Should().Contain("sh -n deploy/scripts/check-release-state-access.sh");
        source.Should().Contain("Invoke-CloudTestInventory.ps1 -Mode Required");
        inventory.Should().Contain("IIoT.CloudPlatform.DeploymentTests");
        inventory.Should().Contain("\"expected\": 20");
    }

    [Fact]
    public void CloudTimeoutWatchdogs_ShouldStopTheirSleepersAndPreserveFailureCodes()
    {
        foreach (var relativePath in new[]
                 {
                     new[] { "deploy", "scripts", "local-release.sh" },
                     new[] { "deploy", "scripts", "build-and-push.sh" }
                 })
        {
            var source = File.ReadAllText(CloudRepositoryPath.Find(relativePath));

            source.Should().Contain("stop_watchdog");
            source.Should().Contain("wait \"$sleep_pid\"");
            source.Should().Contain("signal_process_tree TERM");
            source.Should().Contain("signal_process_tree KILL");
            source.Should().Contain("grace_attempt");
            source.Should().NotContain("kill -TERM \"$cmd_pid\"");
            source.Should().NotContain("kill -KILL \"$cmd_pid\"");
            source.Should().NotContain("sleep 5");
            source.Should().NotContain("set +e\n  wait \"$cmd_pid\"");
        }

        var localRelease = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "local-release.sh"));
        var buildAndPush = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "build-and-push.sh"));
        localRelease.Should().Contain("exit \"$remote_status\"");
        buildAndPush.Should().Contain("exit \"$build_status\"");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(129)]
    [InlineData(130)]
    [InlineData(143)]
    [InlineData(255)]
    public async Task CloudTimeoutWatchdog_ShouldPreserveOrdinarySshAndSignalExitCodes(int expectedExitCode)
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await RunLocalReleaseFunctionHarnessAsync(
            $"run_with_timeout 5 'preserve {expectedExitCode}' bash -c 'exit {expectedExitCode}'");

        result.ExitCode.Should().Be(expectedExitCode, result.CombinedOutput);
    }

    [Fact]
    public async Task CloudTimeoutWatchdog_ShouldReturn124OnlyForActualTimeout()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await RunLocalReleaseFunctionHarnessAsync(
            "run_with_timeout 0.1 'actual timeout' bash -c 'sleep 5'");

        result.ExitCode.Should().Be(124, result.CombinedOutput);
        result.StandardError.Should().Contain("Timed out after 0.1 seconds");
    }

    [Theory]
    [InlineData("check_remote_support_file_access", 42)]
    [InlineData("check_remote_support_file_access", 73)]
    [InlineData("check_remote_support_file_access", 255)]
    [InlineData("check_remote_release_locks", 42)]
    [InlineData("check_remote_release_locks", 75)]
    [InlineData("check_remote_release_locks", 255)]
    public async Task CloudRemotePrechecks_ShouldPreserveFakeSshExitCode(string functionName, int expectedExitCode)
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await RunLocalReleaseFunctionHarnessAsync(
            $$"""
            ssh() { return {{expectedExitCode}}; }
            export -f ssh
            {{functionName}}
            """);

        result.ExitCode.Should().Be(expectedExitCode, result.CombinedOutput);
    }

    [Fact]
    public async Task CloudReleaseLockPrecheck_ShouldIgnoreDotEnvControlPathsAndUseTrustedDefaults()
    {
        if (OperatingSystem.IsWindows())
            return;

        var testRoot = Directory.CreateTempSubdirectory("iiot-cloud-lock-guard-");
        try
        {
            var remoteDeployDir = Path.Combine(testRoot.FullName, "deploy");
            var scriptsDir = Directory.CreateDirectory(Path.Combine(remoteDeployDir, "scripts"));
            File.Copy(
                CloudRepositoryPath.Find("deploy", "scripts", "release-common.sh"),
                Path.Combine(scriptsDir.FullName, "release-common.sh"));

            var customLockFile = Path.Combine(testRoot.FullName, "custom", "release.lock");
            var cleanupLockFile = Path.Combine(testRoot.FullName, "custom", "cleanup.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(customLockFile)!);
            var activeLockDirectory = Directory.CreateDirectory(customLockFile + ".d");
            await File.WriteAllTextAsync(
                Path.Combine(activeLockDirectory.FullName, "pid"),
                Environment.ProcessId.ToString());
            await File.WriteAllTextAsync(
                Path.Combine(remoteDeployDir, ".env"),
                $"DEPLOY_RELEASE_LOCK_FILE={customLockFile}{Environment.NewLine}" +
                $"POST_RELEASE_CLEANUP_LOCK_FILE={cleanupLockFile}{Environment.NewLine}" +
                $"PATH={testRoot.FullName}/attacker-bin{Environment.NewLine}" +
                $"BASH_ENV={testRoot.FullName}/attacker.sh{Environment.NewLine}");

            var result = await RunLocalReleaseFunctionHarnessAsync(
                """
                ssh() { sh -s; }
                export -f ssh
                check_remote_release_locks
                """,
                new Dictionary<string, string?>
                {
                    ["REMOTE_DEPLOY_DIR"] = remoteDeployDir
                });

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            result.CombinedOutput.Should().NotContain(customLockFile);
            result.CombinedOutput.Should().NotContain(cleanupLockFile);
            Directory.Exists(activeLockDirectory.FullName).Should().BeTrue(
                "a dotenv control path must not redirect lock inspection or mutate the victim lock");
        }
        finally
        {
            testRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void CloudLocalRelease_ShouldBuildFromFixedCommitAndUsePerRunArtifacts()
    {
        var localRelease = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "local-release.sh"));
        var buildAndPush = File.ReadAllText(CloudRepositoryPath.Find("deploy", "scripts", "build-and-push.sh"));

        localRelease.Should().Contain("git -C \"$REPO_ROOT\" worktree add --quiet --detach");
        localRelease.Should().Contain("CLOUD_RELEASE_SNAPSHOT_ACTIVE=1");
        localRelease.Should().Contain("CLOUD_RELEASE_SOURCE_SHA=\"$release_sha\"");
        localRelease.Should().Contain("Cloud release source frozen");
        localRelease.Should().Contain("artifacts/deploy/runs/$run_id");
        localRelease.Should().Contain("DEPLOY_ARTIFACT_DIR=\"$artifact_dir\"");
        localRelease.Should().Contain("SERVICES_FILE=\"${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy}/cloud-built-services.txt\"");
        localRelease.Should().Contain("handle_release_snapshot_signal TERM 143");
        buildAndPush.Should().Contain("${DEPLOY_ARTIFACT_DIR:-$REPO_ROOT/artifacts/deploy}");
    }

    private static async Task<ShellResult> RunLocalReleaseFunctionHarnessAsync(
        string body,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var source = await File.ReadAllTextAsync(CloudRepositoryPath.Find("deploy", "scripts", "local-release.sh"));
        var functionsStart = source.IndexOf("child_process_ids() {", StringComparison.Ordinal);
        var functionsEnd = source.IndexOf("sha256_file() {", functionsStart, StringComparison.Ordinal);
        functionsStart.Should().BeGreaterThanOrEqualTo(0);
        functionsEnd.Should().BeGreaterThan(functionsStart);
        var functions = source[functionsStart..functionsEnd];

        var testRoot = Directory.CreateTempSubdirectory("iiot-local-release-functions-");
        try
        {
            var harnessPath = Path.Combine(testRoot.FullName, "harness.sh");
            var harness = new StringBuilder()
                .AppendLine("#!/usr/bin/env bash")
                .AppendLine("set -uo pipefail")
                .AppendLine("DRY_RUN=false")
                .AppendLine("SSH_TARGET=fake@host")
                .AppendLine("REMOTE_DEPLOY_DIR=${REMOTE_DEPLOY_DIR:-/tmp/iiot-fake-cloud-deploy}")
                .AppendLine("export REMOTE_DEPLOY_DIR")
                .AppendLine("SSH_CONNECT_TIMEOUT_SECONDS=1")
                .AppendLine("SYNC_TIMEOUT_SECONDS=2")
                .AppendLine(functions)
                .AppendLine(body)
                .ToString();
            await File.WriteAllTextAsync(harnessPath, harness);

            var startInfo = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(harnessPath);
            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                    startInfo.Environment[key] = value;
            }

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Could not start bash deployment guard harness.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ShellResult(
                process.ExitCode,
                await standardOutput,
                await standardError);
        }
        finally
        {
            testRoot.Delete(recursive: true);
        }
    }

    private sealed record ShellResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => $"stdout:{Environment.NewLine}{StandardOutput}{Environment.NewLine}" +
                                        $"stderr:{Environment.NewLine}{StandardError}";
    }
}
