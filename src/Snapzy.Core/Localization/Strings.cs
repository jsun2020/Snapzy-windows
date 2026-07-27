using System.Globalization;
using System.Resources;

namespace Snapzy.Core.Localization;

public static class Strings
{
    private static readonly ResourceManager Rm =
        new("Snapzy.Core.Localization.Resources.UiStrings", typeof(Strings).Assembly);

    public static string Get(string key) => Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static void SetLanguage(string code)
    {
        var culture = new CultureInfo(code);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
