using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace PaperTodo;

// === Custom patch (dev branch): update check / OTA service ===
// Checks the fork's GitHub releases for a newer version, then upgrades in place.
// A release carries a manifest.json with a SHA-256 hash per file plus a delta bundle
// (only files that changed since the previous release). The client hashes its local
// files, and downloads just the delta bundle when it covers every difference —
// otherwise the full multi-file ZIP. Every extracted file is verified before the
// running binaries are swapped via the rename trick and the app restarts.
internal static class UpdateService
{
    private const string Repository = "coochow/PaperTodo";
    private const string ReleasesApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    private const string ManifestAssetName = "manifest.json";
    // Internal mirror (GitLab common_tools, comX-style latest.json). Probed FIRST:
    // in-network users get fast downloads without touching GitHub; the mirror serves
    // the same versioned assets (zip + manifest_<v>.json + delta zip) with hashes
    // identical to the GitHub Release.
    private const string InternalLatestUrl =
        "https://gitlab.vmic.xyz/res/soft/common_tools/-/raw/master/PaperTodo/latest.json";
    private const string InternalFileRoot =
        "https://gitlab.vmic.xyz/res/soft/common_tools/-/raw/master/PaperTodo/File";

    private static readonly HttpClient Http = CreateHttpClient();

    // Serialized into the release's manifest.json asset.
    internal sealed class ReleaseManifest
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "";

        [JsonPropertyName("packaging")]
        public string Packaging { get; set; } = "";

        [JsonPropertyName("files")]
        public List<ManifestFile> Files { get; set; } = new();

