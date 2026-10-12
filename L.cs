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

    public static bool De => Lang == "de";

    /// <summary>"pl", "en", "de" albo "" (automatycznie — wg języka Windows; inne języki → angielski).</summary>
    public static void Set(string? lang)
    {
        if (string.IsNullOrEmpty(lang))
            lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch { "pl" => "pl", "de" => "de", _ => "en" };
        Lang = lang is "en" or "de" ? lang : "pl";
    }

    public static string T(string pl) =>
        En && Translations.En.TryGetValue(pl, out var e) ? e
        : De && Translations.De.TryGetValue(pl, out var d) ? d
        : pl;

    public static string T(string pl, params object?[] args) => string.Format(T(pl), args);
}
