using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Containers;

/// <summary>
/// Base class for container effects with nested effect branches and standard persistence.
/// </summary>
public abstract class ContainerEffectBase : IContainerEffect, IProjectStatefulComponent
{
    private readonly List<ContainerEffectBranch> _branches = new();
    private AudioFormat _format = AudioFormat.Default;
    protected float[] Scratch = Array.Empty<float>();

    public IReadOnlyList<ContainerEffectBranch> Branches => _branches;

    public IReadOnlyList<IAudioEffect> Children =>
        _branches.SelectMany(b => b.Effects).ToList();

    public abstract string Name { get; }

    string IAudioEffect.TypeId => GetTypeId();
    protected abstract string GetTypeId();
    public bool Enabled { get; set; } = true;

    public virtual IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();

    protected List<ContainerEffectBranch> MutableBranches => _branches;

    public virtual void Prepare(AudioFormat format)
    {
        _format = format;
        EnsureScratch(format);
        foreach (var child in Children)
            ContainerRenderer.PrepareEffect(child, format);
    }

    public abstract void Process(Span<float> buffer);

    public abstract IAudioEffect Clone();

    public virtual void WriteProjectState(OngenWriter writer)
    {
        var store = ContainerWriteContext.Current?.Store ?? new SampleStore();
        ContainerPersistence.WriteEffectBranches(writer, _branches, store);
    }

    public virtual void ReadProjectState(OngenReader reader)
    {
        if (ContainerReadContext.Current is not { } ctx)
        {
            reader.ReadInt();
            return;
        }

        ContainerPersistence.ReadEffectBranches(reader, _branches, ctx.Instruments, ctx.Effects,
            ctx.MidiEffects, ctx.SampleLookup, ctx.Warnings);
    }

    protected void CloneBranchesInto(ContainerEffectBase dst)
    {
        foreach (var src in _branches)
        {
            var branch = new ContainerEffectBranch();
            foreach (var fx in src.Effects) branch.Effects.Add(fx.Clone());
            dst._branches.Add(branch);
        }
    }

    protected void EnsureScratch(AudioFormat format)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var len = Math.Max(8192, channels * 4096);
        if (Scratch.Length < len) Scratch = new float[len];
    }

    protected void EnsureBranchCount(int count)
    {
        while (_branches.Count < count) _branches.Add(new ContainerEffectBranch());
        while (_branches.Count > count) _branches.RemoveAt(_branches.Count - 1);
    }

    protected static ContainerEffectBranch CreateBranch(params IAudioEffect[] effects)
    {
        var branch = new ContainerEffectBranch();
        foreach (var fx in effects) branch.Effects.Add(fx);
        return branch;
    }
}

/// <summary>Thread-local context supplying <see cref="SampleStore"/> while writing container state.</summary>
public sealed class ContainerWriteContext
{
    public static ContainerWriteContext? Current { get; private set; }

    public required SampleStore Store { get; init; }

    public static IDisposable Scope(ContainerWriteContext ctx)
    {
        var prev = Current;
        Current = ctx;
        return new ScopeToken(prev);
    }

    private sealed class ScopeToken : IDisposable
    {
        private readonly ContainerWriteContext? _prev;
        public ScopeToken(ContainerWriteContext? prev) => _prev = prev;
        public void Dispose() => Current = _prev;
    }
}
