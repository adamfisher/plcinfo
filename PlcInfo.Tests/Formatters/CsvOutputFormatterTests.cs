using FluentAssertions;
using PlcInfo.Commands;
using PlcInfo.Formatters;
using PlcInfo.Models;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Formatters;

[UnitTest]
public class CsvOutputFormatterTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("-12.5", "-12.5")]
    [InlineData(null, "")]
    public void Escape_FollowsRfc4180AndBlocksFormulas(string? value, string expected)
    {
        CsvOutputFormatter.Escape(value).Should().Be(expected);
    }

    [Fact]
    public void FormatTags_IncludesProgramTagsAndAppliesScopeFilter()
    {
        var project = new PlcProject
        {
            Tags = new[] { new TagInfo { Name = "Global", DataType = "BOOL", Scope = "Controller" } },
            Programs = new[]
            {
                new ProgramInfo { Name = "Main", LocalTags = new[] { new TagInfo { Name = "Local", DataType = "DINT", Scope = "Main", Description = "Speed, rpm" } } }
            }
        };
        var csv = new CsvOutputFormatter();

        var all = csv.FormatTags(project).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var mainOnly = csv.FormatTags(project, new TagFilter { Scope = "main" }).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        all.Should().Equal(
            "Scope,Name,DataType,Dimensions,Usage,Address,AliasFor,IsIo,Description",
            "Controller,Global,BOOL,,,,,false,",
            "Main,Local,DINT,,,,,false,\"Speed, rpm\"");
        mainOnly.Should().HaveCount(2).And.Contain("Main,Local,DINT,,,,,false,\"Speed, rpm\"");
    }

    [Fact]
    public void TextFormatter_TagsIncludeProgramTags()
    {
        var project = new PlcProject
        {
            Programs = new[] { new ProgramInfo { Name = "Main", LocalTags = new[] { new TagInfo { Name = "Local", DataType = "DINT", Scope = "Main" } } } }
        };

        new TextOutputFormatter().FormatTags(project).Should().Contain("Local");
    }

    [Fact]
    public void FormatDiff_OneRowPerDetail()
    {
        var result = new DiffResult
        {
            Details = new List<DiffDetail>
            {
                new() { Type = "Modified", Category = "Logic", Location = "Programs.P.Routines.R.Rungs[2]", Message = "Rung 2 changed", Value1 = "XIC(a)OTE(b);", Value2 = "XIC(a)OTL(b);" }
            }
        };

        new CsvOutputFormatter().FormatDiff(result).Split("\r\n")[1]
            .Should().Be("Modified,Logic,Programs.P.Routines.R.Rungs[2],Rung 2 changed,XIC(a)OTE(b);,XIC(a)OTL(b);");
    }

    [Fact]
    public void NonTabularCommand_IsUsageError()
    {
        var act = () => new CsvOutputFormatter().FormatSummary(new PlcProject());

        act.Should().Throw<UsageException>().WithMessage("summary: csv output is only available*");
    }
}
