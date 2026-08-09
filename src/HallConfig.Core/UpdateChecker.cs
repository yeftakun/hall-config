using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace HallConfig.Core;

public class UpdateCheckResult
{
    public bool IsError { get; set; }
    public bool HasUpdate { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
}

public static class UpdateChecker
{
    private const string RepoUrl = "https://api.github.com/repos/yeftakun/hall-config/releases/latest";
    private static readonly HttpClient _httpClient = new();

    static UpdateChecker()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HallConfig", GetCurrentVersionString()));
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    private static string GetCurrentVersionString()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? Assembly.GetExecutingAssembly().GetName().Version;
        if (version == null) return "1.0.0";
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = GetCurrentVersionString()
        };

        try
        {
            Logger.Info("UpdateChecker", "Checking for updates via GitHub API...");
            
            var response = await _httpClient.GetAsync(RepoUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("tag_name", out var tagElement))
            {
                string tag = tagElement.GetString() ?? "";
                string latestVerStr = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
                
                result.LatestVersion = latestVerStr;
                
                if (root.TryGetProperty("html_url", out var urlElement))
                {
                    result.ReleaseUrl = urlElement.GetString() ?? "";
                }

                if (Version.TryParse(result.CurrentVersion, out var currentVer) && 
                    Version.TryParse(latestVerStr, out var latestVer))
                {
                    result.HasUpdate = latestVer > currentVer;
                    
                    if (result.HasUpdate)
                    {
                        Logger.Info("UpdateChecker", $"New update available: v{latestVerStr} (Current: v{result.CurrentVersion})");
                    }
                    else
                    {
                        Logger.Info("UpdateChecker", $"App is up to date (v{result.CurrentVersion})");
                    }
                }
                else
                {
                    Logger.Warn("UpdateChecker", $"Failed to parse versions: Current={result.CurrentVersion}, Latest={latestVerStr}");
                    result.IsError = true;
                }
            }
            else
            {
                Logger.Warn("UpdateChecker", "Failed to find tag_name in GitHub API response");
                result.IsError = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateChecker", "Error checking for updates", ex);
            result.IsError = true;
        }

        return result;
    }
}
