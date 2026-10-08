using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Command line of the setup program:
/// <c>[/quiet] [/uninstall] [/peruser | /permachine] [/lang fr|en] [/log path] [/render-pages folder] [PROPERTY=value ...]</c>.
/// /quiet installs (or uninstalls) without any window; /peruser and /permachine choose who a first installation is
/// for; /lang preselects the wizard language; PROPERTY=value overrides a wizard choice.
/// /render-pages writes every wizard page to a PNG in the folder and exits (layout check).
/// </summary>
internal sealed class SetupCommandLine
{
    private const string PerUserSwitch = "/peruser";
    private const string PerMachineSwitch = "/permachine";
    private const string LanguageSwitch = "/lang";

    private SetupCommandLine()
    {
    }

    public bool Quiet { get; private set; }

    public bool Uninstall { get; private set; }

    /// <summary>The context asked on the command line; null when the wizard asks (or /quiet installs per machine).</summary>
    public InstallScope? Scope { get; private set; }

    /// <summary>"fr" or "en" when given; the wizard then starts past its language page.</summary>
    public string? Language { get; private set; }

    public string? LogPath { get; private set; }

    public string? RenderPagesFolder { get; private set; }

    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <exception cref="ArgumentException">An argument is unknown or misses its value.</exception>
    public static SetupCommandLine Parse(IReadOnlyList<string> arguments)
    {
        var result = new SetupCommandLine();
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            switch (argument.ToLowerInvariant())
            {
                case "/quiet":
                case "/q":
                    result.Quiet = true;
                    break;
                case "/uninstall":
                case "/x":
                    result.Uninstall = true;
                    break;
                case PerUserSwitch:
                    result.Scope = InstallScope.PerUser;
                    break;
                case PerMachineSwitch:
                    result.Scope = InstallScope.PerMachine;
                    break;
                case LanguageSwitch:
                    result.Language = LanguageAfter(arguments, ref index, argument);
                    break;
                case "/log":
                case "/l":
                    result.LogPath = ValueAfter(arguments, ref index, argument);
                    break;
                case "/render-pages":
                    result.RenderPagesFolder = ValueAfter(arguments, ref index, argument);
                    break;
                default:
                    AddProperty(result, argument);
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// The arguments of the same run started again with administrator rights: the context becomes per machine and,
    /// when the wizard already asked it, the language is carried over so that the wizard does not ask again.
    /// </summary>
    public static List<string> ForElevation(IReadOnlyList<string> arguments, string? language)
    {
        var result = new List<string>();
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index].ToLowerInvariant();
            if (argument == LanguageSwitch)
            {
                index++;
            }
            else if (argument is not (PerUserSwitch or PerMachineSwitch))
            {
                result.Add(arguments[index]);
            }
        }

        result.Add(PerMachineSwitch);
        if (language is not null)
        {
            result.Add(LanguageSwitch);
            result.Add(language);
        }

        return result;
    }

    /// <summary>Joins arguments into one command line that splits back into the same arguments (CommandLineToArgvW rules).</summary>
    public static string Format(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes are literal unless they precede a quote: then each one is doubled, and the quote escaped.
            quoted.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes).Append(character);
            backslashes = 0;
        }

        // Before the closing quote, the trailing backslashes are doubled too.
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static string LanguageAfter(IReadOnlyList<string> arguments, ref int index, string option)
    {
        string language = ValueAfter(arguments, ref index, option).ToLowerInvariant();
        return language is "fr" or "en" ? language : throw new ArgumentException($"{option} expects fr or en.");
    }

    private static string ValueAfter(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (index + 1 >= arguments.Count)
        {
            throw new ArgumentException($"{option} needs a value.");
        }

        index++;
        return arguments[index];
    }

    private static void AddProperty(SetupCommandLine result, string argument)
    {
        int equals = argument.IndexOf('=');
        if (equals <= 0 || !IsPublicProperty(argument.Substring(0, equals)))
        {
            throw new ArgumentException($"Unknown argument '{argument}'. Expected /quiet, /uninstall, /peruser, /permachine, /lang fr|en, /log <path> or PROPERTY=value.");
        }

        result.Properties[argument.Substring(0, equals)] = argument.Substring(equals + 1);
    }

    private static bool IsPublicProperty(string name)
    {
        foreach (char character in name)
        {
            bool valid = (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9') || character == '_' || character == '.';
            if (!valid)
            {
                return false;
            }
        }

        return name.Length > 0 && !(name[0] >= '0' && name[0] <= '9');
    }
}
