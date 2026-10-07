using System;
using System.Collections.Generic;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Command line of the setup program:
/// <c>[/quiet] [/uninstall] [/log path] [/render-pages folder] [PROPERTY=value ...]</c>.
/// /quiet installs (or uninstalls) without any window; PROPERTY=value overrides a wizard choice.
/// /render-pages writes every wizard page to a PNG in the folder and exits (layout check).
/// </summary>
internal sealed class SetupCommandLine
{
    private SetupCommandLine()
    {
    }

    public bool Quiet { get; private set; }

    public bool Uninstall { get; private set; }

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
            throw new ArgumentException($"Unknown argument '{argument}'. Expected /quiet, /uninstall, /log <path> or PROPERTY=value.");
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
