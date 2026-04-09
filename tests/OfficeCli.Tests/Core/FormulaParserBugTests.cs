using System;
using DocumentFormat.OpenXml;
using OfficeCli.Core;
using Xunit;

namespace OfficeCli.Tests.Core;

public class FormulaParserBugTests
{
    [Fact]
    public void Parse_NullInput_ThrowsWrappedFormulaParseException()
    {
        var ex = Assert.Throws<FormulaParseException>(() => FormulaParser.Parse(null));
        Assert.Contains("Failed to parse formula:", ex.Message);
        Assert.IsType<NullReferenceException>(ex.InnerException);
    }

    [Fact]
    public void ToLatex_NullInput_ThrowsNullReferenceException_Bug()
    {
        Assert.Throws<NullReferenceException>(() => FormulaParser.ToLatex(null));
    }
}
