//
//  Loc.cs
//  Citadel: local AI chat for Windows.
//
//  Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
//  Date: November 9, 2026
//  SPDX-License-Identifier: GPL-2.0-only
//
//  This program is free software: you can redistribute it and/or modify it under the terms of
//  the GNU General Public License, version 2, as published by the Free Software Foundation.
//  It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.
//

namespace Citadel;

public enum UiLanguage { En, Es, Ja }

/// <summary>
/// Interface language: English, Spanish or Japanese. On the first start it is the one chosen in the setup program;
/// without one, the language of Windows when it is Spanish or Japanese, otherwise English. After that, the choice
/// is stored in settings.json.
/// Every visible text goes through <see cref="tr(string, string, string)"/>, which receives the three versions side
/// by side as literals (Spanish, English, Japanese); texts with values use numbered markers ({0}, {1}) and the
/// overload with arguments. The self-test reads every call from the compiled program and checks that the three
/// versions are there and carry the same markers.
/// </summary>
internal static class Loc
{
    private static UiLanguage current = UiLanguage.En;

    /// <summary>Raised after the language changes, so open windows can rebuild their texts.</summary>
    public static event Action Changed;

    public static UiLanguage Current
    {
        get => current;
        set
        {
            if (current == value) return;
            current = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Every interface language, in the order of the menus.</summary>
    public static readonly UiLanguage[] All = [UiLanguage.En, UiLanguage.Es, UiLanguage.Ja];

    /// <summary>The label of the language menus, in the three languages so it can be found from any of them.</summary>
    public const string MenuLabel = "Idioma / Language / 言語";

    /// <summary>Configuration code of a language ("en", "es" or "ja").</summary>
    public static string Code(UiLanguage l) => l switch { UiLanguage.Es => "es", UiLanguage.Ja => "ja", _ => "en" };

    /// <summary>The name of a language in that language, for the menus.</summary>
    public static string NativeName(UiLanguage l) => l switch { UiLanguage.Es => "Español", UiLanguage.Ja => "日本語", _ => "English" };

    /// <summary>Culture of the current language, for dates and for the text layout of the windows.</summary>
    public static string CultureName => current switch { UiLanguage.Es => "es-ES", UiLanguage.Ja => "ja-JP", _ => "en-US" };

    /// <summary>Language for a configuration code; anything unknown means English.</summary>
    public static UiLanguage FromCode(string code) => (code ?? "").Trim().ToLowerInvariant() switch
    {
        "es" => UiLanguage.Es,
        "ja" => UiLanguage.Ja,
        _ => UiLanguage.En,
    };

    /// <summary>The language of the Windows interface when Citadel has it, otherwise English.</summary>
    public static UiLanguage FromWindows() => FromCode(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>Returns the text for the current interface language.</summary>
    public static string tr(string es, string en, string ja) => current switch
    {
        UiLanguage.Es => es,
        UiLanguage.Ja => ja,
        _ => en,
    };

    /// <summary>Returns the text for the current interface language with its markers ({0}, {1}...) filled in.</summary>
    public static string tr(string es, string en, string ja, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, tr(es, en, ja), args);
}
