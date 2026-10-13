using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Models;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Formatters;

[UnitTest]
public class FormatterRegressionTests
{
    private static PlcProject ProjectWithTag(TagInfo tag) => new()
    {
        Vendor = "Test",
        Format = "Test",
        FilePath = "test",
        Tags = new[] { tag }
    };

    [Fact]
    public void Markdown_TagWithPipeAndNewlineInDescription_StaysOneRowWithFiveColumns()
    {
        var tag = new TagInfo { Name = "A|B", DataType = "BOOL", Scope = "Global", Description = "Motor A | B\nline2" };

        var output = new MarkdownOutputFormatter().FormatTags(ProjectWithTag(tag), new TagFilter());

        var row = output.Split('\n').Single(l => l.Contains("Motor A"));
        row.Should().Contain("Motor A \\| B<br>line2");
        // 6 unescaped pipes delimit 5 cells.
        row.Replace("\\|", "").Count(c => c == '|').Should().Be(6);
    }

    [Fact]
    public void Markdown_Diff_IncludesLocationAndValues()
    {
        var result = new DiffResult
        {
            Details = new List<DiffDetail>
            {
                new() { Type = "Modified", Location = "Tags.x.DataType", Message = "Tag 'x' data type changed", Value1 = "DINT", Value2 = "REAL" }
            }
        };

        var output = new MarkdownOutputFormatter().FormatDiff(result);

        output.Should().Contain("`Tags.x.DataType`");
        output.Should().Contain("`DINT` → `REAL`");
    }

    [Fact]
    public void Yaml_StringsThatLookLikeBoolOrNull_RoundTripAsStrings()
    {
        var tag = new TagInfo { Name = "t", DataType = "BOOL", Scope = "Global", Description = "null", InitialValue = "true" };

        var yaml = new YamlOutputFormatter().FormatTags(ProjectWithTag(tag), new TagFilter());

        // Unquoted, a YAML reader would turn these into a real null and a bool.
        yaml.Should().Contain("description: \"null\"");
        yaml.Should().Contain("initialValue: \"true\"");
    }

    [Fact]
    public void Json_NonAsciiAndHtmlCharacters_AreNotEscaped()
    {
        var tag = new TagInfo { Name = "Ölstand", DataType = "REAL", Scope = "Global", Description = "Temp > 50°C & rising" };

        var json = new JsonOutputFormatter().FormatTags(ProjectWithTag(tag), new TagFilter());

        json.Should().Contain("Ölstand").And.Contain("Temp > 50°C & rising");
    }
}
