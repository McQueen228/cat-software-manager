using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CatSoftwareManager.Models;

namespace CatSoftwareManager.Services;

public sealed class OfficialVersionService
{
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;

    public OfficialVersionService(SettingsService settings)
    {
        _settings = settings;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CatSoftwareManager", "1.0"));
    }

    public IReadOnlyList<VersionSourceDefinition> LoadDefinitions()
    {
        var definitions = new List<VersionSourceDefinition>();
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("version-sources.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is not null)
                {
                    definitions.AddRange(ParseDefinitions(stream));
                }
            }
        }
        catch (Exception ex)
        {
            _settings.WriteLog("Embedded version source load failed", ex);
        }

        var userOverride = Path.Combine(_settings.AppDataDirectory, "version-sources.json");
        if (File.Exists(userOverride))
        {
            try
            {
                using var stream = File.OpenRead(userOverride);
                var overrides = ParseDefinitions(stream);
                foreach (var definition in overrides)
                {
                    definitions.RemoveAll(existing =>
                        existing.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
                    definitions.Add(definition);
                }
            }
            catch (Exception ex)
            {
                _settings.WriteLog("User version source load failed", ex);
            }
        }

        return definitions;
    }

    public VersionSourceDefinition? Match(
        ApplicationInfo application,
        IReadOnlyList<VersionSourceDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            if (!string.IsNullOrWhiteSpace(definition.Publisher) &&
                !application.Publisher.Contains(definition.Publisher, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (definition.Match.Any(pattern =>
                    Regex.IsMatch(
                        application.DisplayName,
                        pattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            {
                return definition;
            }
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<string, VersionSourceResult>> GetLatestVersionsAsync(
        IReadOnlyList<ApplicationInfo> applications,
        IReadOnlyList<VersionSourceDefinition> definitions,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var applicationMatches = applications
            .Select(application => (Application: application, Definition: Match(application, definitions)))
            .Where(item => item.Definition is not null)
            .ToArray();

        progress?.Report("official");
        var cache = new Dictionary<string, Task<VersionSourceResult>>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(6, 6);
        var tasks = applicationMatches.Select(async item =>
        {
            var definition = item.Definition!;
            Task<VersionSourceResult> queryTask;
            lock (cache)
            {
                if (!cache.TryGetValue(definition.Id, out queryTask!))
                {
                    queryTask = QueryWithGateAsync(definition, gate, cancellationToken);
                    cache[definition.Id] = queryTask;
                }
            }

            return (item.Application.Id, Result: await queryTask);
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results
            .Where(result => !string.IsNullOrWhiteSpace(result.Result.LatestVersion))
            .GroupBy(result => result.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Result, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<VersionSourceResult> QueryWithGateAsync(
        VersionSourceDefinition definition,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await QueryAsync(definition, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<VersionSourceResult> QueryAsync(
        VersionSourceDefinition definition,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(definition.Url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(definition, $"HTTP {(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var latest = definition.SourceType switch
            {
                VersionSourceType.GitHubPage => ParseGitHubPage(response.RequestMessage?.RequestUri, content),
                VersionSourceType.ChromeVersions => ParseChromeVersions(content),
                VersionSourceType.VsCodeUpdate => ParseVsCodeUpdate(content),
                VersionSourceType.EdgeUpdate => ParseEdgeUpdate(content),
                VersionSourceType.PythonLatest => ParsePythonLatest(content),
                _ => ParseRegex(content, definition.Pattern, definition.PickHighest)
            };

            if (string.IsNullOrWhiteSpace(latest))
            {
                return Failure(definition, "Version was not found in the source response.");
            }

            return new VersionSourceResult
            {
                LatestVersion = latest.Trim().TrimStart('v', 'V'),
                SourceName = definition.Name,
                SourceUrl = response.RequestMessage?.RequestUri?.ToString() ?? definition.Url
            };
        }
        catch (Exception ex)
        {
            _settings.WriteLog($"Official version query failed for {definition.Id}", ex);
            return Failure(definition, ex.Message);
        }
    }

    private static VersionSourceResult Failure(VersionSourceDefinition definition, string error) =>
        new()
        {
            LatestVersion = "",
            SourceName = definition.Name,
            SourceUrl = definition.Url,
            Error = error
        };

    private static string? ParseGitHubPage(Uri? requestUri, string content)
    {
        var source = requestUri?.AbsolutePath ?? "";
        var match = Regex.Match(source, @"/releases/tag/(?<version>[^/?#]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["version"].Value;
        }

        match = Regex.Match(
            content,
            @"/releases/tag/(?<version>v?[0-9][0-9A-Za-z.+\-_]*)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string? ParseChromeVersions(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("versions", out var versions) &&
            versions.ValueKind == JsonValueKind.Array &&
            versions.GetArrayLength() > 0 &&
            versions[0].TryGetProperty("version", out var version))
        {
            return version.GetString();
        }

        return null;
    }

    private static string? ParseVsCodeUpdate(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty("productVersion", out var version)
            ? version.GetString()
            : null;
    }

    private static string? ParseEdgeUpdate(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<string>();
        foreach (var product in document.RootElement.EnumerateArray())
        {
            if (!product.TryGetProperty("Product", out var productName) ||
                !string.Equals(productName.GetString(), "Stable", StringComparison.OrdinalIgnoreCase) ||
                !product.TryGetProperty("Releases", out var releases) ||
                releases.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var release in releases.EnumerateArray())
            {
                var platform = release.TryGetProperty("Platform", out var platformElement)
                    ? platformElement.GetString()
                    : null;
                var architecture = release.TryGetProperty("Architecture", out var architectureElement)
                    ? architectureElement.GetString()
                    : null;
                if (!string.Equals(platform, "Windows", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(architecture, "x64", StringComparison.OrdinalIgnoreCase) ||
                    !release.TryGetProperty("ProductVersion", out var versionElement))
                {
                    continue;
                }

                var version = versionElement.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    candidates.Add(version);
                }
            }
        }

        return VersionComparer.PickHighest(candidates);
    }

    private static string? ParsePythonLatest(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<string>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("is_latest", out var latestElement) ||
                latestElement.ValueKind != JsonValueKind.True ||
                !release.TryGetProperty("pre_release", out var prereleaseElement) ||
                prereleaseElement.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("version", out var majorElement) ||
                majorElement.GetInt32() != 3 ||
                !release.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var match = Regex.Match(
                nameElement.GetString() ?? "",
                @"^Python\s+(?<version>3\.\d+\.\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                candidates.Add(match.Groups["version"].Value);
            }
        }

        return VersionComparer.PickHighest(candidates);
    }

    private static string? ParseRegex(string content, string? pattern, bool pickHighest)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var versions = matches
            .Select(match => match.Groups["version"].Success
                ? match.Groups["version"].Value
                : match.Groups.Count > 1
                    ? match.Groups[1].Value
                    : "")
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .ToArray();
        return pickHighest ? VersionComparer.PickHighest(versions) : versions.FirstOrDefault();
    }

    private static IReadOnlyList<VersionSourceDefinition> ParseDefinitions(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<VersionSourceDefinition>();
        }

        var definitions = new List<VersionSourceDefinition>();
        foreach (var source in sources.EnumerateArray())
        {
            try
            {
                var typeText = GetString(source, "type") ?? "";
                var sourceType = typeText switch
                {
                    "githubPage" => VersionSourceType.GitHubPage,
                    "chromeVersions" => VersionSourceType.ChromeVersions,
                    "vscodeUpdate" => VersionSourceType.VsCodeUpdate,
                    "edgeUpdate" => VersionSourceType.EdgeUpdate,
                    "pythonLatest" => VersionSourceType.PythonLatest,
                    _ => VersionSourceType.Regex
                };

                var match = source.TryGetProperty("match", out var matchElement) &&
                            matchElement.ValueKind == JsonValueKind.Array
                    ? matchElement.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
                    : source.TryGetProperty("match", out var singleMatch)
                        ? new[] { singleMatch.GetString() ?? "" }
                        : Array.Empty<string>();

                definitions.Add(new VersionSourceDefinition
                {
                    Id = GetString(source, "id") ?? Guid.NewGuid().ToString("N"),
                    Name = GetString(source, "name") ?? "Official",
                    Match = match,
                    Publisher = GetString(source, "publisher"),
                    WingetId = GetString(source, "wingetId"),
                    HomepageUrl = GetString(source, "homepage"),
                    UpdatePageUrl = GetString(source, "updatePage"),
                    SourceType = sourceType,
                    Url = GetString(source, "url") ?? "",
                    Pattern = GetString(source, "pattern"),
                    PickHighest = source.TryGetProperty("pickHighest", out var pick) &&
                                  pick.ValueKind == JsonValueKind.True
                });
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Invalid version source definition.", ex);
            }
        }

        return definitions;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
