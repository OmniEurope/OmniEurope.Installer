using OmniEurope.Installer.Msi;

namespace OmniEurope.Installer.Building;

/// <summary>
/// The standard action sequences, limited to the actions whose tables this package writes. Sequence
/// numbers are the ones documented by Windows Installer for each standard action.
/// </summary>
internal static class StandardSequences
{
    private static readonly (string Action, int Sequence)[] InstallExecute =
    [
        ("FindRelatedProducts", 25), ("LaunchConditions", 100), ("ValidateProductID", 700),
        ("CostInitialize", 800), ("FileCost", 900), ("CostFinalize", 1000), ("MigrateFeatureStates", 1200),
        ("InstallValidate", 1400), ("RemoveExistingProducts", 1401), ("InstallInitialize", 1500),
        ("ProcessComponents", 1600), ("UnpublishFeatures", 1800), ("RemoveRegistryValues", 2600),
        ("RemoveShortcuts", 3200), ("RemoveFiles", 3500), ("InstallFiles", 4000), ("CreateShortcuts", 4500),
        ("WriteRegistryValues", 5000), ("RegisterUser", 6000), ("RegisterProduct", 6100),
        ("PublishFeatures", 6300), ("PublishProduct", 6400), ("InstallFinalize", 6600),
    ];

    private static readonly (string Action, int Sequence)[] InstallUi =
    [
        ("FindRelatedProducts", 25), ("LaunchConditions", 100), ("ValidateProductID", 700),
        ("CostInitialize", 800), ("FileCost", 900), ("CostFinalize", 1000), ("MigrateFeatureStates", 1200),
        ("ExecuteAction", 1300),
    ];

    private static readonly (string Action, int Sequence)[] AdminExecute =
    [
        ("CostInitialize", 800), ("FileCost", 900), ("CostFinalize", 1000), ("InstallValidate", 1400),
        ("InstallInitialize", 1500), ("InstallAdminPackage", 3900), ("InstallFiles", 4000), ("InstallFinalize", 6600),
    ];

    private static readonly (string Action, int Sequence)[] AdminUi =
    [
        ("CostInitialize", 800), ("FileCost", 900), ("CostFinalize", 1000), ("ExecuteAction", 1300),
    ];

    private static readonly (string Action, int Sequence)[] AdvertiseExecute =
    [
        ("CostInitialize", 800), ("CostFinalize", 1000), ("InstallValidate", 1400), ("InstallInitialize", 1500),
        ("CreateShortcuts", 4500), ("RegisterProduct", 6100), ("PublishFeatures", 6300), ("PublishProduct", 6400),
        ("InstallFinalize", 6600),
    ];

    public static void Write(MsiDatabase database)
    {
        Write(database, "InstallExecuteSequence", InstallExecute);
        Write(database, "InstallUISequence", InstallUi);
        Write(database, "AdminExecuteSequence", AdminExecute);
        Write(database, "AdminUISequence", AdminUi);
        Write(database, "AdvtExecuteSequence", AdvertiseExecute);
    }

    private static void Write(MsiDatabase database, string table, (string Action, int Sequence)[] actions)
    {
        using MsiStatement insert = database.Prepare(MsiSchema.InsertStatement(table, "Action", "Condition", "Sequence"));
        foreach ((string action, int sequence) in actions)
        {
            insert.Execute(action, null, sequence);
        }
    }
}
