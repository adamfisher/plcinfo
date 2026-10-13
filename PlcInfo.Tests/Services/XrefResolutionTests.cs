using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Parsing;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Services;

[UnitTest]
public class XrefResolutionTests
{
    private static RoutineInfo St(string source) => new() { Name = "Body", Language = "ST", Source = source };

    [Fact]
    public void QualifiedGvlAccess_ResolvesToTheGvlMember()
    {
        var project = new PlcProject
        {
            Vendor = "Beckhoff",
            Tags = new[] { new TagInfo { Name = "arrConnected", Scope = "GVL_HMI" } },
            Programs = new[] { new ProgramInfo { Name = "MAIN", Routines = new[] { St("GVL_HMI.arrConnected[i] := TRUE;") } } }
        };

        var entry = new XrefService().Build(project).Should().ContainSingle().Subject;

        entry.Should().BeEquivalentTo(new { Tag = "arrConnected", Scope = "GVL_HMI", Access = "Write" });
    }

    [Fact]
    public void ThisAndSuperMembers_ResolveToPouLocals()
    {
        var project = new PlcProject
        {
            Programs = new[]
            {
                new ProgramInfo
                {
                    Name = "FB_Dev",
                    LocalTags = new[] { new TagInfo { Name = "sPath", Scope = "FB_Dev" }, new TagInfo { Name = "bDone", Scope = "FB_Dev" } },
                    Routines = new[] { St("THIS^.bDone := THIS^.sPath <> '';") }
                }
            }
        };

        new XrefService().Build(project).Select(e => (e.Tag, e.Access)).Should().Equal(("bDone", "Write"), ("sPath", "Read"));
    }

    [Fact]
    public void InheritedMemberUse_CountsForTheBaseBlock()
    {
        var baseFb = new ProgramInfo { Name = "FB_Base", LocalTags = new[] { new TagInfo { Name = "MqttRetain", Scope = "FB_Base" } } };
        var derived = new ProgramInfo
        {
            Name = "FB_Derived",
            Properties = new Dictionary<string, string> { ["Extends"] = "FB_Base" },
            Routines = new[] { St("MqttRetain := TRUE;") }
        };
        var project = new PlcProject { Programs = new[] { baseFb, derived } };

        new XrefService().Build(project).Should().ContainSingle().Which.Scope.Should().Be("FB_Base");
        new LintService().Analyze(project).Should().NotContain(f => f.Code == "PLC005");
    }

    [Fact]
    public void SiemensQuotedDbMember_UsesLongestDottedMemberAndMarksParentUsed()
    {
        var project = new PlcProject
        {
            Vendor = "Siemens",
            Tags = new[]
            {
                new TagInfo { Name = "General", Scope = "Alarms_DB" },
                new TagInfo { Name = "General.EmergencyStop", Scope = "Alarms_DB" }
            },
            Blocks = new[]
            {
                new BlockInfo { Name = "Main", Type = "OB", Source = "IF \"Alarms_DB\".General.EmergencyStop THEN\n  #x := 1;\nEND_IF;" }
            }
        };

        new XrefService().Build(project).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Tag = "General.EmergencyStop", Scope = "Alarms_DB", Access = "Read" });
        new LintService().Analyze(project).Should().NotContain(f => f.Code == "PLC006");
    }

    [Fact]
    public void Siemens_BoxOperandsAreReads_CoilsAreWrites()
    {
        var project = new PlcProject
        {
            Vendor = "Siemens",
            Blocks = new[]
            {
                new BlockInfo
                {
                    Name = "FB1",
                    Interface = new Dictionary<string, string> { ["Static.tmr"] = "TON_TIME", ["Output.Run"] = "Bool" },
                    Networks = new[]
                    {
                        new NetworkInfo
                        {
                            Number = 1,
                            Instructions = new[]
                            {
                                new InstructionInfo { Mnemonic = "TON", Operands = new[] { "#tmr" } },
                                new InstructionInfo { Mnemonic = "Coil", Operands = new[] { "#Run" } }
                            }
                        }
                    }
                }
            }
        };

        new XrefService().Build(project).Select(e => (e.Tag, e.Access)).Should().Equal(("tmr", "Read"), ("Run", "Write"));
    }

    [Fact]
    public async Task RealTwinCatProject_GvlAccessFromMainIsFound()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Beckhoff", "HotConnect",
            "HotConnectSample_DIPC", "HotCOnnectSample_DIPC.tsproj");
        var project = await new TwinCatParser().ParseAsync(path);

        var uses = new XrefService().Build(project).Where(e => e.Tag == "arrConnected").ToList();

        uses.Should().NotBeEmpty();
        uses.Should().OnlyContain(e => e.Scope == "GVL_HMI" && e.Container == "MAIN");
        new LintService().Analyze(project).Should().NotContain(f => f.Location == "GVL_HMI.arrConnected");
    }
}
