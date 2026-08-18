using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GamepadApp.Services;

public enum DependencyComponent
{
    ViGEmBus,
    HidHide
}

public enum DependencyInstallStage
{
    Downloading,
    Verifying,
    StartingInstaller,
    WaitingForInstaller
}

public sealed record DependencyInstallProgress(
    DependencyInstallStage Stage,
    int? Percentage = null);

public sealed record DependencyInstallResult(
    int ExitCode,
    bool RestartRequired);

public static class DependencyInstallerService
{
    private const string ExpectedPublisher =
        "Nefarius Software Solutions";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly IReadOnlyDictionary<DependencyComponent, InstallerPackage>
        Packages = new Dictionary<DependencyComponent, InstallerPackage>
        {
            [DependencyComponent.ViGEmBus] = new(
                "ViGEmBus_1.22.0_x64_x86_arm64.exe",
                new Uri(
                    "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe"),
                "89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A"),
            [DependencyComponent.HidHide] = new(
                "HidHide_1.5.230_x64.exe",
                new Uri(
                    "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"),
                "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6")
        };

    public static async Task<DependencyInstallResult> DownloadAndRunAsync(
        DependencyComponent component,
        IProgress<DependencyInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (component == DependencyComponent.HidHide &&
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "HidHide kurucusu yalnızca x64 Windows sistemlerini destekliyor.");
        }

        InstallerPackage package = Packages[component];
        ValidateOfficialSource(package.DownloadUri);

        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "aRacnidGamepadApp",
            Guid.NewGuid().ToString("N"));

        string installerPath = Path.Combine(
            tempDirectory,
            package.FileName);

        Directory.CreateDirectory(tempDirectory);

        try
        {
            await DownloadAsync(
                package.DownloadUri,
                installerPath,
                progress,
                cancellationToken);

            progress?.Report(new DependencyInstallProgress(
                DependencyInstallStage.Verifying));

            await VerifyPackageAsync(
                installerPath,
                package.Sha256,
                cancellationToken);

            progress?.Report(new DependencyInstallProgress(
                DependencyInstallStage.StartingInstaller));

            using Process? installerProcess = Process.Start(
                new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Verb = "runas"
                });

            if (installerProcess == null)
                throw new InvalidOperationException(
                    "Kurulum programı başlatılamadı.");

            progress?.Report(new DependencyInstallProgress(
                DependencyInstallStage.WaitingForInstaller));

            await installerProcess.WaitForExitAsync(cancellationToken);

            int exitCode = installerProcess.ExitCode;
            bool restartRequired = exitCode is 1641 or 3010;

            return new DependencyInstallResult(
                exitCode,
                restartRequired);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Kurucu dosyayı kısa süre daha kullanıyorsa Windows daha sonra
                // geçici klasörü temizleyebilir. Kurulum sonucu etkilenmez.
            }
        }
    }

    public static bool IsSuccessfulExitCode(int exitCode)
    {
        return exitCode is 0 or 1641 or 3010;
    }

    private static async Task DownloadAsync(
        Uri downloadUri,
        string destinationPath,
        IProgress<DependencyInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        await using Stream source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        long downloadedBytes = 0;
        int lastReportedPercentage = -1;

        while (true)
        {
            int bytesRead = await source.ReadAsync(
                buffer,
                cancellationToken);

            if (bytesRead == 0)
                break;

            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);

            downloadedBytes += bytesRead;

            int? percentage = totalBytes is > 0
                ? (int)Math.Clamp(
                    downloadedBytes * 100L / totalBytes.Value,
                    0,
                    100)
                : null;

            if (percentage != lastReportedPercentage)
            {
                progress?.Report(new DependencyInstallProgress(
                    DependencyInstallStage.Downloading,
                    percentage));

                lastReportedPercentage = percentage ?? -1;
            }
        }
    }

    private static async Task VerifyPackageAsync(
        string installerPath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(installerPath);
        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        string actualSha256 = Convert.ToHexString(hash);

        if (!string.Equals(
                actualSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "İndirilen kurulum paketinin güvenlik doğrulaması başarısız oldu.");
        }

#pragma warning disable SYSLIB0057 // Authenticode imza sahibini EXE'den okuyan güncel bir eşdeğer API yok.
        using X509Certificate certificate =
            X509Certificate.CreateFromSignedFile(installerPath);
#pragma warning restore SYSLIB0057

        if (!certificate.Subject.Contains(
                ExpectedPublisher,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Kurulum paketinin dijital imza sahibi doğrulanamadı.");
        }
    }

    private static void ValidateOfficialSource(Uri downloadUri)
    {
        bool validSource =
            downloadUri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(
                downloadUri.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase) &&
            downloadUri.AbsolutePath.StartsWith(
                "/nefarius/",
                StringComparison.OrdinalIgnoreCase) &&
            downloadUri.AbsolutePath.Contains(
                "/releases/download/",
                StringComparison.OrdinalIgnoreCase);

        if (!validSource)
            throw new InvalidOperationException(
                "Kurulum paketi resmî kaynaktan gelmiyor.");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "aRacnid-GamepadApp/1.0");

        return client;
    }

    private sealed record InstallerPackage(
        string FileName,
        Uri DownloadUri,
        string Sha256);
}
