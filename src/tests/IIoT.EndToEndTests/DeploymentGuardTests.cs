using System.IO;
using FluentAssertions;

namespace IIoT.EndToEndTests;

public sealed class DeploymentGuardTests
{
    [Fact]
    public void CloudLocalRelease_ShouldSyncOnlyVersionedSupportFilesAndVerifyRemoteHashes()
    {
        var source = File.ReadAllText(FindRepoFile("deploy", "scripts", "local-release.sh"));

        source.Should().Contain("SUPPORT_FILES=(");
        source.Should().Contain("docker-compose.prod.yml");
        source.Should().Contain("nginx/nginx.conf");
        source.Should().Contain("scripts/deploy-release.sh");
        source.Should().Contain(".cloud-support-manifest.sha256");
        source.Should().Contain("Cloud deploy support manifest installed");
        source.Should().Contain("check_remote_release_locks");
        source.Should().Contain("Inspect the lock owner before support sync or image build");
        source.Should().Contain("sha256sum -c");
        source.Should().Contain(".support-staging");
        source.Should().Contain("docker compose");
        source.Should().Contain("protected remote paths remain untouched: .env certs/ releases/ backups/");
        source.Should().Contain("BatchMode=yes");
        source.Should().Contain("cloud-support-sync");
        source.Should().Contain("acquire_managed_lock");
        source.Should().Contain("EXPECTED_CLOUD_SUPPORT_MANIFEST_SHA256");
        source.Should().Contain("Cloud deploy support manifest digest bound to this release");
        source.Should().NotContain("SUPPORT_FILES=(\n  .env");
        source.Should().NotContain("SUPPORT_FILES=(\n  certs");
        source.Should().NotContain("SUPPORT_FILES=(\n  releases");
        source.Should().NotContain("SUPPORT_FILES=(\n  backups");
        source.LastIndexOf("sync_remote_deploy_files", StringComparison.Ordinal)
            .Should().BeLessThan(
                source.LastIndexOf("\"$SCRIPT_DIR/build-and-push.sh\"", StringComparison.Ordinal),
                "support and lock validation must fail before an expensive image build");
    }

    [Fact]
    public void CloudReleaseLocks_ShouldFailFastAndRecoverOnlyProvenStaleLocks()
    {
        var common = File.ReadAllText(FindRepoFile("deploy", "scripts", "release-common.sh"));
        var preflight = File.ReadAllText(FindRepoFile("deploy", "scripts", "pre-deploy-check.sh"));
        var release = File.ReadAllText(FindRepoFile("deploy", "scripts", "deploy-release.sh"));
        var cleanup = File.ReadAllText(FindRepoFile("deploy", "scripts", "post-release-cleanup.sh"));

        common.Should().Contain("managed_lock_status_for_dir");
        common.Should().Contain("process-start");
        common.Should().Contain("remove_stale_managed_lock");
        common.Should().Contain("fail-fast without waiting");
        common.Should().Contain("Managed lock acquired");
        preflight.Should().Contain("preflight_release_lock=available-or-owned");
        preflight.Should().Contain("preflight_cleanup_lock=available");
        preflight.Should().Contain("preflight_support_manifest=verified");
        release.Should().Contain("acquire_managed_lock");
        release.Should().Contain("DEPLOY_RELEASE_LOCK_OWNER_PID");
        release.Should().Contain("handle_deploy_signal TERM 143");
        release.Should().NotContain("trap cleanup_deploy_process EXIT HUP INT TERM");
        cleanup.Should().Contain("ensure_managed_lock_available");
        cleanup.Should().Contain("handle_cleanup_signal TERM 143");
        cleanup.Should().NotContain("trap release_lock EXIT HUP INT TERM");
        cleanup.Should().NotContain("POST_RELEASE_CLEANUP_LOCK_ATTEMPTS:-180");
        cleanup.Should().NotContain("sleep 5");
    }

    [Fact]
    public void DeployRelease_ShouldStreamCleanupAndExplainPartialSuccess()
    {
        var source = File.ReadAllText(FindRepoFile("deploy", "scripts", "deploy-release.sh"));

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
            var mode = File.GetUnixFileMode(FindRepoFile("deploy", "scripts", script));
            (mode & UnixFileMode.UserExecute)
                .Should().NotBe(0, $"{script} is executed directly by pre-deploy-check.sh");
        }
    }

    [Fact]
    public void CloudCi_ShouldSyntaxCheckReleaseStateAccessScript()
    {
        var source = File.ReadAllText(FindRepoFile(".github", "workflows", "cloud-ci.yml"));

        source.Should().Contain("sh -n deploy/scripts/check-release-state-access.sh");
        source.Should().Contain("--filter DeploymentGuardTests");
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
            var source = File.ReadAllText(FindRepoFile(relativePath));

            source.Should().Contain("stop_watchdog");
            source.Should().Contain("wait \"$sleep_pid\"");
            source.Should().Contain("signal_process_tree TERM");
            source.Should().Contain("signal_process_tree KILL");
            source.Should().NotContain("kill -TERM \"$cmd_pid\"");
            source.Should().NotContain("kill -KILL \"$cmd_pid\"");
        }

        var localRelease = File.ReadAllText(FindRepoFile("deploy", "scripts", "local-release.sh"));
        var buildAndPush = File.ReadAllText(FindRepoFile("deploy", "scripts", "build-and-push.sh"));
        localRelease.Should().Contain("exit \"$deploy_status\"");
        buildAndPush.Should().Contain("exit \"$build_status\"");
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {string.Join('/', relativeSegments)}");
    }
}
