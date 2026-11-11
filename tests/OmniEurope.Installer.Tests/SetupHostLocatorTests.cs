using OmniEurope.Installer.Setup;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class SetupHostLocatorTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void An_explicit_host_is_returned_as_given_even_when_a_host_is_bundled()
    {
        _temp.WriteFile(SetupHostLocator.BundledFileName, [1]);

        SetupHostLocator.Resolve(@"custom\host.exe", _temp.Path).ShouldBe(@"custom\host.exe");
    }

    [Fact]
    public void An_explicit_host_is_not_checked_here_the_builder_reports_a_missing_file()
    {
        SetupHostLocator.Resolve(_temp.Combine("missing.exe"), _temp.Path).ShouldBe(_temp.Combine("missing.exe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_an_explicit_host_the_bundled_host_next_to_the_tool_is_used(string? host)
    {
        string bundled = _temp.WriteFile(SetupHostLocator.BundledFileName, [1]);

        SetupHostLocator.Resolve(host, _temp.Path).ShouldBe(bundled);
    }

    [Fact]
    public void Without_an_explicit_host_and_without_a_bundled_one_the_error_names_the_option_and_the_folder()
    {
        var error = Should.Throw<InvalidOperationException>(() => SetupHostLocator.Resolve(null, _temp.Path));

        error.Message.ShouldContain("--host");
        error.Message.ShouldContain(SetupHostLocator.BundledFileName);
        error.Message.ShouldContain(_temp.Path);
    }
}
