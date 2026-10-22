namespace CatSoftwareManager.Services;

public sealed class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>
            {
                ["AppTitle"] = "猫猫的软件管理器",
                ["Subtitle"] = "掌握已安装软件的版本动态",
                ["SearchHint"] = "搜索软件",
                ["UpdatesOnly"] = "只看可更新",
                ["Refresh"] = "立即检查",
                ["RefreshShort"] = "刷新",
                ["Checking"] = "正在扫描软件和版本…",
                ["ScanningInstalled"] = "正在读取已安装软件…",
                ["ScanningOfficial"] = "正在向官方来源查询…",
                ["ScanningWinget"] = "正在核对 Winget 数据…",
                ["Ready"] = "扫描完成",
                ["LastCheckedNever"] = "尚未检查",
                ["LastChecked"] = "上次检查：{0}",
                ["NoApps"] = "没有找到可管理的软件",
                ["NoMatching"] = "没有符合条件的软件",
                ["AllUpToDate"] = "当前没有可更新的软件",
                ["UpdateAvailableCount"] = "{0} 款软件有更新",
                ["Current"] = "当前",
                ["Latest"] = "最新",
                ["LatestUnknown"] = "无法检测",
                ["CurrentUnknown"] = "未知",
                ["UpToDate"] = "已是最新",
                ["Available"] = "可更新",
                ["Skipped"] = "已跳过此版本",
                ["Ignored"] = "已忽略更新",
                ["Unknown"] = "暂无可靠版本源",
                ["Update"] = "更新",
                ["OpenOfficial"] = "打开官网",
                ["Ignore"] = "忽略",
                ["Restore"] = "恢复提示",
                ["RestoreSkip"] = "恢复",
                ["SourceOfficial"] = "官网",
                ["SourceWinget"] = "Winget",
                ["SourceUnknown"] = "来源未知",
                ["MenuOpen"] = "打开管理器",
                ["MenuCheckNow"] = "立即检查",
                ["MenuSettings"] = "设置",
                ["MenuExit"] = "退出",
                ["Settings"] = "设置",
                ["Language"] = "界面语言",
                ["Chinese"] = "简体中文",
                ["English"] = "English",
                ["Theme"] = "外观",
                ["SystemTheme"] = "跟随系统",
                ["LightTheme"] = "浅色",
                ["DarkTheme"] = "深色",
                ["AutoStart"] = "登录 Windows 后静默启动",
                ["CheckManagerUpdate"] = "检查管理器更新",
                ["Version"] = "版本 {0}",
                ["PromptTitle"] = "发现软件更新",
                ["PromptSummary"] = "共发现 {0} 个新版本。勾选要安装的项目。",
                ["ConfirmSelected"] = "确认更新",
                ["Cancel"] = "取消",
                ["SkipSelected"] = "跳过本次",
                ["IgnoreSelected"] = "忽略所选",
                ["NoSelected"] = "请至少勾选一款软件",
                ["Updating"] = "正在更新 {0}…",
                ["UpdateSucceeded"] = "{0} 更新完成",
                ["UpdateFailed"] = "{0} 更新失败",
                ["UpdateFailedDetail"] = "{0} 更新失败：{1}",
                ["RestartRequired"] = "{0} 需要重启电脑才能完成更新。",
                ["RestartPrompt"] = "需要重启电脑才能完成更新。是否现在重启？",
                ["RestartNow"] = "立即重启",
                ["Later"] = "稍后",
                ["RunningProcessTitle"] = "软件正在运行",
                ["RunningProcessMessage"] = "{0} 正在运行。请先手动关闭软件，然后点击“重试”。管理器不会替你关闭程序。",
                ["Retry"] = "重试",
                ["NoUpdateChannel"] = "没有可用的直接更新通道，将打开官方网站。",
                ["UpdateSummaryDone"] = "更新流程结束：成功 {0}，失败 {1}",
                ["PowerOffline"] = "当前使用电池，接通电源后会自动检查",
                ["NetworkOffline"] = "网络不可用，恢复后会自动检查",
                ["DailyAlreadyChecked"] = "今天已经完成自动检查",
                ["NoUpdateChannelShort"] = "无更新通道",
                ["WingetMissing"] = "未找到 Winget，无法执行直接更新",
                ["OfficialSourceError"] = "未能读取官网版本",
                ["AppSelfUpdateUnavailable"] = "暂时无法读取管理器更新信息",
                ["AppSelfUpdateAvailable"] = "管理器有新版本 {0}",
                ["OpenLogs"] = "打开日志目录",
                ["Close"] = "关闭",
                ["UnknownVersion"] = "未知版本",
                ["ManagerUpdateLatest"] = "当前已经是最新版本",
                ["ManagerUpdateOpen"] = "发现管理器新版本 {0}。是否现在下载并安装？",
                ["ManagerUpdateChecking"] = "正在检查管理器更新…",
                ["ManagerUpdateDownloading"] = "正在下载管理器更新…",
                ["ManagerUpdateInstallFailed"] = "管理器更新下载或启动失败：{0}"
            },
            ["en-US"] = new Dictionary<string, string>
            {
                ["AppTitle"] = "Cat Software Manager",
                ["Subtitle"] = "Keep installed software up to date",
                ["SearchHint"] = "Search software",
                ["UpdatesOnly"] = "Updates only",
                ["Refresh"] = "Check now",
                ["RefreshShort"] = "Refresh",
                ["Checking"] = "Scanning installed software and versions…",
                ["ScanningInstalled"] = "Reading installed software…",
                ["ScanningOfficial"] = "Checking official sources…",
                ["ScanningWinget"] = "Matching Winget data…",
                ["Ready"] = "Scan complete",
                ["LastCheckedNever"] = "Not checked yet",
                ["LastChecked"] = "Last checked: {0}",
                ["NoApps"] = "No manageable software found",
                ["NoMatching"] = "No matching software",
                ["AllUpToDate"] = "Everything is up to date",
                ["UpdateAvailableCount"] = "{0} update(s) available",
                ["Current"] = "Current",
                ["Latest"] = "Latest",
                ["LatestUnknown"] = "Unknown",
                ["CurrentUnknown"] = "Unknown",
                ["UpToDate"] = "Up to date",
                ["Available"] = "Update available",
                ["Skipped"] = "Version skipped",
                ["Ignored"] = "Updates ignored",
                ["Unknown"] = "No reliable version source",
                ["Update"] = "Update",
                ["OpenOfficial"] = "Open website",
                ["Ignore"] = "Ignore",
                ["Restore"] = "Restore",
                ["RestoreSkip"] = "Restore",
                ["SourceOfficial"] = "Official",
                ["SourceWinget"] = "Winget",
                ["SourceUnknown"] = "Unknown source",
                ["MenuOpen"] = "Open manager",
                ["MenuCheckNow"] = "Check now",
                ["MenuSettings"] = "Settings",
                ["MenuExit"] = "Exit",
                ["Settings"] = "Settings",
                ["Language"] = "Language",
                ["Chinese"] = "简体中文",
                ["English"] = "English",
                ["Theme"] = "Appearance",
                ["SystemTheme"] = "Use system setting",
                ["LightTheme"] = "Light",
                ["DarkTheme"] = "Dark",
                ["AutoStart"] = "Start silently after Windows sign-in",
                ["CheckManagerUpdate"] = "Check manager update",
                ["Version"] = "Version {0}",
                ["PromptTitle"] = "Software updates found",
                ["PromptSummary"] = "{0} new version(s) found. Select the items to install.",
                ["ConfirmSelected"] = "Install selected",
                ["Cancel"] = "Cancel",
                ["SkipSelected"] = "Skip these",
                ["IgnoreSelected"] = "Ignore selected",
                ["NoSelected"] = "Select at least one item",
                ["Updating"] = "Updating {0}…",
                ["UpdateSucceeded"] = "{0} was updated",
                ["UpdateFailed"] = "Could not update {0}",
                ["UpdateFailedDetail"] = "Could not update {0}: {1}",
                ["RestartRequired"] = "{0} needs a restart to finish updating.",
                ["RestartPrompt"] = "A restart is required to finish updating. Restart now?",
                ["RestartNow"] = "Restart now",
                ["Later"] = "Later",
                ["RunningProcessTitle"] = "Software is running",
                ["RunningProcessMessage"] = "{0} is running. Close it manually, then click Retry. The manager will not close it for you.",
                ["Retry"] = "Retry",
                ["NoUpdateChannel"] = "No direct update channel is available. The official website will be opened.",
                ["UpdateSummaryDone"] = "Update finished: {0} succeeded, {1} failed",
                ["PowerOffline"] = "On battery; the check will resume when AC power is connected",
                ["NetworkOffline"] = "Network unavailable; the check will resume automatically",
                ["DailyAlreadyChecked"] = "Today's automatic check is complete",
                ["NoUpdateChannelShort"] = "No update channel",
                ["WingetMissing"] = "Winget was not found, so direct updates are unavailable",
                ["OfficialSourceError"] = "Could not read the official version",
                ["AppSelfUpdateUnavailable"] = "Could not read manager update information",
                ["AppSelfUpdateAvailable"] = "Manager version {0} is available",
                ["OpenLogs"] = "Open log folder",
                ["Close"] = "Close",
                ["UnknownVersion"] = "Unknown version",
                ["ManagerUpdateLatest"] = "You are using the latest version",
                ["ManagerUpdateOpen"] = "Manager version {0} is available. Download and install it now?",
                ["ManagerUpdateChecking"] = "Checking for manager updates…",
                ["ManagerUpdateDownloading"] = "Downloading the manager update…",
                ["ManagerUpdateInstallFailed"] = "Could not download or start the manager update: {0}"
            }
        };

    public string Language { get; private set; } = "zh-CN";

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string? language)
    {
        var normalized = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";

        if (string.Equals(Language, normalized, StringComparison.Ordinal))
        {
            return;
        }

        Language = normalized;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key] =>
        Strings.TryGetValue(Language, out var table) && table.TryGetValue(key, out var text)
            ? text
            : key;

    public string Format(string key, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.GetCultureInfo(Language), this[key], args);
}
