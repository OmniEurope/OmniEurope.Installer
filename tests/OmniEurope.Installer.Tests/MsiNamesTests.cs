using OmniEurope.Installer.Building;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class MsiNamesTests
{
    [Fact]
    public void A_valid_short_name_is_kept_alone()
    {
        MsiNames.FileName("APP.EXE", new HashSet<string>()).ShouldBe("APP.EXE");
    }

    [Fact]
    public void A_long_name_gets_a_hashed_short_name_with_its_extension()
    {
        string value = MsiNames.FileName("Microsoft.AspNetCore.Components.dll", new HashSet<string>());

        value.ShouldMatch(@"^[0-9a-f]{8}\.dll\|Microsoft\.AspNetCore\.Components\.dll$");
    }

    [Fact]
    public void Short_names_stay_unique_within_a_folder()
    {
        var taken = new HashSet<string>();
        var names = Enumerable.Range(0, 500).Select(index => MsiNames.FileName($"Some.Long.Library.{index}.dll", taken).Split('|')[0].ToUpperInvariant()).ToList();

        names.Distinct().Count().ShouldBe(500);
    }

    [Fact]
    public void Identifiers_and_guids_are_stable_and_case_insensitive()
    {
        var scope = Guid.Parse("003E3479-DAD8-4570-8DA8-25D967F58BBD");

        MsiNames.Identifier('f', @"wwwroot\app.css").ShouldBe(MsiNames.Identifier('f', @"WWWROOT\App.css"));
        MsiNames.StableGuid(scope, "file:a.dll").ShouldBe(MsiNames.StableGuid(scope, "file:A.DLL"));
        MsiNames.StableGuid(scope, "file:a.dll").ShouldNotBe(MsiNames.StableGuid(scope, "file:b.dll"));
        MsiNames.StableGuid(scope, "file:a.dll").ShouldMatch(@"^\{[0-9A-F]{8}-[0-9A-F]{4}-5[0-9A-F]{3}-[89AB][0-9A-F]{3}-[0-9A-F]{12}\}$");
    }
}
