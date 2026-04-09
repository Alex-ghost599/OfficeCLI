using FluentAssertions;
using OfficeCli;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class WordSchemaRegressionTests : IDisposable
{
    private readonly string _docxPath;
    private WordHandler _handler;

    public WordSchemaRegressionTests()
    {
        _docxPath = Path.Combine(Path.GetTempPath(), $"wordschema_{Guid.NewGuid():N}.docx");
        BlankDocCreator.Create(_docxPath);
        _handler = new WordHandler(_docxPath, editable: true);
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (File.Exists(_docxPath))
            File.Delete(_docxPath);
    }

    [Fact]
    public void Word_DocSettings_MixedWrites_ProduceSchemaValidSettings()
    {
        _handler.Set("/", new()
        {
            ["evenAndOddHeaders"] = "true",
            ["displayBackgroundShape"] = "true",
            ["charSpacingControl"] = "compressPunctuation",
            ["bookFoldPrinting"] = "true",
            ["bookFoldPrintingSheets"] = "2",
            ["defaultTabStop"] = "720"
        });

        var errors = _handler.Validate();
        errors.Should().BeEmpty("mixed settings writes should preserve a schema-valid w:settings part");
    }

    [Fact]
    public void Word_Footer_RunFormatting_MaintainsSchemaOrder()
    {
        _handler.Add("/", "footer", null, new() { ["text"] = "Footer text" });

        _handler.Set("/footer[1]", new()
        {
            ["font"] = "Arial",
            ["size"] = "11",
            ["color"] = "FF0000",
            ["highlight"] = "yellow",
            ["underline"] = "single",
            ["bold"] = "true"
        });

        var errors = _handler.Validate();
        errors.Should().BeEmpty("footer run formatting should keep rPr children in schema order");
    }

    [Fact]
    public void Word_Footer_ParagraphMarkRunProperties_MaintainsSchemaOrder()
    {
        _handler.Add("/", "footer", null, new());

        _handler.Set("/footer[1]", new()
        {
            ["font"] = "Arial",
            ["size"] = "11",
            ["color"] = "0044CC",
            ["highlight"] = "yellow",
            ["underline"] = "single",
            ["bold"] = "true"
        });

        var errors = _handler.Validate();
        errors.Should().BeEmpty("footer paragraph mark run properties should stay schema-valid after formatting writes");
    }
}
