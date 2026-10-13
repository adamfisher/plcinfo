using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Models;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Formatters;

[UnitTest]
public class CanonicalTextRendererTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rockwell", "ACDTestsWithAOI.L5X");

    [Fact]
    public async Task Render_IsDeterministicAndOmitsVolatileData()
    {
        var project = await new RockwellL5XParser().ParseAsync(Fixture);

        var first = CanonicalTextRenderer.Render(project);
        var second = CanonicalTextRenderer.Render(project with { FilePath = "elsewhere.L5X", LastModified = DateTime.UnixEpoch });

        second.Should().Be(first);
        first.Should().NotContain(Fixture).And.NotContain("ExportDate");
        first.Should().StartWith("# plcinfo textconv v1: Rockwell Automation L5X\n");
    }

    [Fact]
    public async Task Render_ContainsLogicOneItemPerLine()
    {
        var text = CanonicalTextRenderer.Render(await new RockwellL5XParser().ParseAsync(Fixture));
        var lines = text.Split('\n');

        lines.Should().Contain("MainProgram/MainRoutine rung // Im a Rung Comment");
        lines.Should().Contain("MainProgram/MainRoutine rung: XIC(DINT.2)OTE(DINT.3);");
        lines.Should().Contain("MainProgram/STRoutine 2| DINT := DINT AND ULINT; // Line 2 Comment");
        lines.Should().Contain("datatype UDT_Test.TestULINT: ULINT  // Test ULINT Description UDT");
        lines.Should().Contain(l => l.StartsWith("task MainTask: type=CONTINUOUS priority=10"));
    }

    [Fact]
    public void Render_InsertedRungWithRenumbering_ChangesExactlyOneLine()
    {
        // Logix renumbers later rungs on insert; that must not touch the other lines.
        PlcProject Ladder(params string[] texts) => new()
        {
            Vendor = "Rockwell Automation",
            Format = "L5X",
            Programs = new[]
            {
                new ProgramInfo
                {
                    Name = "P",
                    Routines = new[]
                    {
                        new RoutineInfo
                        {
                            Name = "R",
                            Language = "RLL",
                            Rungs = texts.Select((t, i) => new RungInfo { Number = i, Text = t, EditState = "N" }).ToList()
                        }
                    }
                }
            }
        };

        var before = CanonicalTextRenderer.Render(Ladder("XIC(a)OTE(b);", "XIC(c)OTE(d);")).Split('\n');
        var after = CanonicalTextRenderer.Render(Ladder("XIC(a)OTE(b);", "XIC(new)OTE(x);", "XIC(c)OTE(d);")).Split('\n');

        after.Except(before).Should().Equal("P/R rung: XIC(new)OTE(x);");
        before.Except(after).Should().BeEmpty();
    }
}
