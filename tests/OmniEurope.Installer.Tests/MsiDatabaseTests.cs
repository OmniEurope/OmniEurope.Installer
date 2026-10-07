using OmniEurope.Installer.Msi;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class MsiDatabaseTests
{
    [Fact]
    public void A_rejected_statement_reports_the_Windows_Installer_error()
    {
        using var temp = new TempFolder();
        using MsiDatabase database = MsiDatabase.Create(temp.Combine("bad.msi"));

        MsiException exception = Should.Throw<MsiException>(() => database.Execute("SELECT FROM WHERE"));

        exception.ErrorCode.ShouldBe(1615u); // ERROR_BAD_QUERY_SYNTAX
        exception.Message.ShouldContain("SELECT FROM WHERE");
        exception.Message.ShouldContain("1615");
    }

    [Fact]
    public void A_stream_written_in_a_row_is_read_back_unchanged()
    {
        using var temp = new TempFolder();
        byte[] content = [1, 2, 3, 250, 0, 7];
        string source = temp.WriteFile("blob.bin", content);
        using MsiDatabase database = MsiDatabase.Create(temp.Combine("streams.msi"));

        database.Execute("CREATE TABLE `Blob` (`Name` CHAR(72) NOT NULL, `Data` OBJECT PRIMARY KEY `Name`)");
        database.Execute(MsiSchema.InsertStatement("Blob", "Name", "Data"), "one", new MsiStream(source));

        database.ReadStream("SELECT `Data` FROM `Blob` WHERE `Name` = ?", "one").ShouldBe(content);
        Should.Throw<InvalidOperationException>(() => database.ReadStream("SELECT `Data` FROM `Blob` WHERE `Name` = ?", "two"));
    }

    [Fact]
    public void A_disposed_database_refuses_further_use()
    {
        using var temp = new TempFolder();
        MsiDatabase database = MsiDatabase.Create(temp.Combine("closed.msi"));
        database.Dispose();

        Should.Throw<ObjectDisposedException>(() => database.Commit());
    }
}