        [JsonPropertyName("delta")]
        public ManifestDelta? Delta { get; set; }
    }

    internal sealed class ManifestFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";
    }

    internal sealed class ManifestDelta
    {
        [JsonPropertyName("from")]
        public string From { get; set; } = "";

        [JsonPropertyName("asset")]
        public string Asset { get; set; } = "";

        [JsonPropertyName("files")]
        public List<string> Files { get; set; } = new();

        [JsonPropertyName("deleted")]
        public List<string> Deleted { get; set; } = new();
    }

    internal sealed class UpdateInfo
    {
        public required string Version { get; init; }
        public required string ReleaseUrl { get; init; }
        public required string Manifest { get; init; }
        public required string MultiFileZipUrl { get; init; }
        public string? DeltaZipUrl { get; init; }
        // Custom patch (dev branch): the other channel's URL for the SAME assets. The mirror
        // and the GitHub release are byte-identical, so one flaky host must not fail an
        // upgrade (the 2026-11-18 .. 11-24 failure streak came from exactly that).
        public string? FallbackMultiFileZipUrl { get; init; }
        public string? FallbackDeltaZipUrl { get; init; }
        public required string ReleaseNotes { get; init; }
    }

    internal sealed class UpdateProgress
    {
        public long TotalBytes { get; init; }
        public long CompletedBytes { get; init; }
    }

    // Custom patch (dev branch): stage-aware update failure reporting. The old contract was a
    // bare false, which made the multi-day failure streak (2026-11-18 .. 11-24) undiagnosable.
    // Every phase now records where it stopped and appends a line to
    // %LOCALAPPDATA%\PaperTodo\update-trace.log.
    internal enum UpdateFailureStage
    {
        None,
        NotSupported,
        Manifest,
        Download,
        Extract,
        Apply,
        Restart,
        AlreadyCurrent,
        Unknown
    }

    // The local files already equal the release payload; re-running the update would only
    // rewrite identical bytes. Reported under its own stage so the About page can say
    // "already current" instead of surfacing a scary failure.
    private sealed class UpdateAlreadyCurrentException : Exception
    {
        internal UpdateAlreadyCurrentException(string message) : base(message) { }
    }

    private static readonly object TraceLogLock = new();
    private static UpdateFailureStage _currentStage = UpdateFailureStage.None;

    internal static UpdateFailureStage LastFailureStage { get; private set; }
    internal static string LastFailureDetail { get; private set; } = "";

    private static void BeginStage(UpdateFailureStage stage) => _currentStage = stage;

    private static void RecordFailure(UpdateFailureStage stage, Exception ex)
    {
        LastFailureStage = stage;
        LastFailureDetail = ex.Message;
        TraceLog($"{stage} failed: {ex}");
    }

    private static void ClearFailure()
    {
        _currentStage = UpdateFailureStage.None;
        LastFailureStage = UpdateFailureStage.None;
        LastFailureDetail = "";
    }

    private static void TraceLog(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        Trace.WriteLine($"[Update] {message}");
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PaperTodo");
            Directory.CreateDirectory(directory);
            var logPath = Path.Combine(directory, "update-trace.log");
            var existing = new FileInfo(logPath);
            if (existing.Exists && existing.Length > 512 * 1024)
            {
                File.Move(logPath, logPath + ".1", overwrite: true);
            }
            lock (TraceLogLock)
            {
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Best-effort; a failed log write must never fail the upgrade.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"PaperTodo-Updater/{CurrentVersion}");
        return client;
    }

    /// <summary>
    /// Probe helper shared with the About page's doc-link fallback: issues a plain
    /// GET and returns the response as-is; callers impose their own timeout.
    /// </summary>
    internal static Task<HttpResponseMessage> ProbeRawUrlAsync(string url, CancellationToken cancellation) =>
        Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);

    internal static string CurrentVersion => ResolveCurrentVersion();

    // Custom patch (dev branch): v1.0.4 shipped a stale <InformationalVersion>1.0.3</...>
    // while the binaries were 1.0.4, so the About page showed 1.0.3 and every check
    // re-offered the installed release as new. When the assembly version is ahead of
    // the informational one, trust the assembly version; otherwise keep the
    // informational string as-is (it may carry a pre-release label).
    private static string ResolveCurrentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0] ?? "";
        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is null)
        {
            return informational;
        }
        var assemblyCore =
            $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        if (TryCompareCoreVersions(assemblyCore, informational.Split('-', '+')[0].Trim(), out var order) &&
            order > 0)
        {
            return assemblyCore;
        }
        return informational;
    }

    // A single-file install is one PaperTodo.exe next to no runtime DLL layout.
    private static bool IsSingleFileInstall =>
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "papertodo_lmdb.dll")) &&
        Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Length == 0;

    internal static bool IsNewerVersion(string candidate, string current)
    {
        // Both sides carry an optional pre-release label. Numeric-only comparison first
        // (a newer core beats any label); on numeric ties, an unlabeled version is newer
        // than a labeled one, and otherwise the label itself breaks the tie.
        var candidateCore = candidate.Split('-', '+')[0].Trim();
        var currentCore = current.Split('-', '+')[0].Trim();
        if (TryCompareCoreVersions(candidateCore, currentCore, out var order) && order != 0)
        {
            return order > 0;
        }
        var candidateLabel = LabelOf(candidate);
        var currentLabel = LabelOf(current);
        return CompareLabels(candidateLabel, currentLabel) > 0;
    }

    private static string LabelOf(string version)
    {
        var dash = version.IndexOf('-');
        var plus = version.IndexOf('+');
        if (dash < 0)
        {
            return "";
        }
        return plus > dash ? version.Substring(dash + 1, plus - dash - 1) : version[(dash + 1)..];
    }

    // No label = final release (newer); otherwise lexicographic on the label text.
    private static int CompareLabels(string candidate, string current)
    {
        if (candidate.Length == 0 && current.Length == 0) return 0;
        if (candidate.Length == 0) return 1;
        if (current.Length == 0) return -1;
        return string.CompareOrdinal(candidate, current);
    }

    private static bool TryCompareCoreVersions(string left, string right, out int order)
    {
        order = 0;
        if (Version.TryParse(NormalizeVersion(left), out var a) &&
            Version.TryParse(NormalizeVersion(right), out var b))
        {
            order = a.CompareTo(b);
            return true;
        }
        return false;
    }

    // Version.TryParse rejects missing patch digits; feed it a normalized
    // 4-digit shadow of the real number.
    private static string NormalizeVersion(string version)
    {
        var core = version.Trim();
        var parts = core.Split('.');
        if (parts.Length > 4)
        {
            core = string.Join(".", parts.Take(4));
        }
        while (core.Count(c => c == '.') < 3)
        {
            core += ".0";
        }
        return core;
    }

    /// <summary>Fetches the latest release manifest; returns null when up to date.</summary>
    internal static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        // Internal mirror first: same versioned assets, faster on the intranet, and
        // an unauthenticated raw URL (no rate limits). Falls through to GitHub only
        // when the mirror itself FAILED — the mirror's "up to date" verdict is
        // AUTHORITATIVE (both channels publish the same release), so internal users
        // never depend on GitHub's anonymous API quota just to hear "no update".
        // Custom patch note: v1.0.2 conflated "mirror says up to date" with "mirror
        // failed" in a single null, so a GitHub quota 403 turned a clean "latest"
        // verdict into a spurious failure report.
        var mirror = await TryCheckInternalMirrorAsync().ConfigureAwait(false);
        if (mirror.ResultKind == MirrorCheckResultKind.UpToDate)
        {
            return null;
        }
        if (mirror.ResultKind == MirrorCheckResultKind.UpdateInfo && mirror.Info is { } found)
        {
            return found;
        }
        return await CheckGitHubReleaseAsync().ConfigureAwait(false);
    }

    // Custom patch (dev branch): three-way mirror outcome so the "up to date"
    // verdict is distinguishable from a failed probe.
    private enum MirrorCheckResultKind
    {
        UpdateInfo,
        UpToDate,
        Failed
    }

    private sealed record MirrorCheckResult(MirrorCheckResultKind ResultKind, UpdateInfo? Info = null);

    private static async Task<MirrorCheckResult> TryCheckInternalMirrorAsync()
    {
        try
        {
            using var probe = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var pointerJson = await Http.GetStringAsync(InternalLatestUrl, probe.Token).ConfigureAwait(false);
            using var pointer = JsonDocument.Parse(StripBom(pointerJson));
            var root = pointer.RootElement;
            var version = GetStringProperty(root, "LatestVersion").Trim();
            if (string.IsNullOrWhiteSpace(version))
            {
                return new MirrorCheckResult(MirrorCheckResultKind.Failed);
            }
            if (!IsNewerVersion(version, CurrentVersion))
            {
                // Up to date per the mirror — AUTHORITATIVE: both channels publish the
                // same release, so GitHub's "no update" adds nothing while its anonymous
                // quota can turn this verdict into a spurious failure report.
                // Custom patch (dev branch): the record must travel back, not null — the
                // caller dereferences .ResultKind, so a bare null turned the authoritative
                // up-to-date verdict into a NullReferenceException ("检查更新失败")
                // whenever the mirror answered and the install was already current.
                return new MirrorCheckResult(MirrorCheckResultKind.UpToDate);
            }

            var manifestName = GetStringProperty(root, "Manifest");
            if (string.IsNullOrWhiteSpace(manifestName))
            {
                return new MirrorCheckResult(MirrorCheckResultKind.Failed);
            }
            var manifestJson = await Http.GetStringAsync(
                $"{InternalFileRoot}/{Uri.EscapeDataString(manifestName)}", probe.Token).ConfigureAwait(false);
            var manifest = ParseManifest(manifestJson);
            if (!string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase))
            {
                return new MirrorCheckResult(MirrorCheckResultKind.Failed); // mirror out of sync: let GitHub decide
            }

            var zipName = GetStringProperty(root, "FileName");
            if (string.IsNullOrWhiteSpace(zipName))
            {
                return new MirrorCheckResult(MirrorCheckResultKind.Failed);
            }
            var deltaUrl = manifest.Delta is { } delta && !string.IsNullOrWhiteSpace(delta.Asset)
                ? $"{InternalFileRoot}/{Uri.EscapeDataString(delta.Asset)}"
                : null;
            return new MirrorCheckResult(MirrorCheckResultKind.UpdateInfo, new UpdateInfo
            {
                Version = version,
                ReleaseUrl = $"https://gitlab.vmic.xyz/res/soft/common_tools/-/tree/master/PaperTodo",
                Manifest = manifestJson,
                MultiFileZipUrl = $"{InternalFileRoot}/{Uri.EscapeDataString(zipName)}",
                DeltaZipUrl = deltaUrl,
                // Custom patch (dev branch): the GitHub release serves byte-identical assets;
                // used when the mirror's download host is unreachable or flaky.
                FallbackMultiFileZipUrl =
                    $"https://github.com/{Repository}/releases/download/v{version}/{zipName}",
                FallbackDeltaZipUrl = manifest.Delta?.Asset is { Length: > 0 } deltaAssetName
                    ? $"https://github.com/{Repository}/releases/download/v{version}/{deltaAssetName}"
                    : null,
                ReleaseNotes = root.TryGetProperty("ReleaseNote", out var noteElement) &&
                               noteElement.ValueKind == JsonValueKind.Object &&
                               noteElement.TryGetProperty("zh-CN", out var zhNote) &&
                               zhNote.ValueKind == JsonValueKind.String
                    ? zhNote.GetString() ?? ""
                    : ""
            });
        }
        catch
        {
            // Mirror unreachable/malformed: fall back to GitHub silently.
            return new MirrorCheckResult(MirrorCheckResultKind.Failed);
        }
    }

    private static async Task<UpdateInfo?> CheckGitHubReleaseAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var release = await GetJsonAsync(ReleasesApiUrl, cts.Token).ConfigureAwait(false);
        var tag = GetStringProperty(release.RootElement, "tag_name");
        var version = tag.TrimStart('v');
        if (string.IsNullOrWhiteSpace(version) || !IsNewerVersion(version, CurrentVersion))
        {
            return null;
        }

        // Map every asset name to its direct download URL; URLs come from the API
        // response instead of being constructed, so asset naming stays CI-side.
        var assetUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (release.RootElement.TryGetProperty("assets", out var assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = GetStringProperty(asset, "name");
                var url = GetStringProperty(asset, "browser_download_url");
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                {
                    assetUrls[name] = url;
                }
            }
        }
        if (!assetUrls.TryGetValue(ManifestAssetName, out var manifestUrl))
        {
            return null;
        }

        var manifestJson = await Http.GetStringAsync(manifestUrl, cts.Token).ConfigureAwait(false);
        // Validation happens up front so the UI can fail fast on a malformed release.
        var manifest = ParseManifest(manifestJson);
        if (!string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The ZIP is the only published package. Releases before the asset slim-down
        // also carried a single-file exe, but the client no longer requires or uses it.
        var zipUrl = assetUrls.FirstOrDefault(kv =>
            kv.Key.StartsWith("PaperTodo-v", StringComparison.OrdinalIgnoreCase) &&
            kv.Key.EndsWith("-win-x64-multi-file.zip", StringComparison.OrdinalIgnoreCase));
        if (zipUrl.Value is null)
        {
            return null;
        }

        return new UpdateInfo
        {
            Version = version,
            ReleaseUrl = GetStringProperty(release.RootElement, "html_url"),
            Manifest = manifestJson,
            MultiFileZipUrl = zipUrl.Value,
            DeltaZipUrl = manifest.Delta is { } delta && !string.IsNullOrWhiteSpace(delta.Asset) &&
                          assetUrls.TryGetValue(delta.Asset, out var deltaUrl)
                ? deltaUrl
                : null,
            // Custom patch (dev branch): the mirror serves the same assets when GitHub's
            // download host is blocked or slow.
            FallbackMultiFileZipUrl = $"{InternalFileRoot}/{Uri.EscapeDataString(zipUrl.Key)}",
            FallbackDeltaZipUrl = manifest.Delta?.Asset is { Length: > 0 } mirrorDeltaAsset
                ? $"{InternalFileRoot}/{Uri.EscapeDataString(mirrorDeltaAsset)}"
                : null,
            ReleaseNotes = GetStringProperty(release.RootElement, "body")
        };
    }

    private static string GetStringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static string StripBom(string json)
    {
        // PowerShell/file-tool authored JSON on the mirror may carry a UTF-8 BOM;
        // JsonDocument rejects leading BOM bytes.
        return json.Length > 0 && json[0] == '﻿' ? json[1..] : json;
    }

    private static ReleaseManifest ParseManifest(string json)
    {
        ReleaseManifest manifest;
        try
        {
            // PowerShell Set-Content may prepend a UTF-8 BOM; JsonSerializer rejects
            // leading BOM bytes, so strip them before parsing.
            if (json.Length > 0 && json[0] == '﻿')
            {
                json = json[1..];
            }
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false }) ?? new ReleaseManifest();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The release manifest is malformed.", ex);
        }
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException("The release manifest has no version.");
        }
        if (manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("The release manifest lists no files.");
        }
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) ||
                file.Path.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(file.Path) ||
                file.Size < 0 ||
                !TryNormalizeSha256(file.Sha256, out _))
            {
                throw new InvalidOperationException($"The release manifest entry '{file.Path}' is invalid.");
            }
        }
        return manifest;
    }

    private static bool TryNormalizeSha256(string value, out string normalized)
    {
        normalized = value.Trim().ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellation)
    {
        using var response = await Http.GetAsync(url, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads and stages the update, then swaps files and restarts the app.
    /// Returns false when the update cannot proceed (network, checksum, disk).
    /// </summary>
    internal static async Task<bool> TryUpdateToAsync(
        UpdateInfo info,
        Action<UpdateProgress>? onProgress = null,
        CancellationToken cancellation = default)
    {
        ClearFailure();
        try
        {
            var staged = await StageUpdateAsync(info, onProgress, cancellation).ConfigureAwait(false);
            BeginStage(UpdateFailureStage.Apply);
            return ApplyStagedUpdate(staged.ApplyRoot, staged.ApplyFiles, staged.DeletedFiles,
                restartAfterExit: true);
        }
        catch (UpdateAlreadyCurrentException ex)
        {
            RecordFailure(UpdateFailureStage.AlreadyCurrent, ex);
            return false;
        }
        catch (Exception ex)
        {
            // Custom patch (dev branch): record the failing stage (detail goes to the trace
            // log) instead of collapsing every cause into a bare false, and let no exception
            // type escape onto the About page.
            RecordFailure(_currentStage == UpdateFailureStage.None
                ? UpdateFailureStage.Unknown
                : _currentStage, ex);
            return false;
        }
    }

    private sealed record StagedUpdate(string ApplyRoot, IReadOnlyList<string> ApplyFiles,
        IReadOnlyList<string> DeletedFiles);

    private static async Task<StagedUpdate> StageUpdateAsync(
        UpdateInfo info,
        Action<UpdateProgress>? onProgress,
        CancellationToken cancellation)
    {
        if (IsSingleFileInstall)
        {
            BeginStage(UpdateFailureStage.NotSupported);
            // The single-file asset was dropped from releases: it offered no incremental
            // path and duplicated the ZIP's size. Fail with a clear message instead of a
            // null-URL ArgumentNullException piercing the catch filter; the About page
            // then points the user at a manual ZIP download.
            throw new InvalidOperationException(
                "Single-file installs no longer support in-app updates. Please download the multi-file ZIP from the release page and replace this copy.");
        }
        return await StageBundleUpdateAsync(info, onProgress, cancellation).ConfigureAwait(false);
    }

    private static string CreateStagingRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PaperTodo",
            "update-staging");
        if (Directory.Exists(root))
        {
            try { Directory.Delete(root, recursive: true); } catch { /* recreated below */ }
        }
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<StagedUpdate> StageBundleUpdateAsync(
        UpdateInfo info,
        Action<UpdateProgress>? onProgress,
        CancellationToken cancellation)
    {
        BeginStage(UpdateFailureStage.Manifest);
        var manifest = ParseManifest(info.Manifest);
        if (!IsNewerVersion(manifest.Version, CurrentVersion))
        {
            throw new InvalidOperationException("The manifest does not describe a newer version.");
        }
        var manifestFiles = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);

        // Local diff: everything missing or hash-mismatched must be delivered by the payload.
        var missing = manifest.Files.Where(f => !LocalFileMatches(f)).Select(f => f.Path).ToList();

        // Custom patch (dev branch): deletions are version-independent (the listed files do
        // not exist in the new release), so they apply on both the delta and the full path.
        // Computed before downloading because "nothing to change" short-circuits below.
        var deleted = manifest.Delta?.Deleted is { } deletedList
            ? deletedList.Where(d => manifestFiles.ContainsKey(d) == false).ToList()
            : new List<string>();
        if (missing.Count == 0 && deleted.Count == 0)
        {
            // Every local file already equals the release payload — typically a stale
            // <InformationalVersion> re-offering the release that is already installed.
            throw new UpdateAlreadyCurrentException(
                $"All files already match the {manifest.Version} manifest.");
        }

        var useDelta = info.DeltaZipUrl is not null &&
                       manifest.Delta is { } delta &&
                       delta.Files.Count > 0 &&
                       missing.All(path => delta.Files.Contains(path, StringComparer.OrdinalIgnoreCase));
        var root = CreateStagingRoot();
        var payloadPath = Path.Combine(root, "payload.zip");
        BeginStage(UpdateFailureStage.Download);
        useDelta = await DownloadPayloadAsync(info, useDelta, payloadPath, onProgress, cancellation)
            .ConfigureAwait(false);
        BeginStage(UpdateFailureStage.Extract);

        // Extract only the entries the apply step needs; verify every extracted file
        // against the manifest so a truncated or tampered payload never lands on disk.
        // Release ZIPs nest everything under a single PaperTodo/ top folder (plus a
        // "!" run-me note that is intentionally absent from the manifest) — strip the
        // prefix and skip unlisted entries so both the nested and the legacy flat
        // layouts apply identically. Delta bundles stay flat (manifest-relative).
        var applyRoot = Path.Combine(root, "apply");
        Directory.CreateDirectory(applyRoot);
        var applyFiles = new List<string>();
        using (var archive = ZipFile.OpenRead(payloadPath))
        {
            foreach (var entry in archive.Entries)
            {
                var entryName = NormalizeZipEntryName(entry.FullName);
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    continue; // directory record
                }
                entryName = StripTopLevelFolder(entryName);
                if (!manifestFiles.TryGetValue(entryName, out var manifestFile))
                {
                    if (useDelta)
                    {
                        throw new InvalidOperationException(
                            $"The delta payload contains a file absent from the manifest: {entryName}");
                    }
                    continue; // full ZIP: extra files (run-me note, changed extras) never overwrite anything
                }
                if (!useDelta && !missing.Contains(entryName, StringComparer.OrdinalIgnoreCase))
                {
                    continue; // full payload: only replace files that actually differ
                }
                var extracted = Path.Combine(applyRoot, entryName);
                Directory.CreateDirectory(Path.GetDirectoryName(extracted) ?? applyRoot);
                entry.ExtractToFile(extracted, overwrite: true);
                if (!FileMatchesSha256(extracted, manifestFile))
                {
                    throw new InvalidOperationException(
                        $"Checksum mismatch after extraction: {entryName}");
                }
                applyFiles.Add(entryName);
            }
        }
        if (applyFiles.Count == 0)
        {
            throw new InvalidOperationException("The payload contains no applicable files.");
        }

        // Deletions were computed before the download (see the AlreadyCurrent guard).
        return new StagedUpdate(applyRoot, applyFiles, deleted);
    }

    private static string NormalizeZipEntryName(string fullName)
    {
        // ZIP separators may be either '/' or '\'; normalize to the OS separator and
        // reject anything that tries to escape the apply root.
        var normalized = fullName.Replace('\\', '/');
        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            return ""; // directory record
        }
        var segments = normalized.Split('/');
        if (segments.Any(string.IsNullOrEmpty) ||
            segments.Contains("..") ||
            normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The payload contains an unsafe entry name: {fullName}");
        }
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    // Release ZIPs put the whole app under one "PaperTodo" top-level folder; the
    // manifest and the install directory are prefix-free, so strip it when present.
    // Delta bundles and pre-nesting releases match as-is (returns input unchanged).
    private static string StripTopLevelFolder(string entryName)
    {
        var separator = Path.DirectorySeparatorChar;
        var firstSep = entryName.IndexOf(separator);
        if (firstSep <= 0)
        {
            return entryName;
        }
        return string.Equals(entryName[..firstSep], "PaperTodo", StringComparison.OrdinalIgnoreCase)
            ? entryName[(firstSep + 1)..]
            : entryName;
    }

    private static bool LocalFileMatches(ManifestFile file)
    {
        try
        {
            var local = Path.Combine(AppContext.BaseDirectory, file.Path);
            return FileMatchesSha256(local, file);
        }
        catch
        {
            return false;
        }
    }

    private static bool FileMatchesSha256(string path, ManifestFile file)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        var digest = Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
        return TryNormalizeSha256(file.Sha256, out var expected) && digest == expected;
    }

    private static async Task DownloadOnceAsync(
        string url,
        string targetPath,
        Action<UpdateProgress>? onProgress,
        CancellationToken cancellation)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        await using var target = File.Create(targetPath);
        var buffer = new byte[64 * 1024];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            received += read;
            onProgress?.Invoke(new UpdateProgress { TotalBytes = total, CompletedBytes = received });
        }
        if (total >= 0 && received != total)
        {
            throw new InvalidOperationException($"Downloaded payload is shorter than expected: {url}");
        }
    }

    // Custom patch (dev branch): one download used to mean one URL from whichever channel
    // answered the check — a flaky mirror was a guaranteed failure day (2026-11-18 .. 11-24)
    // although both channels serve byte-identical assets. Now: retry the same asset on the
    // other channel first, then degrade delta → full, so one bad host never blocks an update.
    // Returns whether the payload that actually landed is the delta (vs the full package).
    private static async Task<bool> DownloadPayloadAsync(
        UpdateInfo info,
        bool useDelta,
        string targetPath,
        Action<UpdateProgress>? onProgress,
        CancellationToken cancellation)
    {
        var attempts = new List<(string Url, bool Delta)>();
        if (useDelta && info.DeltaZipUrl is { } deltaPrimary)
        {
            attempts.Add((deltaPrimary, true));
            if (info.FallbackDeltaZipUrl is { } deltaBackup)
            {
                attempts.Add((deltaBackup, true));
            }
        }
        attempts.Add((info.MultiFileZipUrl, false));
        if (info.FallbackMultiFileZipUrl is { } fullBackup)
        {
            attempts.Add((fullBackup, false));
        }

        Exception? last = null;
        foreach (var (url, delta) in attempts)
        {
            try
            {
                TryDeleteFile(targetPath);
                onProgress?.Invoke(new UpdateProgress());
                await DownloadOnceAsync(url, targetPath, onProgress, cancellation)
                    .ConfigureAwait(false);
                if (!delta)
                {
                    TraceLog("delta payload unavailable; downloaded the full package instead.");
                }
                return delta;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                                        IOException or InvalidOperationException)
            {
                last = ex;
                TraceLog($"download attempt failed ({HostOf(url)}): {ex.Message}");
            }
        }
        throw new InvalidOperationException(
            $"download failed on every channel ({string.Join(", ", attempts.Select(a => HostOf(a.Url)))}): {last?.Message}",
            last);
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    /// <summary>Atomically swaps staged files into place; the caller restarts the app.</summary>
    private static bool ApplyStagedUpdate(
        string applyRoot,
        IReadOnlyList<string> applyFiles,
        IReadOnlyList<string> deletedFiles,
        bool restartAfterExit)
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            // Phase 1: park every file that will be replaced or removed (delete pending
            // across the restart — a loaded DLL cannot be deleted, but it can be renamed).
            foreach (var relativeName in applyFiles.Concat(deletedFiles))
            {
                var live = Path.Combine(appDir, relativeName);
                if (!File.Exists(live))
                {
                    continue;
                }
                var parked = live + ".old";
                TryDeleteFile(parked);
                File.Move(live, parked);
            }
            // Phase 2: move staged files in. A failure here leaves .old files behind and
            // possibly a half-applied update; the user re-runs the update to finish it.
            var movedIn = 0;
            foreach (var relativeName in applyFiles)
            {
                var staged = Path.Combine(applyRoot, relativeName);
                var live = Path.Combine(appDir, relativeName);
                Directory.CreateDirectory(Path.GetDirectoryName(live) ?? appDir);
                File.Move(staged, live);
                movedIn++;
            }
            // Custom patch (dev branch): once every apply file has moved in, the update
            // IS applied — anything failing afterwards (staging cleanup hitting a locked
            // file, restart marshalling) must not turn a completed swap into a "failed"
            // report, as observed during the 2026-11-12 v1.0.1-v1.0.2 upgrade.
            if (movedIn == applyFiles.Count)
            {
                try
                {
                    TryDeleteDirectory(Path.GetDirectoryName(applyRoot) ?? applyRoot);
                }
                catch (Exception cleanupEx)
                {
                    Trace.WriteLine($"[Update] staging cleanup after apply skipped: {cleanupEx.Message}");
                }
                if (restartAfterExit && Application.Current is App app)
                {
                    app.RequestRestartAfterExit();
                    app.Dispatcher.BeginInvoke(() => RestartAfterApply());
                }
                return true;
            }
            return true;
        }
        catch (Exception ex)
        {
            // Custom patch (dev branch): name the stage so a half-applied swap is obvious in
            // the trace log instead of a bare "failed".
            RecordFailure(UpdateFailureStage.Apply, ex);
            return false;
        }
    }

    // Custom patch (dev branch): the apply runs on a worker thread, so the close-and-restart
    // must marshal to the UI thread, use the controller's real-close path, and still fall
    // back to a bare Shutdown for the unlikely case the controller is already gone.
    private static void RestartAfterApply()
    {
        try
        {
            var controller = AppController.Current;
            if (controller is not null)
            {
                controller.Exit();
                return;
            }
        }
        catch (Exception ex)
        {
            RecordFailure(UpdateFailureStage.Restart, ex);
        }
        try
        {
            Application.Current.Shutdown();
        }
        catch
        {
        }
    }

    internal static void CleanupLeftoverUpdateFiles()
    {
        try
        {
            foreach (var parked in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.old",
                         SearchOption.AllDirectories))
            {
                TryDeleteFile(parked);
            }
            var stagingRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PaperTodo",
                "update-staging");
            TryDeleteDirectory(stagingRoot);
        }
        catch
        {
            // Best-effort only; files still in use are retried on the next launch.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* in use */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* in use */ }
    }

    // Scheduled silent check used by the startup hook; surfaces a tray balloon only
    // when a newer release exists. Never throws onto the UI thread.
    internal static void ScheduleStartupCheck(Dispatcher dispatcher, TaskbarIcon trayIcon)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var info = await CheckForUpdateAsync().ConfigureAwait(false);
                if (info == null)
                {
                    return;
                }
                _ = dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        RoutedEventHandler? onBalloonClicked = null;
                        onBalloonClicked = (_, _) =>
                        {
                            trayIcon.TrayBalloonTipClicked -= onBalloonClicked!;
                            AppController.Current?.OpenAboutSettingsPage();
                        };
                        trayIcon.TrayBalloonTipClicked += onBalloonClicked;
                        trayIcon.ShowBalloonTip(
                            Strings.Get("UpdateAvailableTitle"),
                            string.Format(Strings.Get("UpdateAvailableMessage"), info.Version),
                            BalloonIcon.Info);
                    }
                    catch { /* tray already gone */ }
                });
            }
            catch
            {
                // Offline / rate-limited: silent.
            }
        });
    }
}
