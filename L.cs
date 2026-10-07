using System.Globalization;

namespace TS6Overlay;

/// <summary>
/// Tłumaczenia interfejsu. Kluczem jest polski tekst; brak tłumaczenia = tekst polski.
/// Teksty z parametrami: T("{0} dołączył(a) do kanału", nick).
/// </summary>
public static class L
{
    public static string Lang { get; private set; } = "pl";
    public static bool En => Lang == "en";

    /// <summary>"pl", "en" albo "" (automatycznie — wg języka Windows).</summary>
    public static void Set(string? lang)
    {
        if (string.IsNullOrEmpty(lang))
            lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl" ? "pl" : "en";
        Lang = lang == "en" ? "en" : "pl";
    }

    public static string T(string pl) => En && Translations.En.TryGetValue(pl, out var e) ? e : pl;

    public static string T(string pl, params object?[] args) => string.Format(T(pl), args);
}
