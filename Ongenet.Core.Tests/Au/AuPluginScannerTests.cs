using System;
using System.Linq;
using Ongenet.Au;

namespace Ongenet.Core.Tests.Au;

public class AuPluginScannerTests
{
    [Fact]
    public void ScanReturnsEmptyOffMacWithoutThrowing()
    {
        // The scanner guards by OS: on non-macOS it must be a safe no-op (never touches Apple frameworks).
        var scanner = new AuPluginScanner();
        var result = scanner.Scan();
        Assert.NotNull(result);
        if (!OperatingSystem.IsMacOS()) Assert.Empty(result);
    }

    [Fact]
    public void ScanEnumeratesSystemAudioUnitsOnMac()
    {
        if (!OperatingSystem.IsMacOS()) return; // Component Manager exists only on macOS.

        var scanner = new AuPluginScanner();
        var result = scanner.Scan();

        // macOS always ships built-in Audio Units (e.g. Apple's DLS synth + effects), so a healthy
        // interop layer must find at least one and classify each as an instrument and/or an effect.
        Assert.NotEmpty(result);
        Assert.All(result, d =>
        {
            Assert.True(d.IsInstrument || d.IsEffect);
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
        });
    }

    [Fact]
    public void MakeIdIsStableAndDistinctPerComponent()
    {
        var a = AuPluginBase.MakeId(0x61756D75, 0x646C7301, 0x6170706C);
        var b = AuPluginBase.MakeId(0x61756D75, 0x646C7302, 0x6170706C);
        Assert.StartsWith("au:", a);
        Assert.NotEqual(a, b);
    }
}
