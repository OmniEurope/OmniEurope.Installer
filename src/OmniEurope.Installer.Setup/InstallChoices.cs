using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// The choices made in the wizard (or taken from the previous installation), turned into the
/// Windows Installer command line. A check box sets its property to "1" when checked and to an
/// empty string otherwise: an MSI condition tests whether the property is set, so "0" would be true.
/// </summary>
internal sealed class InstallChoices
{
    // Value names the product's MSI writes under SetupConfig.SettingsKey (see products/*/product.json).
    public const string LanguageSetting = "Language";
    public const string InstallFolderSetting = "InstallFolder";

    public InstallChoices(string installFolder, string language)
    {
        InstallFolder = installFolder;
        Language = language;
    }

    public string InstallFolder { get; set; }

    /// <summary>"fr" or "en".</summary>
    public string Language { get; set; }

    public IDictionary<string, bool> Options { get; } = new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>The defaults of a first installation.</summary>
    public static InstallChoices Defaults(SetupConfig config, string programFiles64)
    {
        var choices = new InstallChoices(Path.Combine(programFiles64, Path.Combine(config.InstallDirectory)), "fr");
        foreach (SetupOption option in config.Options)
        {
            choices.Options[option.Property] = option.DefaultChecked;
        }

        return choices;
    }

    /// <summary>Overrides the defaults with what the previous installation recorded; missing values are kept.</summary>
    public void ApplySettings(Func<string, string?> readSetting, SetupConfig config)
    {
        string? language = readSetting(LanguageSetting);
        if (language == "fr" || language == "en")
        {
            Language = language;
        }

        string? folder = readSetting(InstallFolderSetting);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            InstallFolder = folder!.TrimEnd('\\');
        }

        foreach (SetupOption option in config.Options)
        {
            string? value = readSetting(option.SettingName);
            if (value is not null)
            {
                Options[option.Property] = value == "1";
            }
        }
    }

    /// <summary>The property assignments passed to MsiInstallProduct, with <paramref name="overrides"/> last.</summary>
    public string ToCommandLine(SetupConfig config, IDictionary<string, string> overrides)
    {
        var properties = new List<KeyValuePair<string, string>>
        {
            // No trailing backslash: Windows Installer adds it, and \" could read as an escaped quote.
            new("INSTALLFOLDER", InstallFolder.TrimEnd('\\')),
        };
        if (config.LanguageProperty.Length > 0)
        {
            properties.Add(new(config.LanguageProperty, Language));
        }

        foreach (KeyValuePair<string, bool> option in Options)
        {
            properties.Add(new(option.Key, option.Value ? "1" : string.Empty));
        }

        properties.AddRange(overrides);
        var commandLine = new StringBuilder();
        foreach (KeyValuePair<string, string> property in properties)
        {
            commandLine.Append(property.Key).Append("=\"").Append(property.Value.Replace("\"", "\"\"")).Append("\" ");
        }

        return commandLine.ToString().TrimEnd();
    }
}
