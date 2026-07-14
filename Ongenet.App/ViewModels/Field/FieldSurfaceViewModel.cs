using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.App.ViewModels.Field;

/// <summary>
/// Runtime + design view-model for a freeform Field control surface. When <see cref="IsDesignMode"/>
/// is true, widgets can be moved/resized/bound; otherwise they drive/read the live graph.
/// </summary>
public sealed class FieldSurfaceViewModel : ViewModelBase
{
    private readonly FieldGraph _graph;
    private readonly Action _onSurfaceChanged;
    private FieldSurfaceDefinition _surface;
    private FieldWidgetViewModel? _selected;
    private bool _isDesignMode;
    private string _status = "";

    public FieldSurfaceViewModel(FieldGraph graph, FieldSurfaceDefinition surface, Action onSurfaceChanged)
    {
        _graph = graph;
        _surface = surface;
        _onSurfaceChanged = onSurfaceChanged;
        RebuildWidgets();
    }

    public FieldSurfaceDefinition Surface => _surface;
    public ObservableCollection<FieldWidgetViewModel> Widgets { get; } = new();

    public bool IsDesignMode
    {
        get => _isDesignMode;
        set
        {
            if (!SetField(ref _isDesignMode, value)) return;
            OnPropertyChanged(nameof(CanEditSelection));
        }
    }

