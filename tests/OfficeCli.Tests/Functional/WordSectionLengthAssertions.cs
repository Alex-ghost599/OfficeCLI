using FluentAssertions;
using System.Globalization;

namespace OfficeCli.Tests.Functional;

internal static class WordSectionLengthAssertions
{
    internal static decimal ParseCm(object value)
    {
        value.Should().NotBeNull();

        var text = value!.ToString();
        text.Should().NotBeNullOrWhiteSpace();
        text!.Should().EndWith("cm", because: "Word section readback now uses friendly centimeter strings");

        return decimal.Parse(text[..^2], CultureInfo.InvariantCulture);
    }
}
