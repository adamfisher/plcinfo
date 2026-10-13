using System.Globalization;
using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class RockwellL5XRegressionTests : IDisposable
{
    private readonly RockwellL5XParser _parser = new();
    private readonly string _tempDirectory;

    public RockwellL5XRegressionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void TokenizeRung_SimpleRung_ReturnsInstructionsWithOperands()
    {
        var result = RockwellL5XParser.TokenizeRung("XIC(Motor_Run)TON(Timer1,?,?)OTE(Output_1);");

        result.Select(i => i.Mnemonic).Should().Equal("XIC", "TON", "OTE");
        result[0].Operands.Should().Equal("Motor_Run");
        result[1].Operands.Should().Equal("Timer1", "?", "?");
        result[1].Type.Should().Be("Timer");
        result[2].Operands.Should().Equal("Output_1");
        result.Select(i => i.Position).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void TokenizeRung_TagNamesStartingWithCapital_AreNotInstructions()
    {
        var result = RockwellL5XParser.TokenizeRung("XIC(StartButton)OTE(Motor1);");

        result.Should().HaveCount(2);
        result.Select(i => i.Mnemonic).Should().NotContain(new[] { "StartButton", "Motor1" });
    }

    [Fact]
    public void TokenizeRung_BranchesWithSpacesBeforeCommas_ParsesEveryLeg()
    {
        // Spacing as exported by Studio 5000 v36.
        var result = RockwellL5XParser.TokenizeRung(
            "XIC(I_ToggleEnable_LocalTag)[XIC(I_ToggleSINT_LocalTag) MOVE(127,O_SINT_LocalTag) ,XIO(I_ToggleSINT_LocalTag) MOVE(-128,O_SINT_LocalTag) ];");

        result.Select(i => i.Mnemonic).Should().Equal("XIC", "XIC", "MOVE", "XIO", "MOVE");
        result[4].Operands.Should().Equal("-128", "O_SINT_LocalTag");
    }

    [Fact]
    public void TokenizeRung_NestedExpressionsAndArrayIndexes_SplitOnlyTopLevelCommas()
    {
        var result = RockwellL5XParser.TokenizeRung("CPT(Dest,(A+B)*MAX(C,D))COP(Src[1,2],Dst[Idx+1],10);");

        result.Should().HaveCount(2);
        result[0].Mnemonic.Should().Be("CPT");
        result[0].Operands.Should().Equal("Dest", "(A+B)*MAX(C,D)");
        result[0].Type.Should().Be("Math");
        result[1].Operands.Should().Equal("Src[1,2]", "Dst[Idx+1]", "10");
    }

    [Fact]
    public void TokenizeRung_NoOperandInstruction_HasNoOperands()
    {
        var result = RockwellL5XParser.TokenizeRung("NOP();");

        result.Should().ContainSingle();
        result[0].Mnemonic.Should().Be("NOP");
        result[0].Operands.Should().BeEmpty();
    }

    [Fact]
    public void TokenizeRung_AddOnInstructionCall_IsOneInstruction()
    {
        var result = RockwellL5XParser.TokenizeRung("My_AOI(My_AOI_Inst,InTag,OutTag);");

        result.Should().ContainSingle();
        result[0].Mnemonic.Should().Be("My_AOI");
        result[0].Operands.Should().Equal("My_AOI_Inst", "InTag", "OutTag");
    }

    [Fact]
    public async Task ParseAsync_TaskInhibited_IsDisabled_ButDisableUpdateOutputsIsNot()
    {
        var file = await WriteL5X(@"
    <Tasks>
      <Task Name='Inhibited' Type='CONTINUOUS' InhibitTask='true' DisableUpdateOutputs='false'/>
      <Task Name='NoOutputs' Type='CONTINUOUS' InhibitTask='false' DisableUpdateOutputs='true'/>
    </Tasks>");

        var result = await _parser.ParseAsync(file);

        result.Tasks.Single(t => t.Name == "Inhibited").IsEnabled.Should().BeFalse();
        result.Tasks.Single(t => t.Name == "NoOutputs").IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_ControllerWithoutRevision_HasEmptyFirmwareRevision()
    {
        var file = await WriteL5X("", controllerAttributes: "");

        var result = await _parser.ParseAsync(file);

        result.Controller!.FirmwareRevision.Should().BeEmpty();
    }

    [Theory]
    [InlineData("10", new[] { 10 })]
    [InlineData("2 3", new[] { 2, 3 })]
    [InlineData("2,3", new[] { 2, 3 })]
    [InlineData("10 x", new[] { 10 })]
    public async Task ParseAsync_TagDimensions_ParseWithoutThrowing(string dimensions, int[] expected)
    {
        var file = await WriteL5X($@"
    <Tags>
      <Tag Name='Arr' TagType='Base' DataType='DINT' Dimensions='{dimensions}'/>
    </Tags>");

        var result = await _parser.ParseAsync(file);

        result.Tags.Single().ArrayDimensions.Should().Equal(expected);
    }

    [Fact]
    public async Task ParseAsync_EmptyRoutine_UsesTypeAttributeForLanguage()
    {
        var file = await WriteL5X(@"
    <Programs>
      <Program Name='P'>
        <Routines>
          <Routine Name='Prescan' Type='RLL'/>
        </Routines>
      </Program>
    </Programs>");

        var result = await _parser.ParseAsync(file);

        result.Programs[0].Routines[0].Language.Should().Be("RLL");
    }

    [Fact]
    public async Task ParseAsync_PeriodicTaskRate_ParsesIndependentOfCulture()
    {
        var file = await WriteL5X(@"
    <Tasks>
      <Task Name='Fast' Type='PERIODIC' Rate='2.5' Priority='5' Watchdog='500'/>
    </Tasks>");

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = await _parser.ParseAsync(file);
            result.Tasks[0].Rate.Should().Be(2.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task ParseAsync_ContextComponents_AreNotProjectContent()
    {
        // Partial exports carry referenced components with Use="Context" for import only.
        var file = await WriteL5X(@"
    <DataTypes>
      <DataType Name='Ref' Use='Context'><Members/></DataType>
    </DataTypes>
    <Tags>
      <Tag Name='Exported' TagType='Base' DataType='DINT' Use='Target'/>
      <Tag Name='Referenced' TagType='Base' DataType='DINT' Use='Context'/>
    </Tags>
    <Programs>
      <Program Name='Ctx' Use='Context'/>
      <Program Name='Main' Use='Target'/>
    </Programs>");

        var result = await _parser.ParseAsync(file);

        result.Tags.Select(t => t.Name).Should().Equal("Exported");
        result.Programs.Select(p => p.Name).Should().Equal("Main");
        result.DataTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_OnlineEditCopies_UsesOriginalContent()
    {
        var file = await WriteL5X(@"
    <Programs>
      <Program Name='P'>
        <Routines>
          <Routine Name='S' Type='ST'>
            <STContent OnlineEditType='PendingEdits'><Line Number='0'><![CDATA[x := 2;]]></Line></STContent>
            <STContent OnlineEditType='Original'><Line Number='0'><![CDATA[x := 1;]]></Line></STContent>
          </Routine>
          <Routine Name='L' Type='RLL'>
            <RLLContent>
              <Rung Number='0' Type='N'><Text><![CDATA[XIC(a)OTE(b);]]></Text></Rung>
              <Rung Number='1' Type='e'><Text><![CDATA[XIC(c)OTE(d);]]></Text></Rung>
            </RLLContent>
          </Routine>
        </Routines>
      </Program>
    </Programs>");

        var result = await _parser.ParseAsync(file);

        var routines = result.Programs.Single().Routines;
        routines.Single(r => r.Name == "S").Source.Should().Be("x := 1;");
        routines.Single(r => r.Name == "L").Rungs.Select(r => (r.Number, r.EditState)).Should().Equal((0, "N"), (1, "e"));
    }

    private async Task<string> WriteL5X(string controllerBody, string controllerAttributes = "MajorRev='33' MinorRev='1'")
    {
        var content = $@"<?xml version='1.0' encoding='UTF-8' standalone='yes'?>
<RSLogix5000Content SchemaRevision='1.0' SoftwareRevision='33.00' TargetType='Controller'>
  <Controller Name='Ctl' ProcessorType='1756-L83E' {controllerAttributes}>{controllerBody}
  </Controller>
</RSLogix5000Content>";
        var path = Path.Combine(_tempDirectory, $"{Guid.NewGuid()}.L5X");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
