namespace NvProfileSwitcher;

internal static class BuildInfo
{
    private static readonly Version AppVersion =
        typeof(BuildInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string Version => $"{AppVersion.Major}.{AppVersion.Minor}.{AppVersion.Build}";

    public static string DisplayVersion
    {
        get
        {
            var version = $"v{Version}";
#if NVPS_DEV_BUILD
            return version + " Dev";
#else
            return version;
#endif
        }
    }

    public static bool IsDevelopmentBuild
    {
        get
        {
#if NVPS_DEV_BUILD
            return true;
#else
            return false;
#endif
        }
    }

    public static string AboutVersion
    {
        get
        {
#if NVPS_DEV_BUILD
            return string.IsNullOrWhiteSpace(DevelopmentRevisionInfo.Revision)
                ? $"{Version} Dev"
                : $"{Version} Dev ({DevelopmentRevisionInfo.Revision})";
#else
            return Version;
#endif
        }
    }
}
