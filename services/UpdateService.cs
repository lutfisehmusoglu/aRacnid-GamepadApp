using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace GamepadApp.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }

    public UpdateInfo? UpdateInfo { get; init; }

    public string? NewVersion { get; init; }

    public string? ErrorMessage { get; init; }
}

public class UpdateService
{
    private static readonly Lazy<UpdateService> _instance = new(() => new());
    public static UpdateService Instance => _instance.Value;

    private const string GitHubRepoUrl =
        "https://github.com/lutfisehmusoglu/aRacnid-GamepadApp";

    private UpdateManager? _manager;

    private UpdateManager Manager =>
        _manager ??= new UpdateManager(
            new GithubSource(
                GitHubRepoUrl,
                accessToken: null,
                prerelease: false));

    public string CurrentVersionString
    {
        get
        {
            string? packageVersion = Manager.CurrentVersion?.ToString();

            if (!string.IsNullOrWhiteSpace(packageVersion))
                return packageVersion;

            return Assembly.GetExecutingAssembly()
                       .GetName()
                       .Version?
                       .ToString(3)
                   ?? "0.0.0";
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            UpdateInfo? updateInfo =
                await Manager.CheckForUpdatesAsync()
                    .ConfigureAwait(false);

            if (updateInfo == null)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpToDate
                };
            }

            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                UpdateInfo = updateInfo,
                NewVersion =
                    updateInfo.TargetFullRelease.Version.ToString()
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                ErrorMessage = ex.Message
            };
        }
    }

    public Task DownloadUpdatesAsync(
        UpdateInfo updateInfo,
        IProgress<int>? progress = null)
    {
        Action<int>? progressCallback = progress == null
            ? null
            : value => progress.Report(value);

        return Manager.DownloadUpdatesAsync(
            updateInfo,
            progressCallback);
    }

    public void ApplyUpdatesAndRestart(UpdateInfo updateInfo)
    {
        Manager.ApplyUpdatesAndRestart(
            updateInfo.TargetFullRelease);
    }
}
