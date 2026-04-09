using FluentAssertions;
using OfficeCli;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public class WordRawSetAliasTests : IDisposable
{
    private readonly string _path;
    private WordHandler _handler;

    public WordRawSetAliasTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"word_raw_alias_{Guid.NewGuid():N}.docx");
        BlankDocCreator.Create(_path);
        _handler = new WordHandler(_path, editable: true);
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Word_RawSet_DocumentAlias_AppliesMutation()
    {
        _handler.Add("/body", "paragraph", null, new() { ["text"] = "Alias" });
        _handler.RawSet("document", "//w:t[1]", "replace", "<w:t>Alias Updated</w:t>");

        var raw = _handler.Raw("/document");
        raw.Should().Contain("Alias Updated");
    }

    [Fact]
    public void Word_RawSet_StylesAlias_AppliesMutation()
    {
        _handler.RawSet("styles", "//w:docDefaults/w:pPrDefault/w:pPr/w:autoSpaceDE", "setattr", "w:val=true");

        var raw = _handler.Raw("/styles");
        raw.Should().Contain("autoSpaceDE");
        raw.Should().Contain("w:val=\"true\"");
    }
}
