using OmniEurope.Installer.Building;
using OmniEurope.Installer.Msi;
using OmniEurope.Installer.Product;
using OmniEurope.Installer.Setup;

const string Usage = """
    Usage:
      oe-installer msi   --definition <product.json> --source <published folder> --version <x.y.z> --output <file.msi>
      oe-installer setup --definition <product.json> --msi <file.msi> --host <OmniEurope.Installer.Setup.exe>
                         --icon <product executable> [--license <license.rtf>] --output <Setup.exe>
    """;

string[] commands = ["msi", "setup"];
if (args.Length == 0 || !commands.Contains(args[0]))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int index = 1; index < args.Length; index += 2)
{
    if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    options[args[index][2..]] = args[index + 1];
}

string[] required = args[0] == "msi" ? ["definition", "source", "version", "output"] : ["definition", "msi", "host", "icon", "output"];
string[] missing = required.Where(name => !options.ContainsKey(name)).ToArray();
if (missing.Length > 0)
{
    Console.Error.WriteLine($"Missing option(s): {string.Join(", ", missing.Select(name => "--" + name))}");
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    ProductDefinition definition = ProductDefinition.Load(options["definition"]);
    if (args[0] == "msi")
    {
        MsiBuildResult result = MsiPackageBuilder.Build(definition, new MsiBuildRequest(options["source"], options["version"], options["output"]));
        Console.WriteLine($"MSI written: {result.OutputPath} ({result.FileCount} files, ProductCode {result.ProductCode:B})");
    }
    else
    {
        var request = new SetupBuildRequest(options["host"], options["msi"], options.GetValueOrDefault("license"), options["icon"], options["output"]);
        SetupPackageBuilder.Build(definition, request);
        Console.WriteLine($"Setup written: {Path.GetFullPath(request.OutputPath)}");
    }

    return 0;
}
catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or MsiException or InvalidOperationException)
{
    Console.Error.WriteLine($"ERROR: {exception.Message}");
    return 1;
}
