using FluentAssertions;
using OfficeCli.Handlers;
using Xunit;

namespace OfficeCli.Tests.Functional;

public sealed class ResidentCliLifecycleTests
{
    [Fact]
    public async Task ResidentCliLifecycle_Docx_OpenReuseClose_Works()
    {
        using var harness = new CliSubprocessHarness();
        var path = harness.CreateTempFile(".docx");
        CreateResidentDocx(path);

        await AssertResidentLifecycleAsync(harness, path);
    }

    [Fact]
    public async Task ResidentCliLifecycle_Xlsx_OpenReuseClose_Works()
    {
        using var harness = new CliSubprocessHarness();
        var path = harness.CreateTempFile(".xlsx");
        CreateResidentXlsx(path);

        await AssertResidentLifecycleAsync(harness, path);
    }

    [Fact]
    public async Task ResidentCliLifecycle_Pptx_OpenReuseClose_Works()
    {
        using var harness = new CliSubprocessHarness();
        var path = harness.CreateTempFile(".pptx");
        CreateResidentPptx(path);

        await AssertResidentLifecycleAsync(harness, path);
    }

    private static async Task AssertResidentLifecycleAsync(CliSubprocessHarness harness, string path)
    {
        var cli = harness.CliExecutablePath;
        var shell = $"""
            set +e
            "{cli}" open "{path}"
            echo "__RC_OPEN:$?"
            "{cli}" open "{path}"
            echo "__RC_OPEN2:$?"
            "{cli}" close "{path}"
            echo "__RC_CLOSE:$?"
            "{cli}" close "{path}"
            echo "__RC_CLOSE2:$?"
            exit 0
            """;

        var result = await harness.RunShellAsync(TimeSpan.FromSeconds(45), shell);
        result.TimedOut.Should().BeFalse();
        result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
        result.Stdout.Should().Contain("Opened");
        result.Stdout.Should().Contain("already running");
        result.Stdout.Should().Contain("Resident closed");
        result.Stdout.Should().Contain("__RC_OPEN:0");
        result.Stdout.Should().Contain("__RC_OPEN2:0");
        result.Stdout.Should().Contain("__RC_CLOSE:0");
        result.Stdout.Should().Contain("__RC_CLOSE2:1");
        result.Stderr.Should().Contain("No resident running");
    }

    private static void CreateResidentDocx(string path)
    {
        BlankDocCreator.Create(path);
        using var word = new WordHandler(path, editable: true);
        word.Add("/body", "paragraph", null, new() { ["text"] = "Resident integration test", ["style"] = "Heading 1" });
        word.Add("/body", "paragraph", null, new() { ["text"] = "First list item", ["liststyle"] = "bullet" });
        word.Add("/body", "paragraph", null, new() { ["text"] = "Second list item", ["liststyle"] = "bullet" });
        word.Add("/body", "table", null, new() { ["rows"] = "2", ["cols"] = "2", ["border.all"] = "single;4;4472C4" });
        word.Add("/body", "chart", null, new()
        {
            ["chartType"] = "column",
            ["title"] = "Resident Chart",
            ["data"] = "Series 1:10,20",
            ["categories"] = "Q1,Q2"
        });
    }

    private static void CreateResidentXlsx(string path)
    {
        BlankDocCreator.Create(path);
        using var excel = new ExcelHandler(path, editable: true);
        excel.Add("/", "sheet", null, new() { ["name"] = "Data" });
        excel.Add("/Data", "cell", null, new() { ["ref"] = "A1", ["value"] = "Month", ["type"] = "string" });
        excel.Add("/Data", "cell", null, new() { ["ref"] = "B1", ["value"] = "Revenue", ["type"] = "string" });
        excel.Add("/Data", "cell", null, new() { ["ref"] = "A2", ["value"] = "Jan", ["type"] = "string" });
        excel.Add("/Data", "cell", null, new() { ["ref"] = "B2", ["value"] = "10" });
        excel.Add("/Data", "cell", null, new() { ["ref"] = "A3", ["value"] = "Feb", ["type"] = "string" });
        excel.Add("/Data", "cell", null, new() { ["ref"] = "B3", ["value"] = "20" });
        excel.Add("/Data", "cell", null, new() { ["ref"] = "C2", ["formula"] = "B2*2" });
        excel.Add("/Data", "chart", null, new()
        {
            ["chartType"] = "line",
            ["title"] = "Revenue",
            ["data"] = "Series 1:10,20",
            ["categories"] = "Jan,Feb"
        });
    }

    private static void CreateResidentPptx(string path)
    {
        BlankDocCreator.Create(path);
        using var ppt = new PowerPointHandler(path, editable: true);
        ppt.Add("/", "slide", null, new() { ["title"] = "Resident Deck" });
        ppt.Add("/slide[1]", "shape", null, new()
        {
            ["text"] = "Summary",
            ["x"] = "1cm",
            ["y"] = "1cm",
            ["width"] = "6cm",
            ["height"] = "2cm",
            ["fill"] = "4472C4"
        });
        var customShape = ppt.Add("/slide[1]", "shape", null, new()
        {
            ["geometry"] = "M 0,0 L 100,0 L 100,100 L 0,100 Z",
            ["x"] = "9cm",
            ["y"] = "2cm",
            ["width"] = "3cm",
            ["height"] = "3cm",
            ["fill"] = "ED7D31"
        });
        ppt.Set(customShape, new() { ["line"] = "000000", ["rotation"] = "15" });
        ppt.Add("/slide[1]", "table", null, new() { ["rows"] = "2", ["cols"] = "2" });
        ppt.Add("/slide[1]", "chart", null, new()
        {
            ["chartType"] = "pie",
            ["title"] = "Mix",
            ["data"] = "Series 1:60,40",
            ["categories"] = "A,B"
        });
    }
}
