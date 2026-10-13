using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Parsing;
using PlcInfo.Rendering;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Rendering;

[UnitTest]
public class LadderRenderingTests
{
    [Fact]
    public void Parse_SeriesBranchesAndBoxes()
    {
        var rung = LadderParser.TryParse("XIC(a)[XIO(b) ,XIC(c) MOV(1,d) ]OTE(e);")!;

        rung.Items.Should().HaveCount(3);
        ((LadderInstruction)rung.Items[0]).Key.Should().Be("XIC(a)");
        var branch = rung.Items[1].Should().BeOfType<LadderBranch>().Subject;
        branch.Legs.Should().HaveCount(2);
        branch.Legs[1].Items.Select(i => ((LadderInstruction)i).Mnemonic).Should().Equal("XIC", "MOV");
        ((LadderInstruction)rung.Items[2]).Key.Should().Be("OTE(e)");
    }

    [Fact]
    public void Parse_EmptyLegAndNestedBranch()
    {
        var rung = LadderParser.TryParse("XIC(a)[,XIC(b)[XIC(c),XIO(d)]]OTE(e);")!;

        var outer = rung.Items[1].Should().BeOfType<LadderBranch>().Subject;
        outer.Legs[0].Items.Should().BeEmpty();
        outer.Legs[1].Items[1].Should().BeOfType<LadderBranch>();
    }

    [Fact]
    public void Parse_PlcOpenCanonicalText()
    {
        LadderParser.TryParse("XIC(a) [XIC(b) , XIO(c)] OTE(d)").Should().NotBeNull();
    }

    [Theory]
    [InlineData("Coil(#Run) := #Start AND NOT #Stop")]
    [InlineData("IF a THEN b := 1; END_IF;")]
    [InlineData("XIC(a")]
    [InlineData("[XIC(a)")]
    [InlineData("")]
    public void Parse_NonLadderText_ReturnsNull(string text)
    {
        LadderParser.TryParse(text).Should().BeNull();
    }

    [Fact]
    public void Render_DrawsSymbolsAndEncodesText()
    {
        var svg = LadderSvgRenderer.Render(LadderParser.TryParse("XIC(a)XIO(b)OTL(c)TON(\"<x>&y\",1,0);")!);

        svg.Should().StartWith("<svg").And.EndWith("</svg>");
        svg.Should().Contain("<path class=\"lad-sym\"");           // coil arcs
        svg.Should().Contain(">L</text>");                         // latch mark
        svg.Should().Contain("<rect class=\"lad-box\"");          // TON box
        svg.Should().Contain("&lt;x&gt;&amp;y").And.NotContain("<x>");
    }

    [Fact]
    public void Render_LongLabelsKeepTheirEnd()
    {
        var svg = LadderSvgRenderer.Render(LadderParser.TryParse("XIC(TimerCounter.TON_Run)OTE(x);")!);

        svg.Should().Contain(">…ounter.TON_Run</text>");
        svg.Should().Contain("<title>XIC(TimerCounter.TON_Run)</title>");
    }

    [Fact]
    public void Render_HighlightClassIsApplied()
    {
        var svg = LadderSvgRenderer.Render(LadderParser.TryParse("XIC(a)OTE(b);")!, i => i.Mnemonic == "OTE" ? "lad-add" : null);

        svg.Should().Contain("class=\"lad-instr lad-add\"><title>OTE(b)</title>");
    }

    [Fact]
    public async Task Report_RealProject_IsSelfContainedAndRendersRungs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rockwell", "ExampleForCICD_L85E.L5X");
        var project = await new RockwellL5XParser().ParseAsync(path);

        var html = HtmlReportRenderer.RenderProject(project, new LintService().Analyze(project));

        html.Should().StartWith("<!doctype html>");
        // Self-contained: no scripts and nothing loaded from elsewhere (the SVG xmlns URI is only a name).
        html.Should().NotContain("<script");
        html.Should().NotMatchRegex(@"(src|href)\s*=\s*[""']?https?:|url\(\s*[""']?https?:");
        html.Split("<svg class=\"ladder\"").Length.Should().Be(1 + 13);   // one drawing per rung
        html.Should().Contain("ExampleWithCICD_L85E");
    }

    [Fact]
    public void Report_EncodesUntrustedText()
    {
        var project = new PlcProject
        {
            Vendor = "<b>v</b>",
            Controller = new ControllerInfo { Name = "<script>alert(1)</script>" },
            Programs = new[]
            {
                new ProgramInfo
                {
                    Name = "P",
                    Routines = new[] { new RoutineInfo { Name = "R", Language = "ST", Source = "x := '</pre><img src=x onerror=alert(1)>';" } }
                }
            }
        };

        var html = HtmlReportRenderer.RenderProject(project, Array.Empty<LintFinding>());

        html.Should().NotContain("<script>alert").And.NotContain("<img src=x").And.NotContain("<b>v</b>");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public void Diff_ChangedRung_RendersBothSidesWithHighlights()
    {
        var result = new DiffResult
        {
            Summary = "Found 1 differences: 1 modified",
            Details = new List<DiffDetail>
            {
                new()
                {
                    Type = "Modified", Category = "Logic", Location = "Programs.P.Routines.R.Rungs[2]",
                    Message = "Rung 2 changed", Value1 = "XIC(a)OTE(b);", Value2 = "XIC(a)OTL(b);"
                }
            }
        };

        var html = HtmlReportRenderer.RenderDiff(result);

        html.Should().Contain("class=\"side old\"").And.Contain("class=\"side new\"");
        html.Should().Contain("class=\"lad-instr lad-del\"><title>OTE(b)</title>");
        html.Should().Contain("class=\"lad-instr lad-add\"><title>OTL(b)</title>");
        html.Should().Contain("class=\"lad-instr \"><title>XIC(a)</title>");
    }
}