    public FieldWidgetViewModel? SelectedWidget
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            OnPropertyChanged(nameof(CanEditSelection));
            OnPropertyChanged(nameof(SelectedLabel));
            OnPropertyChanged(nameof(SelectedKindName));
            OnPropertyChanged(nameof(BindingStatus));
            foreach (var w in Widgets) w.IsSelected = ReferenceEquals(w, value);
        }
    }

    public bool CanEditSelection => IsDesignMode && _selected is not null;

    public string SelectedLabel
    {
        get => _selected?.Label ?? "";
        set
        {
            if (_selected is null) return;
            _selected.Label = value;
            Persist();
        }
    }

    public string SelectedKindName => _selected?.Kind.ToString() ?? "";

    public string BindingStatus
    {
        get
        {
            if (_selected is null) return "";
            if (_selected.IsBindingResolved) return "Bound";
            if (_selected.BindingKind == FieldWidgetBindingKind.None) return "Unbound";
            return "Unresolved (disabled)";
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public double CanvasWidth
    {
        get => _surface.CanvasWidth;
        set
        {
            if (Math.Abs(_surface.CanvasWidth - value) < 0.5) return;
            _surface.CanvasWidth = Math.Max(160, value);
            OnPropertyChanged();
            Persist();
        }
    }

    public double CanvasHeight
    {
        get => _surface.CanvasHeight;
        set
        {
            if (Math.Abs(_surface.CanvasHeight - value) < 0.5) return;
            _surface.CanvasHeight = Math.Max(120, value);
            OnPropertyChanged();
            Persist();
        }
    }

    public IReadOnlyList<FieldWidgetKind> PaletteKinds { get; } = new[]
    {
        FieldWidgetKind.Knob, FieldWidgetKind.VSlider, FieldWidgetKind.HSlider,
        FieldWidgetKind.Toggle, FieldWidgetKind.Button, FieldWidgetKind.Choice,
        FieldWidgetKind.XYPad, FieldWidgetKind.ValueReadout,
        FieldWidgetKind.Text, FieldWidgetKind.Panel, FieldWidgetKind.Divider, FieldWidgetKind.Spacer,
        FieldWidgetKind.LevelMeter, FieldWidgetKind.Oscilloscope, FieldWidgetKind.EnvelopeDisplay
    };

    public void ReplaceSurface(FieldSurfaceDefinition surface)
    {
        _surface = surface;
        RebuildWidgets();
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    public void AddWidget(FieldWidgetKind kind)
    {
        if (!IsDesignMode) return;
        var widget = new FieldWidget
        {
            Kind = kind,
            X = 24 + Widgets.Count * 12,
            Y = 24 + Widgets.Count * 8,
            Width = DefaultWidth(kind),
            Height = DefaultHeight(kind),
            ZOrder = Widgets.Count,
            Label = kind.ToString(),
            BindingKind = IsVisualKind(kind) ? FieldWidgetBindingKind.Signal : FieldWidgetBindingKind.None
        };
        _surface.Widgets.Add(widget);
        Persist(rebuildExposed: true);
        SelectedWidget = Widgets.FirstOrDefault(w => w.Id == widget.Id);
        Status = $"Added {kind}";
    }

    public void DeleteSelected()
    {
        if (!IsDesignMode || _selected is null) return;
        _surface.Widgets.RemoveAll(w => w.Id == _selected.Id);
        Persist(rebuildExposed: true);
        SelectedWidget = null;
    }

    public void DuplicateSelected()
    {
        if (!IsDesignMode || _selected is null) return;
        var src = _surface.Widgets.FirstOrDefault(w => w.Id == _selected.Id);
        if (src is null) return;
        var copy = new FieldWidget
        {
            Kind = src.Kind,
            X = src.X + 16,
            Y = src.Y + 16,
            Width = src.Width,
            Height = src.Height,
            ZOrder = _surface.Widgets.Count,
            Label = src.Label,
            BindingKind = src.BindingKind,
            ParameterBinding = src.ParameterBinding is null ? null : new FieldParameterBinding
            {
                NodeId = src.ParameterBinding.NodeId,
                ParamIndex = src.ParameterBinding.ParamIndex,
                ExpectedKind = src.ParameterBinding.ExpectedKind
            },
            SecondaryParameterBinding = src.SecondaryParameterBinding is null ? null : new FieldParameterBinding
            {
                NodeId = src.SecondaryParameterBinding.NodeId,
                ParamIndex = src.SecondaryParameterBinding.ParamIndex,
                ExpectedKind = src.SecondaryParameterBinding.ExpectedKind
            },
            SignalBinding = src.SignalBinding is null ? null : new FieldSignalBinding
            {
                NodeId = src.SignalBinding.NodeId,
                PortId = src.SignalBinding.PortId
            }
        };
        _surface.Widgets.Add(copy);
        Persist(rebuildExposed: true);
        SelectedWidget = Widgets.FirstOrDefault(w => w.Id == copy.Id);
    }

    public void BringSelectedToFront()
    {
        if (_selected is null) return;
        var max = _surface.Widgets.Count == 0 ? 0 : _surface.Widgets.Max(w => w.ZOrder) + 1;
        var model = _surface.Widgets.FirstOrDefault(w => w.Id == _selected.Id);
        if (model is null) return;
        model.ZOrder = max;
        Persist();
    }

    public void SendSelectedToBack()
    {
        if (_selected is null) return;
        var min = _surface.Widgets.Count == 0 ? 0 : _surface.Widgets.Min(w => w.ZOrder) - 1;
        var model = _surface.Widgets.FirstOrDefault(w => w.Id == _selected.Id);
        if (model is null) return;
        model.ZOrder = min;
        Persist();
    }

    /// <summary>Binds the selected widget to a graph node parameter (and exposes it for automation).</summary>
    public void BindSelectedToParameter(Guid nodeId, int paramIndex)
    {
        if (_selected is null) return;
        var node = _graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || paramIndex < 0 || paramIndex >= node.Parameters.Count) return;
        var param = node.Parameters[paramIndex];
        var kind = FieldExposedParameters.KindOf(param);
        var model = _surface.Widgets.First(w => w.Id == _selected.Id);
        model.BindingKind = FieldWidgetBindingKind.Parameter;
        model.ParameterBinding = new FieldParameterBinding
        {
            NodeId = nodeId,
            ParamIndex = paramIndex,
            ExpectedKind = kind
        };
        if (string.IsNullOrWhiteSpace(model.Label)) model.Label = param.Name;
        EnsureExposed(nodeId, paramIndex, kind, model.Label);
        Persist(rebuildExposed: false);
        RebuildWidgets();
        SelectedWidget = Widgets.FirstOrDefault(w => w.Id == model.Id);
        Status = $"Bound to {param.Name}";
    }

    /// <summary>Binds a visual widget to a scope/waveform node.</summary>
    public void BindSelectedToSignal(Guid nodeId, string portId = "out")
    {
        if (_selected is null) return;
        var model = _surface.Widgets.First(w => w.Id == _selected.Id);
        model.BindingKind = FieldWidgetBindingKind.Signal;
        model.SignalBinding = new FieldSignalBinding { NodeId = nodeId, PortId = portId };
        Persist();
        RebuildWidgets();
        SelectedWidget = Widgets.FirstOrDefault(w => w.Id == model.Id);
    }

    public void ApplyGeometry(Guid widgetId, double x, double y, double width, double height)
    {
        var model = _surface.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (model is null) return;
        const double grid = 8;
        model.X = Math.Round(x / grid) * grid;
        model.Y = Math.Round(y / grid) * grid;
        model.Width = Math.Max(24, Math.Round(width / grid) * grid);
        model.Height = Math.Max(24, Math.Round(height / grid) * grid);
        Persist();
    }

    public IEnumerable<(string Label, Guid NodeId, int ParamIndex)> EnumerateBindableParameters()
    {
        foreach (var node in _graph.Nodes)
        {
            for (var i = 0; i < node.Parameters.Count; i++)
                yield return ($"{node.DisplayName} → {node.Parameters[i].Name}", node.Id, i);
        }
    }

    public IEnumerable<(string Label, Guid NodeId)> EnumerateSignalNodes()
    {
        foreach (var node in _graph.Nodes)
        {
            if (node is IWaveformSource || node is ScopeNode || node is AudioOutNode)
                yield return (node.DisplayName, node.Id);
        }
    }

    /// <summary>Pulls live meter/waveform samples for visual widgets (UI thread).</summary>
    public void RefreshVisuals()
    {
        foreach (var widget in Widgets)
            widget.RefreshVisual(_graph);
    }

    private void EnsureExposed(Guid nodeId, int paramIndex, FieldBoundParamKind kind, string displayName)
    {
        if (_surface.ExposedControls.Any(e => e.NodeId == nodeId && e.ParamIndex == paramIndex)) return;
        _surface.ExposedControls.Add(new FieldExposedControl
        {
            NodeId = nodeId,
            ParamIndex = paramIndex,
            ExpectedKind = kind,
            DisplayName = displayName
        });
    }

    private void Persist(bool rebuildExposed = false)
    {
        if (rebuildExposed)
        {
            // Keep manually ordered exposed list; only seed when empty.
            FieldSurfaceSerializer.EnsureExposedFromParameterWidgets(_surface);
        }

        RebuildWidgets();
        _onSurfaceChanged();
    }

    private void RebuildWidgets()
    {
        var selectedId = _selected?.Id;
        Widgets.Clear();
        foreach (var widget in _surface.Widgets.OrderBy(w => w.ZOrder))
            Widgets.Add(new FieldWidgetViewModel(widget, _graph));
        SelectedWidget = selectedId is { } id ? Widgets.FirstOrDefault(w => w.Id == id) : null;
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private static bool IsVisualKind(FieldWidgetKind kind)
        => kind is FieldWidgetKind.LevelMeter or FieldWidgetKind.Oscilloscope or FieldWidgetKind.EnvelopeDisplay;

    private static double DefaultWidth(FieldWidgetKind kind) => kind switch
    {
        FieldWidgetKind.HSlider => 140,
        FieldWidgetKind.Panel => 160,
        FieldWidgetKind.Oscilloscope => 180,
        FieldWidgetKind.EnvelopeDisplay => 160,
        FieldWidgetKind.Text => 100,
        FieldWidgetKind.Divider => 120,
        FieldWidgetKind.Spacer => 40,
        _ => 72
    };

    private static double DefaultHeight(FieldWidgetKind kind) => kind switch
    {
        FieldWidgetKind.VSlider => 120,
        FieldWidgetKind.Panel => 100,
        FieldWidgetKind.Oscilloscope => 90,
        FieldWidgetKind.EnvelopeDisplay => 80,
        FieldWidgetKind.HSlider => 40,
        FieldWidgetKind.Text => 28,
        FieldWidgetKind.Divider => 8,
        FieldWidgetKind.Spacer => 40,
        FieldWidgetKind.LevelMeter => 100,
        _ => 72
    };
}

/// <summary>One widget instance on the surface, resolved against the live graph.</summary>
public sealed class FieldWidgetViewModel : ViewModelBase
{
    private readonly FieldWidget _model;
    private readonly float[] _waveScratch = new float[256];
    private bool _isSelected;

    public FieldWidgetViewModel(FieldWidget model, FieldGraph graph)
    {
        _model = model;
        Resolve(graph);
    }

    public Guid Id => _model.Id;
    public FieldWidgetKind Kind => _model.Kind;
    public FieldWidgetBindingKind BindingKind => _model.BindingKind;
    public double X { get => _model.X; set => _model.X = value; }
    public double Y { get => _model.Y; set => _model.Y = value; }
    public double Width { get => _model.Width; set => _model.Width = value; }
    public double Height { get => _model.Height; set => _model.Height = value; }

    public string Label
    {
        get => _model.Label;
        set { _model.Label = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsBindingResolved { get; private set; }
    public FloatParameter? FloatParam { get; private set; }
    public BoolParameter? BoolParam { get; private set; }
    public ChoiceParameter? ChoiceParam { get; private set; }
    public FloatParameter? SecondaryFloatParam { get; private set; }
    public IWaveformSource? WaveformSource { get; private set; }
    public FieldNode? BoundNode { get; private set; }

    public double Level { get; private set; }
    public float[] Waveform { get; private set; } = Array.Empty<float>();
    public int WaveformRevision { get; private set; }

    public double EnvAttack => ReadNamed("Attack");
    public double EnvDecay => ReadNamed("Decay");
    public double EnvSustain => ReadNamed("Sustain", 1);
    public double EnvRelease => ReadNamed("Release");

    private void Resolve(FieldGraph graph)
    {
        IsBindingResolved = false;
        FloatParam = null;
        BoolParam = null;
        ChoiceParam = null;
        SecondaryFloatParam = null;
        WaveformSource = null;
        BoundNode = null;

        if (_model.BindingKind == FieldWidgetBindingKind.Parameter && _model.ParameterBinding is { } b
            && FieldExposedParameters.TryResolve(graph, b, out var p) && p is not null)
        {
            IsBindingResolved = true;
            BoundNode = graph.Nodes.FirstOrDefault(n => n.Id == b.NodeId);
            FloatParam = p as FloatParameter;
            BoolParam = p as BoolParameter;
            ChoiceParam = p as ChoiceParameter;
        }

        if (_model.SecondaryParameterBinding is { } b2
            && FieldExposedParameters.TryResolve(graph, b2, out var p2))
            SecondaryFloatParam = p2 as FloatParameter;

        if (_model.BindingKind == FieldWidgetBindingKind.Signal && _model.SignalBinding is { } sig)
        {
            BoundNode = graph.Nodes.FirstOrDefault(n => n.Id == sig.NodeId);
            if (BoundNode is IWaveformSource src)
            {
                WaveformSource = src;
                IsBindingResolved = true;
            }
            else if (BoundNode is not null)
            {
                // Envelope displays can bind to an envelope node without waveform.
                IsBindingResolved = Kind == FieldWidgetKind.EnvelopeDisplay;
            }
        }

        if (_model.BindingKind == FieldWidgetBindingKind.None
            && Kind is FieldWidgetKind.Text or FieldWidgetKind.Panel or FieldWidgetKind.Divider
                or FieldWidgetKind.Spacer)
            IsBindingResolved = true;
    }

    public void RefreshVisual(FieldGraph graph)
    {
        Resolve(graph);
        if (WaveformSource is { } src)
        {
            var n = src.CaptureLatest(_waveScratch);
            if (n > 0)
            {
                var copy = new float[n];
                Array.Copy(_waveScratch, copy, n);
                Waveform = copy;
                WaveformRevision++;
                double peak = 0;
                for (var i = 0; i < n; i++)
                {
                    var a = Math.Abs(copy[i]);
                    if (a > peak) peak = a;
                }

                Level = peak;
                OnPropertyChanged(nameof(Waveform));
                OnPropertyChanged(nameof(WaveformRevision));
                OnPropertyChanged(nameof(Level));
            }
        }

        OnPropertyChanged(nameof(EnvAttack));
        OnPropertyChanged(nameof(EnvDecay));
        OnPropertyChanged(nameof(EnvSustain));
        OnPropertyChanged(nameof(EnvRelease));
        OnPropertyChanged(nameof(IsBindingResolved));
    }

    private double ReadNamed(string name, double fallback = 0)
    {
        if (BoundNode is null) return fallback;
        foreach (var p in BoundNode.Parameters)
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (p is FloatParameter f) return f.Value;
        }

        return fallback;
    }
}
