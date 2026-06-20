using Ongenet.Core.Audio.Instruments.Sfz;

namespace Ongenet.Core.Tests.Sfz;

public class SfzParserTests
{
    [Fact]
    public void ParsesRegionsWithSpacesCommentsAndInheritance()
    {
        const string sfz = @"
// a leading line comment
<global>
volume=-3 ampeg_release=0.5
<group>
ampeg_attack=0.01 /* inline block */ pan=-10
<region>
sample=Grand Piano C4.wav lokey=c4 hikey=e4 pitch_keycenter=d4 lovel=1 hivel=127
<region>
sample=subdir/Other Sample.wav key=a4 volume=0 // override global volume
";
        var doc = SfzParser.Parse(sfz);

        Assert.Equal(2, doc.Regions.Count);

        var r0 = doc.Regions[0];
        Assert.Equal("Grand Piano C4.wav", r0.Sample); // value keeps its spaces
        Assert.Equal(60, r0.Opcodes.GetKey("lokey", -1));
        Assert.Equal(64, r0.Opcodes.GetKey("hikey", -1));
        Assert.Equal(62, r0.Opcodes.GetKey("pitch_keycenter", -1));
        Assert.Equal(-3f, r0.Opcodes.GetFloat("volume", 99));      // inherited from <global>
        Assert.Equal(0.01f, r0.Opcodes.GetFloat("ampeg_attack", 0), 1e-6f); // from <group>
        Assert.Equal(-10f, r0.Opcodes.GetFloat("pan", 0));         // from <group>
        Assert.Equal(0.5f, r0.Opcodes.GetFloat("ampeg_release", 0)); // from <global>

        var r1 = doc.Regions[1];
        Assert.Equal("subdir/Other Sample.wav", r1.Sample);
        Assert.Equal(0f, r1.Opcodes.GetFloat("volume", 99));       // region overrides global
        Assert.Equal(69, r1.Opcodes.GetKeyAny(-1, "lokey", "key")); // key= shorthand
    }

    [Fact]
    public void ParsesControlDefaultPathCcAndDefines()
    {
        const string sfz = @"
<control>
default_path=samples\piano\
set_cc7=100 set_cc10=64
#define $REL 0.8
<global>
ampeg_release=$REL
<region>
sample=note.wav
";
        var doc = SfzParser.Parse(sfz);

        Assert.Equal("samples/piano/", doc.Control.DefaultPath); // backslashes normalized
        Assert.Equal(100, doc.Control.InitialCcValues[7]);
        Assert.Equal(64, doc.Control.InitialCcValues[10]);
        Assert.Equal(0.8f, doc.Regions[0].Opcodes.GetFloat("ampeg_release", 0)); // $REL expanded
    }

    [Fact]
    public void MasterScopeResetsGroupButNotGlobal()
    {
        const string sfz = @"
<global> volume=-6
<master> cutoff=2000
<group> resonance=3
<region> sample=a.wav
<master> cutoff=500
<region> sample=b.wav
";
        var doc = SfzParser.Parse(sfz);

        Assert.Equal(2, doc.Regions.Count);
        Assert.Equal(2000f, doc.Regions[0].Opcodes.GetFloat("cutoff", 0));
        Assert.Equal(3f, doc.Regions[0].Opcodes.GetFloat("resonance", 0));
        Assert.Equal(0, doc.Regions[0].GroupIndex);

        Assert.Equal(500f, doc.Regions[1].Opcodes.GetFloat("cutoff", 0)); // new <master> value
        Assert.False(doc.Regions[1].Opcodes.Has("resonance"));            // <group> reset
        Assert.Equal(-6f, doc.Regions[1].Opcodes.GetFloat("volume", 0));  // <global> persists
        Assert.Equal(-1, doc.Regions[1].GroupIndex);
    }

    [Fact]
    public void IncludeResolverIsInvoked()
    {
        const string main = "#include \"shared.sfz\"\n<region> sample=a.wav";
        var opts = new SfzParseOptions
        {
            IncludeResolver = path => path == "shared.sfz" ? "<global> volume=-9" : null
        };

        var doc = SfzParser.Parse(main, opts);

        Assert.Single(doc.Regions);
        Assert.Equal(-9f, doc.Regions[0].Opcodes.GetFloat("volume", 0));
    }

    [Fact]
    public void MissingIncludeIsWarnedNotFatal()
    {
        var doc = SfzParser.Parse("#include \"missing.sfz\"\n<region> sample=a.wav");

        Assert.Single(doc.Regions);
        Assert.Contains(doc.Warnings, w => w.Contains("missing.sfz"));
    }

    [Fact]
    public void EmptyInputYieldsNoRegions()
    {
        var doc = SfzParser.Parse("");
        Assert.Empty(doc.Regions);
        Assert.Same(SfzControl.Empty, doc.Control);
    }
}
