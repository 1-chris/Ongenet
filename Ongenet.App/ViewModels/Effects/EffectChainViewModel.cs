using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Services;
using Ongenet.Core.Services.Interfaces;
using Ongenet.App.Services;
using Ongenet.App.Localization;
using Ongenet.App.Views.Windows;

namespace Ongenet.App.ViewModels.Effects
{
    public sealed record MasteringChainOption(string Key, string DisplayName, string Description, string[] WorkflowSteps)
    {
        public string EstimatedLoudness => Key switch
        {
            "full" or "full+" or "streaming" or "audiophile" or "reference" => "~−14 LUFS / −1 dBTP",
            "club" or "techno" => "~−9 LUFS / −0.3 dBTP",
            "glue" => "~−10 LUFS / −0.5 dBTP",
            "podcast" => "~−16 LUFS / −1.5 dBTP",
            _ => string.Empty
        };

        public string DisplayDescription => EstimatedLoudness.Length == 0
            ? Description
            : $"{Description} · Estimated {EstimatedLoudness}";
    }
    public sealed record WorkflowStepItem(string Label, int Index);
    public sealed record MeterTapChoice(MasterMeterTap Tap, string Label);
    public sealed record AbSlotInfo(string Name, DateTime StoredAt, double IntegratedLufs, string Notes);

    /// <summary>
    /// A reusable, self-contained editor for one insert-effect chain — the list of effect cards, the
    /// grouped "Add effect" menu, and add/remove/reorder/bypass with undo. It operates on a supplied
    /// backing <see cref="List{IAudioEffect}"/> plus a commit delegate, so the same control drives both a
    /// track's (post) chain and an individual instrument slot's (pre) chain. Created fresh whenever the
    /// edited target changes; dispose to unsubscribe playback clock and delivery-target events.
    /// </summary>
    public sealed class EffectChainViewModel : ViewModelBase, IDisposable
    {
        private readonly List<IAudioEffect> _effects;
        private readonly Action _commit;
        private readonly Action _changed;
        private readonly IEffectRegistry _registry;
        private readonly IHistoryService _history;
        private readonly ITransportService _transport;
        private readonly IPlaybackClock _clock;
        private readonly bool _isMasterTrack;
        private readonly IPresetLibrary? _presetLibrary;
        private bool _disposed;

        private bool[]? _bypassSnapshot;
        private bool[]? _compareBypassSnapshot;
        private bool _chainBypassed;
        private List<IAudioEffect>? _abSlotA;
        private List<IAudioEffect>? _abSlotB;
        private AbSlotInfo? _abInfoA;
        private AbSlotInfo? _abInfoB;
        private bool _abIsB;
        private bool _abLoudnessMatch = true;
        private bool _compareFullChainBypass;
        private double _compareMakeupDb;
        private ToolEffect? _compareTool;
        private bool _compareToolWasInserted;
        private double _compareToolOriginalGain;
        private double _compareTargetLufs = double.NegativeInfinity;
        private AudioSampleBuffer? _reference;
        private AudioSampleBuffer? _referenceB;
        private int _activeReferenceSlot;
        private double _referenceGainDb;
        private bool _referenceLatched;
        private readonly float[] _liveSpectrumSamples = new float[2048];
        private readonly Queue<double> _lufsHistory = new();
        private readonly IAudioEngine? _engine;
        private readonly IMasteringDeliveryTarget? _deliveryTarget;
        private MasteringChainOption _selectedMasteringChain;
        private bool _appendMasteringChain;
        private bool _referenceAuditionSynced = true;
        private bool _autoSyncLimiterCeiling = true;
        private bool _sectionChainExpanded = true;
        private bool _sectionMeteringExpanded = true;
        private bool _sectionDeliveryExpanded = true;
        private bool _sectionReferenceExpanded = true;
        private string _lufsHistoryWindow = "30s";
        private string _effectSearchFilter = string.Empty;
        private Action? _registryChangedHandler;
        private Action? _presetLibraryChangedHandler;

        private static readonly string[] CategoryOrder =
            { "Mastering", "Mastering essentials", "Dynamics", "EQ & Filter", "Modulation", "Delay & Reverb", "Distortion", "Pitch", "Utility", "Visualizer", "CLAP", "LV2", "VST2", "VST3", "AU" };

        private static readonly HashSet<string> MasteringEssentialIds = new(StringComparer.Ordinal)
        {
            EqEffect.TypeId, MidSideEqEffect.TypeId, CompressorEffect.TypeId,
            StereoWidthEffect.TypeId, ClipperEffect.TypeId, PeakLimiterEffect.TypeId,
            MultibandCompressorEffect.TypeId, ToolEffect.TypeId, SpectrumEffect.TypeId,
            LoudnessMeterEffect.TypeId, MatchEqEffect.TypeId, LinearPhaseEqEffect.TypeId,
            DcOffsetEffect.TypeId, DeEsserEffect.TypeId
        };

        public EffectChainViewModel(List<IAudioEffect> effects, Action commit, Action changed,
            IEffectRegistry registry, IHistoryService history, ITransportService transport, IPlaybackClock clock,
            bool isMasterTrack = false)
        {
            _effects = effects;
            _commit = commit;
            _changed = changed;
            _registry = registry;
            _history = history;
            _transport = transport;
            _clock = clock;
            _isMasterTrack = isMasterTrack;
            _engine = App.ServiceProvider?.GetService<IAudioEngine>();
            _deliveryTarget = isMasterTrack
                ? App.ServiceProvider?.GetService<IMasteringDeliveryTarget>()
                : null;
            _presetLibrary = App.ServiceProvider?.GetService<IPresetLibrary>();

            BypassAllCommand = new RelayCommand(BypassAll, () => HasEffects);
            RestoreBypassCommand = new RelayCommand(RestoreBypass, () => _bypassSnapshot is not null);
            ApplyFullMasterCommand = new RelayCommand(() => ApplyChain("full", false));
            ApplySelectedMasteringChainCommand = new RelayCommand(
                () => ApplyChain(SelectedMasteringChain.Key, AppendMasteringChain));
            ToggleCompareBypassCommand = new RelayCommand(ToggleCompareBypass, () => HasEffects);
            StoreAbACommand = new RelayCommand(() => CaptureAb(ref _abSlotA, ref _abInfoA, "A"));
            StoreAbBCommand = new RelayCommand(() => CaptureAb(ref _abSlotB, ref _abInfoB, "B"));
            ToggleAbCommand = new RelayCommand(ToggleAb, () => _abSlotA is not null && _abSlotB is not null);
            CaptureReferenceToMatchEqCommand = new RelayCommand(CaptureReferenceToMatchEq, () => HasReference);
            SelectWorkflowStepCommand = new RelayCommand<WorkflowStepItem>(SelectWorkflowStep, step => step is not null);
            ResetLoudnessCommand = new RelayCommand(ResetLoudness);
            ToggleMonoCommand = new RelayCommand(ToggleMono);
            SyncLimiterCeilingCommand = new RelayCommand(SyncLimiterCeilingToDelivery, () => ShowMasteringTools);
            PreflightAnalyseCommand = new RelayCommand(() => _ = RunPreflightAnalyseAsync(), () => ShowMasteringTools);
            ToggleReferenceLatchCommand = new RelayCommand(ToggleReferenceLatch, () => HasReference);
            SuggestLufsAssistCommand = new RelayCommand(SuggestLufsAssist, () => ShowMasteringTools);

            MasteringChainOptions = CreateMasteringChainOptions();
            _selectedMasteringChain = MasteringChainOptions[0];

            _registryChangedHandler = () => Dispatcher.UIThread.Post(RebuildAddable);
            _registry.Changed += _registryChangedHandler;
            _presetLibraryChangedHandler = () => Dispatcher.UIThread.Post(RebuildMasteringChainOptions);
            if (_presetLibrary is not null)
                _presetLibrary.Changed += _presetLibraryChangedHandler;
            _clock.Tick += OnPlaybackTick;
            if (_deliveryTarget is not null)
                _deliveryTarget.Changed += OnDeliveryTargetChanged;

            RebuildAddable();
            Rebuild();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clock.Tick -= OnPlaybackTick;
            if (_deliveryTarget is not null)
                _deliveryTarget.Changed -= OnDeliveryTargetChanged;
            if (_registryChangedHandler is not null)
                _registry.Changed -= _registryChangedHandler;
            if (_presetLibrary is not null && _presetLibraryChangedHandler is not null)
                _presetLibrary.Changed -= _presetLibraryChangedHandler;
            if (_referenceLatched)
                StopReferenceAudition();
        }

        public ObservableCollection<EffectViewModel> Effects { get; } = new();

        public IReadOnlyList<EffectCategoryViewModel> AddableCategories { get; private set; } =
            new List<EffectCategoryViewModel>();

        public string EffectSearchFilter
        {
            get => _effectSearchFilter;
            set
            {
                if (!SetField(ref _effectSearchFilter, value)) return;
                RebuildAddable();
            }
        }

        public bool HasEffects => Effects.Count > 0;
        public bool IsMasterTrack => _isMasterTrack;
        public bool ShowMasteringTools => _isMasterTrack;
        public bool ChainBypassed => _chainBypassed;
        public bool AbIsB => _abIsB;
        public string AbLabel => _abIsB
            ? Loc.Get("Mastering_AB_ActiveB", "Hearing B")
            : Loc.Get("Mastering_AB_ActiveA", "Hearing A");
        public bool HasAbSlotA => _abSlotA is not null;
        public bool HasAbSlotB => _abSlotB is not null;
        public string AbSlotALabel => HasAbSlotA
            ? Loc.Get("Mastering_AB_SlotReadyA", "A ✓")
            : Loc.Get("Mastering_AB_StoreA", "Store A");
        public string AbSlotBLabel => HasAbSlotB
            ? Loc.Get("Mastering_AB_SlotReadyB", "B ✓")
            : Loc.Get("Mastering_AB_StoreB", "Store B");
        public string AbSlotADetail => FormatAbDetail(_abInfoA);
        public string AbSlotBDetail => FormatAbDetail(_abInfoB);
        public bool AbLoudnessMatch
        {
            get => _abLoudnessMatch;
            set => SetField(ref _abLoudnessMatch, value);
        }
        public bool CompareBypassActive { get; private set; }
        public bool CompareFullChainBypass
        {
            get => _compareFullChainBypass;
            set => SetField(ref _compareFullChainBypass, value);
        }
        public string CompareInsertsToolHint { get; private set; } = string.Empty;
        public string CompareMakeupText => CompareBypassActive
            ? Loc.Get("Mastering_Compare_Makeup", "Makeup {0:+0.0;-0.0;0.0} dB", _compareMakeupDb)
            : string.Empty;
        public bool CompareMakeupSettled => CompareBypassActive && Math.Abs(_compareMakeupDb) < 0.5;
        public IReadOnlyList<MeterTapChoice> MeterTapChoices { get; } =
        [
            new(MasterMeterTap.PostFader, Loc.Get("Mastering_MeterTap_PostFader", "Post Fader")),
            new(MasterMeterTap.PreLimiter, Loc.Get("Mastering_MeterTap_PreLimiter", "Pre Limiter")),
            new(MasterMeterTap.PostChain, Loc.Get("Mastering_MeterTap_PostChain", "Post Chain"))
        ];
        public MasterMeterTap SelectedMeterTap
        {
            get => _engine?.MasterMeterTap ?? MasterMeterTap.PostFader;
            set
            {
                if (_engine is null || _engine.MasterMeterTap == value) return;
                _engine.MasterMeterTap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedMeterTapChoice));
            }
        }
        public MeterTapChoice SelectedMeterTapChoice
        {
            get => MeterTapChoices.First(c => c.Tap == SelectedMeterTap);
            set
            {
                if (value is not null) SelectedMeterTap = value.Tap;
            }
        }
        public string MasteringMeterText
        {
            get
            {
                static string F(double value) => double.IsNegativeInfinity(value) ? "−∞" : value.ToString("0.0");
                var m = _engine?.MasterMomentaryLufs ?? double.NegativeInfinity;
                var st = _engine?.MasterShortTermLufs ?? double.NegativeInfinity;
                var integrated = _engine?.MasterIntegratedLufs ?? double.NegativeInfinity;
                var lra = _engine?.MasterLoudnessRangeLu ?? double.NegativeInfinity;
                var tp = _engine?.MasterTruePeakMaxDbTp ?? -120;
                var correlation = _engine?.MasterCorrelation ?? 1;
                var target = _deliveryTarget?.TargetIntegratedLufs ?? -14;
                var delta = double.IsNegativeInfinity(integrated) ? double.NegativeInfinity : target - integrated;
                return $"M {F(m)} · ST {F(st)} · I {F(integrated)} · LRA {F(lra)} · {tp:0.0} dBTP · Corr {correlation:+0.00;-0.00;0.00} · ΔI {F(delta)} LU";
            }
        }
        public string ComplianceSummaryText
        {
            get
            {
                var integrated = _engine?.MasterIntegratedLufs ?? double.NegativeInfinity;
                var tp = _engine?.MasterTruePeakMaxDbTp ?? -120;
                if (double.IsNegativeInfinity(integrated))
                    return Loc.Get("Mastering_Compliance_NeedPlay", "Play to measure delivery compliance.");
                var targetLufs = _deliveryTarget?.TargetIntegratedLufs ?? -14;
                var targetTp = _deliveryTarget?.TargetTruePeakDbTp ?? -1;
                var lufsOk = Math.Abs(integrated - targetLufs) <= 1.0;
                var tpOk = tp <= targetTp + 0.05;
                var lufsLabel = lufsOk ? Loc.Get("Mastering_Compliance_Pass", "PASS") : Loc.Get("Mastering_Compliance_Fail", "FAIL");
                var tpLabel = tpOk ? Loc.Get("Mastering_Compliance_Pass", "PASS") : Loc.Get("Mastering_Compliance_Fail", "FAIL");
                return Loc.Get("Mastering_Compliance_Summary",
                    "LUFS {0} ({1:0.0} vs {2:0.#}) · TP {3} ({4:0.0} vs {5:0.0} dBTP)",
                    lufsLabel, integrated, targetLufs, tpLabel, tp, targetTp);
            }
        }
        public string PeakHoldText
        {
            get
            {
                var l = _engine?.MasterTruePeakLeftDbTp ?? -120;
                var r = _engine?.MasterTruePeakRightDbTp ?? -120;
                var sample = Math.Max(_engine?.MasterLevelLeft ?? 0, _engine?.MasterLevelRight ?? 0);
                var sampleDb = sample > 1e-9 ? 20 * Math.Log10(sample) : -120;
                var crest = double.IsNegativeInfinity(_engine?.MasterIntegratedLufs ?? double.NegativeInfinity)
                    ? double.NaN
                    : sampleDb - (_engine?.MasterIntegratedLufs ?? 0);
                var plr = double.IsNegativeInfinity(_engine?.MasterShortTermLufs ?? double.NegativeInfinity)
                    ? double.NaN
                    : (_engine?.MasterTruePeakMaxDbTp ?? -120) - (_engine?.MasterShortTermLufs ?? 0);
                static string F(double v) => double.IsNaN(v) || double.IsNegativeInfinity(v) ? "—" : v.ToString("0.0");
                return Loc.Get("Mastering_PeakHold",
                    "Hold L {0} / R {1} · Sample {2} dBFS · Crest {3} · PLR {4}",
                    F(l), F(r), F(sampleDb), F(crest), F(plr));
            }
        }
        public string NormalizePreviewText
        {
            get
            {
                var current = _engine?.MasterIntegratedLufs ?? double.NegativeInfinity;
                if (double.IsNegativeInfinity(current))
                    return Loc.Get("Mastering_NormalizePreview_Play", "Play the project to estimate normalization gain.");
                var target = _deliveryTarget?.TargetIntegratedLufs ?? -14;
                var gain = target - current;
                var tp = _engine?.MasterTruePeakMaxDbTp ?? -120;
                var estimatedTp = tp + gain;
                var targetTp = _deliveryTarget?.TargetTruePeakDbTp ?? -1;
                var relimit = estimatedTp > targetTp + 0.05
                    ? Loc.Get("Mastering_NormalizePreview_Relimit", " Will re-limit at delivery ceiling.")
                    : string.Empty;
                return Loc.Get("Mastering_NormalizePreview_GainTp",
                    "Estimated normalization gain: {0:+0.0;-0.0;0.0} dB · post TP ≈ {1:0.0} dBTP",
                    gain, estimatedTp) + relimit;
            }
        }
        public string PreflightReportText { get; private set; } = string.Empty;
        public string LufsAssistSuggestionText { get; private set; } = string.Empty;
        public Points LufsHistoryPoints
        {
            get
            {
                var points = new Points();
                var i = 0;
                var count = Math.Max(1, _lufsHistory.Count - 1);
                foreach (var lufs in _lufsHistory)
                {
                    var normalized = Math.Clamp((lufs + 36) / 30, 0, 1);
                    points.Add(new Point(i * (200.0 / count), 40 - normalized * 40));
                    i++;
                }
                return points;
            }
        }
        public double LufsTargetLineY
        {
            get
            {
                var target = _deliveryTarget?.TargetIntegratedLufs ?? -14;
                var normalized = Math.Clamp((target + 36) / 30, 0, 1);
                return 40 - normalized * 40;
            }
        }
        public string LufsHistoryWindow
        {
            get => _lufsHistoryWindow;
            set
            {
                if (!SetField(ref _lufsHistoryWindow, value)) return;
                _lufsHistory.Clear();
                OnPropertyChanged(nameof(LufsHistoryPoints));
            }
        }
        public IReadOnlyList<string> LufsHistoryWindows { get; } = ["30s", "60s", "Full"];
        public IReadOnlyList<MasteringChainOption> MasteringChainOptions { get; private set; }
        public MasteringChainOption SelectedMasteringChain
        {
            get => _selectedMasteringChain;
            set
            {
                if (value is null || !SetField(ref _selectedMasteringChain, value)) return;
                OnPropertyChanged(nameof(ActiveWorkflowTitle));
                OnPropertyChanged(nameof(ActiveWorkflowDescription));
                OnPropertyChanged(nameof(WorkflowSteps));
                OnPropertyChanged(nameof(WorkflowStepItems));
                OnPropertyChanged(nameof(WorkflowStepsText));
            }
        }
        public bool AppendMasteringChain
        {
            get => _appendMasteringChain;
            set => SetField(ref _appendMasteringChain, value);
        }
        public string ActiveWorkflowTitle => SelectedMasteringChain.DisplayName;
        public string ActiveWorkflowDescription => SelectedMasteringChain.Description;
        public IReadOnlyList<string> WorkflowSteps => SelectedMasteringChain.WorkflowSteps;
        public IReadOnlyList<WorkflowStepItem> WorkflowStepItems => WorkflowSteps
            .Select((label, index) => new WorkflowStepItem(label, index)).ToArray();
        public string WorkflowStepsText => string.Join(Environment.NewLine, WorkflowSteps);
        public string SelectedDeliveryPlatform
        {
            get => _deliveryTarget?.PlatformName ?? "Spotify";
            set
            {
                if (string.IsNullOrWhiteSpace(value)
                    || string.Equals(value, SelectedDeliveryPlatform, StringComparison.Ordinal)) return;
                _deliveryTarget?.ApplyPlatform(value);
            }
        }
        public IReadOnlyList<string> DeliveryPlatforms { get; } = DeliveryPlatformPresets.All
            .Select(p => p.Name)
            .Append("Custom")
            .ToArray();
        public bool IsCustomDeliveryPlatform =>
            string.Equals(SelectedDeliveryPlatform, "Custom", StringComparison.OrdinalIgnoreCase);
        public double CustomTargetLufs
        {
            get => _deliveryTarget?.TargetIntegratedLufs ?? -14;
            set
            {
                if (_deliveryTarget is null || !IsCustomDeliveryPlatform) return;
                _deliveryTarget.TargetIntegratedLufs = value;
            }
        }
        public double CustomTargetDbTp
        {
            get => _deliveryTarget?.TargetTruePeakDbTp ?? -1;
            set
            {
                if (_deliveryTarget is null || !IsCustomDeliveryPlatform) return;
                _deliveryTarget.TargetTruePeakDbTp = value;
            }
        }
        public string DeliveryTargetText
        {
            get
            {
                static string N(double value, string format) => value.ToString(format).Replace("-", "−");
                var lufs = _deliveryTarget?.TargetIntegratedLufs ?? -14;
                var dbTp = _deliveryTarget?.TargetTruePeakDbTp ?? -1;
                return $"{SelectedDeliveryPlatform} {N(lufs, "0.#")} LUFS / {N(dbTp, "0.0")} dBTP";
            }
        }
        public bool AutoSyncLimiterCeiling
        {
            get => _autoSyncLimiterCeiling;
            set => SetField(ref _autoSyncLimiterCeiling, value);
        }
        public bool LimiterCeilingMismatch
        {
            get
            {
                var limiter = _effects.OfType<PeakLimiterEffect>().LastOrDefault();
                if (limiter is null || _deliveryTarget is null) return false;
                return Math.Abs(limiter.CeilingDb - _deliveryTarget.TargetTruePeakDbTp) > 0.1;
            }
        }
        public string LimiterCeilingMismatchText => LimiterCeilingMismatch
            ? Loc.Get("Mastering_LimiterCeilingMismatch",
                "Limiter ceiling {0:0.0} dB differs from delivery {1:0.0} dBTP",
                _effects.OfType<PeakLimiterEffect>().LastOrDefault()?.CeilingDb ?? 0,
                _deliveryTarget?.TargetTruePeakDbTp ?? -1)
            : string.Empty;
        public bool SectionChainExpanded
        {
            get => _sectionChainExpanded;
            set => SetField(ref _sectionChainExpanded, value);
        }
        public bool SectionMeteringExpanded
        {
            get => _sectionMeteringExpanded;
            set => SetField(ref _sectionMeteringExpanded, value);
        }
        public bool SectionDeliveryExpanded
        {
            get => _sectionDeliveryExpanded;
            set => SetField(ref _sectionDeliveryExpanded, value);
        }
        public bool SectionReferenceExpanded
        {
            get => _sectionReferenceExpanded;
            set => SetField(ref _sectionReferenceExpanded, value);
        }
        public ObservableCollection<SignalFlowStageItem> SignalFlowStages { get; } = new();
        public int HighlightedEffectIndex { get; private set; } = -1;

        private void OnDeliveryTargetChanged()
        {
            OnPropertyChanged(nameof(SelectedDeliveryPlatform));
            OnPropertyChanged(nameof(DeliveryTargetText));
            OnPropertyChanged(nameof(IsCustomDeliveryPlatform));
            OnPropertyChanged(nameof(CustomTargetLufs));
            OnPropertyChanged(nameof(CustomTargetDbTp));
            OnPropertyChanged(nameof(MasteringMeterText));
            OnPropertyChanged(nameof(NormalizePreviewText));
            OnPropertyChanged(nameof(ComplianceSummaryText));
            OnPropertyChanged(nameof(LufsTargetLineY));
            OnPropertyChanged(nameof(LimiterCeilingMismatch));
            OnPropertyChanged(nameof(LimiterCeilingMismatchText));
            if (_autoSyncLimiterCeiling && LimiterCeilingMismatch)
                SyncLimiterCeilingToDelivery();
            foreach (var fx in Effects.OfType<PeakLimiterEffectViewModel>())
                fx.Refresh();
        }
        public bool HasReference => _reference is not null;
        public string ReferenceName { get; private set; } = string.Empty;
        public string ReferenceMatchText { get; private set; } = string.Empty;
        public Points ReferenceSpectrumPoints { get; private set; } = new();
        public Points LiveSpectrumPoints { get; private set; } = new();
        public bool ReferenceAuditionSynced
        {
            get => _referenceAuditionSynced;
            set => SetField(ref _referenceAuditionSynced, value);
        }
        public string ReferenceLegendText => Loc.Get("Mastering_Reference_Legend",
            "Live = sapphire · Reference = peach");
        public double ReferenceGainDb
        {
            get => _referenceGainDb;
            set => SetField(ref _referenceGainDb, value);
        }
        public bool ReferenceLatched
        {
            get => _referenceLatched;
            private set => SetField(ref _referenceLatched, value);
        }
        public bool HasReferenceB => _referenceB is not null;
        public int ActiveReferenceSlot
        {
            get => _activeReferenceSlot;
            set
            {
                if (!SetField(ref _activeReferenceSlot, value)) return;
                OnPropertyChanged(nameof(ActiveReferenceName));
            }
        }
        public string ActiveReferenceName => ActiveReferenceSlot == 1 && HasReferenceB
            ? ReferenceNameB
            : ReferenceName;
        public string ReferenceNameB { get; private set; } = string.Empty;

        public RelayCommand BypassAllCommand { get; }
        public RelayCommand RestoreBypassCommand { get; }
        public RelayCommand ApplyFullMasterCommand { get; }
        public RelayCommand ApplySelectedMasteringChainCommand { get; }
        public RelayCommand ToggleCompareBypassCommand { get; }
        public RelayCommand StoreAbACommand { get; }
        public RelayCommand StoreAbBCommand { get; }
        public RelayCommand ToggleAbCommand { get; }
        public RelayCommand CaptureReferenceToMatchEqCommand { get; }
        public RelayCommand<WorkflowStepItem> SelectWorkflowStepCommand { get; }
        public RelayCommand ResetLoudnessCommand { get; }
        public RelayCommand ToggleMonoCommand { get; }
        public RelayCommand SyncLimiterCeilingCommand { get; }
        public RelayCommand PreflightAnalyseCommand { get; }
        public RelayCommand ToggleReferenceLatchCommand { get; }
        public RelayCommand SuggestLufsAssistCommand { get; }
        public bool MonoAuditionActive => _effects.OfType<ToolEffect>().Any(t => t.Mono);

        private void OnPlaybackTick()
        {
            OnPropertyChanged(nameof(MasteringMeterText));
            OnPropertyChanged(nameof(NormalizePreviewText));
            OnPropertyChanged(nameof(ComplianceSummaryText));
            OnPropertyChanged(nameof(PeakHoldText));
            OnPropertyChanged(nameof(CompareMakeupText));
            OnPropertyChanged(nameof(CompareMakeupSettled));
            OnPropertyChanged(nameof(LimiterCeilingMismatch));
            OnPropertyChanged(nameof(LimiterCeilingMismatchText));
            var shortTerm = _engine?.MasterShortTermLufs ?? double.NegativeInfinity;
            if (!double.IsNegativeInfinity(shortTerm))
            {
                _lufsHistory.Enqueue(shortTerm);
                var max = _lufsHistoryWindow switch
                {
                    "60s" => 120,
                    "Full" => 600,
                    _ => 60
                };
                while (_lufsHistory.Count > max) _lufsHistory.Dequeue();
                OnPropertyChanged(nameof(LufsHistoryPoints));
                OnPropertyChanged(nameof(LufsTargetLineY));
            }
            UpdateCompareCompensation();
            UpdateLiveSpectrum();
            RebuildSignalFlowLevels();
            if (_transport.State != TransportState.Playing) return;
            foreach (var fx in Effects) fx.Refresh();
        }

        public void RefreshEditor(IPluginEditor editor)
        {
            foreach (var vm in Effects)
                if (ReferenceEquals(vm.Editor, editor)) { vm.NotifyEditorState(); return; }
        }

        private void RebuildAddable()
        {
            int Rank(string category)
            {
                var i = Array.IndexOf(CategoryOrder, category);
                return i < 0 ? CategoryOrder.Length : i;
            }

            var filter = _effectSearchFilter.Trim();
            var available = _registry.Available
                .Where(info => filter.Length == 0
                    || info.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            var groups = available
                .GroupBy(info => info.Category)
                .OrderBy(g => Rank(g.Key)).ThenBy(g => g.Key)
                .Select(g => new EffectCategoryViewModel(LocalizeCategory(g.Key),
                    g.Select(info => new AvailableEffectViewModel(
                        info.DisplayName, new RelayCommand(() => AddEffect(info.Id)))).ToList()))
                .ToList();

            if (_isMasterTrack)
            {
                var essentials = available
                    .Where(info => MasteringEssentialIds.Contains(info.Id))
                    .GroupBy(info => info.Id)
                    .Select(g => g.First())
                    .Select(info => new AvailableEffectViewModel(
                        info.DisplayName, new RelayCommand(() => AddEffect(info.Id))))
                    .ToList();
                groups.Insert(0, new EffectCategoryViewModel(LocalizeCategory("Mastering essentials"), essentials));
            }

            AddableCategories = groups;
            OnPropertyChanged(nameof(AddableCategories));
        }

        private static string LocalizeCategory(string category) => category switch
        {
            "Mastering essentials" => Loc.Get("EffectCategory_MasteringEssentials", category),
            "Mastering" => Loc.Get("EffectCategory_Mastering", category),
            "Dynamics" => Loc.Get("EffectCategory_Dynamics", category),
            "EQ & Filter" => Loc.Get("EffectCategory_EqFilter", category),
            "Modulation" => Loc.Get("EffectCategory_Modulation", category),
            "Delay & Reverb" => Loc.Get("EffectCategory_DelayReverb", category),
            "Distortion" => Loc.Get("EffectCategory_Distortion", category),
            "Pitch" => Loc.Get("EffectCategory_Pitch", category),
            "Utility" => Loc.Get("EffectCategory_Utility", category),
            "Visualizer" => Loc.Get("EffectCategory_Visualizer", category),
            _ => category
        };

        public void AddEffect(string id)
        {
            if (CreateEffect(id) is { } fx) Apply("Add effect", () => _effects.Add(fx));
        }

        public void InsertEffect(int index, string id)
        {
            if (CreateEffect(id) is { } fx) Apply("Add effect", () => _effects.Insert(Clamp(index), fx));
        }

        public void ReplaceEffectAt(int index, string id)
        {
            if (InRange(index) && CreateEffect(id) is { } fx) Apply("Replace effect", () => _effects[index] = fx);
        }

        public void AddEffectPreset(string presetPath)
        {
            if (LoadEffectPreset(presetPath) is { } fx) Apply("Add effect preset", () => _effects.Add(fx));
        }

        public void InsertEffectPreset(int index, string presetPath)
        {
            if (LoadEffectPreset(presetPath) is { } fx) Apply("Add effect preset", () => _effects.Insert(Clamp(index), fx));
        }

        public void ReplaceEffectPresetAt(int index, string presetPath)
        {
            if (InRange(index) && LoadEffectPreset(presetPath) is { } fx) Apply("Replace effect", () => _effects[index] = fx);
        }

        public int IndexOf(EffectViewModel vm) => _effects.IndexOf(vm.Effect);

        private IAudioEffect? CreateEffect(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return _registry.Create(id); }
            catch { return null; }
        }

        private IAudioEffect? LoadEffectPreset(string presetPath)
        {
            var instruments = App.ServiceProvider?.GetService<IInstrumentRegistry>();
            if (instruments is null) return null;
            try
            {
                using var fs = System.IO.File.OpenRead(presetPath);
                return Ongenet.Core.Persistence.PresetFile.Load(fs, instruments, _registry)?.Effect;
            }
            catch { return null; }
        }

        private int Clamp(int index) => Math.Clamp(index, 0, _effects.Count);
        private bool InRange(int index) => index >= 0 && index < _effects.Count;

        private void Apply(string historyLabel, Action mutate)
        {
            _history.Capture(historyLabel);
            mutate();
            _commit();
            _changed();
            Rebuild();
        }

        public void AddEffectChainPreset(string presetPath)
        {
            var chain = LoadEffectChainPreset(presetPath);
            if (chain is null || chain.Count == 0) return;

            _history.Capture("Add FX chain preset");
            foreach (var fx in chain) _effects.Add(fx);
            _commit();
            _changed();
            Rebuild();
        }

        private IReadOnlyList<IAudioEffect>? LoadEffectChainPreset(string presetPath)
        {
            var instruments = App.ServiceProvider?.GetService<IInstrumentRegistry>();
            if (instruments is null) return null;
            try
            {
                using var fs = File.OpenRead(presetPath);
                return Ongenet.Core.Persistence.PresetFile.Load(fs, instruments, _registry)?.Effects;
            }
            catch { return null; }
        }

        private string _chainPresetName = string.Empty;

        public string ChainPresetName
        {
            get => _chainPresetName;
            set => SetField(ref _chainPresetName, value);
        }

        public void SaveChainAsPreset()
        {
            if (_effects.Count == 0) return;
            var name = string.IsNullOrWhiteSpace(_chainPresetName) ? "FX Chain" : _chainPresetName.Trim();
            App.ServiceProvider?.GetService<IPresetLibrary>()?.SaveChain(_effects, name);
            ChainPresetName = string.Empty;
        }

        private void BypassAll()
        {
            _bypassSnapshot = _effects.Select(e => e.Enabled).ToArray();
            _history.Capture("Bypass master chain");
            foreach (var e in _effects) e.Enabled = false;
            _chainBypassed = true;
            _commit();
            _changed();
            Rebuild();
            OnPropertyChanged(nameof(ChainBypassed));
        }

        private void RestoreBypass()
        {
            if (_bypassSnapshot is null) return;
            _history.Capture("Restore master chain");
            for (var i = 0; i < _effects.Count && i < _bypassSnapshot.Length; i++)
                _effects[i].Enabled = _bypassSnapshot[i];
            _bypassSnapshot = null;
            _chainBypassed = false;
            _commit();
            _changed();
            Rebuild();
            OnPropertyChanged(nameof(ChainBypassed));
        }

        private async void ApplyChain(string key, bool append)
        {
            var userChain = key.StartsWith("user:", StringComparison.Ordinal)
                ? LoadEffectChainPreset(key["user:".Length..])
                : null;
            if (key.StartsWith("user:", StringComparison.Ordinal) && (userChain is null || userChain.Count == 0))
                return;
            if (!append && _effects.Count > 0 &&
                Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner } &&
                !await MessageDialog.Confirm(owner, Loc.Get("Mastering_Replace_Title"),
                    Loc.Get("Mastering_Replace_Message"), Loc.Get("Mastering_Replace_Confirm")))
                return;
            _history.Capture(append ? "Append mastering chain" : "Apply mastering chain");
            if (!append) _effects.Clear();
            if (userChain is not null)
            {
                foreach (var effect in userChain) _effects.Add(effect);
            }
            else
            {
                foreach (var effect in MasteringChains.Create(key)) _effects.Add(effect);
            }
            _commit();
            _changed();
            Rebuild();
        }

        private void ToggleCompareBypass()
        {
            if (!CompareBypassActive)
            {
                _compareBypassSnapshot = _effects.Select(e => e.Enabled).ToArray();
                _compareTargetLufs = _engine?.MasterShortTermLufs ?? double.NegativeInfinity;
                _history.Capture("Compare bypass");
                foreach (var e in _effects)
                {
                    if (_compareFullChainBypass || e is PeakLimiterEffect or ClipperEffect or LimiterEffect)
                        e.Enabled = false;
                }
                _compareTool = EnsureCompareTool(out _compareToolWasInserted);
                CompareInsertsToolHint = _compareFullChainBypass
                    ? Loc.Get("Mastering_Compare_FullHint", "Full master chain bypassed; Tool supplies loudness-matched comparison.")
                    : _compareToolWasInserted
                        ? Loc.Get("Mastering_Compare_ToolHint", "Clipper and limiter are bypassed; Tool was added for loudness-matched comparison.")
                        : Loc.Get("Mastering_Compare_ActiveHint", "Clipper and limiter are bypassed; Tool supplies loudness-matched comparison.");
                _compareToolOriginalGain = _compareTool.GainDb;
                _compareMakeupDb = 0;
                CompareBypassActive = true;
            }
            else
            {
                if (_compareBypassSnapshot is not null)
                {
                    for (var i = 0; i < _effects.Count && i < _compareBypassSnapshot.Length; i++)
                        _effects[i].Enabled = _compareBypassSnapshot[i];
                }
                _compareBypassSnapshot = null;
                if (_compareTool is not null)
                {
                    if (_compareToolWasInserted)
                        _effects.Remove(_compareTool);
                    else
                        _compareTool.GainDb = _compareToolOriginalGain;
                }
                _compareMakeupDb = 0;
                _compareTool = null;
                _compareToolWasInserted = false;
                CompareInsertsToolHint = string.Empty;
                CompareBypassActive = false;
            }
            _commit();
            _changed();
            Rebuild();
            OnPropertyChanged(nameof(CompareBypassActive));
            OnPropertyChanged(nameof(CompareInsertsToolHint));
            OnPropertyChanged(nameof(CompareMakeupText));
            OnPropertyChanged(nameof(CompareMakeupSettled));
        }

        private ToolEffect EnsureCompareTool(out bool inserted)
        {
            var tool = _effects.OfType<ToolEffect>().LastOrDefault();
            inserted = tool is null;
            if (tool is not null) return tool;
            tool = new ToolEffect();
            _effects.Add(tool);
            return tool;
        }

        private void ResetLoudness()
        {
            _engine?.ResetMasterLoudness();
            _lufsHistory.Clear();
            OnPropertyChanged(nameof(MasteringMeterText));
            OnPropertyChanged(nameof(NormalizePreviewText));
            OnPropertyChanged(nameof(ComplianceSummaryText));
            OnPropertyChanged(nameof(PeakHoldText));
            OnPropertyChanged(nameof(LufsHistoryPoints));
        }

        private void SyncLimiterCeilingToDelivery()
        {
            if (_deliveryTarget is null) return;
            var limiters = _effects.OfType<PeakLimiterEffect>().ToList();
            if (limiters.Count == 0) return;
            _history.Capture("Sync limiter ceiling to delivery target");
            foreach (var lim in limiters)
                lim.CeilingDb = _deliveryTarget.TargetTruePeakDbTp;
            _commit();
            _changed();
            Rebuild();
            OnPropertyChanged(nameof(LimiterCeilingMismatch));
            OnPropertyChanged(nameof(LimiterCeilingMismatchText));
        }

        private async Task RunPreflightAnalyseAsync()
        {
            var export = App.ServiceProvider?.GetService<ExportService>();
            var project = App.ServiceProvider?.GetService<IProjectService>();
            if (export is null || project is null || _engine is null)
            {
                PreflightReportText = Loc.Get("Mastering_Preflight_Unavailable", "Preflight analysis unavailable.");
                OnPropertyChanged(nameof(PreflightReportText));
                return;
            }

            PreflightReportText = Loc.Get("Mastering_Preflight_Running", "Analysing…");
            OnPropertyChanged(nameof(PreflightReportText));
            try
            {
                var targetLufs = _deliveryTarget?.TargetIntegratedLufs ?? -14;
                var targetTp = _deliveryTarget?.TargetTruePeakDbTp ?? -1;
                var platform = _deliveryTarget?.PlatformName;
                var bpm = _transport.Tempo.BeatsPerMinute;
                var format = _engine.Format;
                var report = await Task.Run(() =>
                {
                    var path = Path.Combine(Path.GetTempPath(), $"ongenet-preflight-{Guid.NewGuid():N}.wav");
                    try
                    {
                        var options = new ExportOptions
                        {
                            BitDepth = 24,
                            AnalyzeLoudness = true,
                            NormalizeLoudness = false,
                            TargetIntegratedLufs = targetLufs,
                            TargetTruePeakDbTp = targetTp,
                            DeliveryPlatform = platform
                        };
                        export.Export(project.Current, format, bpm, path, options);
                        if (options.LoudnessReport is { } lr)
                        {
                            return Loc.Get("Mastering_Preflight_Result",
                                "I {0:0.0} LUFS · LRA {1:0.0} LU · TP {2:0.0} dBTP (target {3:0.#}/{4:0.0}) · LUFS {5} · TP {6}",
                                lr.IntegratedLufs, lr.LoudnessRangeLu, lr.TruePeakDbTp,
                                targetLufs, targetTp,
                                Math.Abs(lr.IntegratedLufs - targetLufs) <= 1.0
                                    ? Loc.Get("Mastering_Compliance_Pass", "PASS")
                                    : Loc.Get("Mastering_Compliance_Fail", "FAIL"),
                                lr.TruePeakDbTp <= targetTp + 0.05
                                    ? Loc.Get("Mastering_Compliance_Pass", "PASS")
                                    : Loc.Get("Mastering_Compliance_Fail", "FAIL"));
                        }
                        return Loc.Get("Mastering_Preflight_NoSidecar", "Export completed but loudness report was missing.");
                    }
                    finally
                    {
                        try { File.Delete(path); } catch { /* ignore */ }
                        try { File.Delete(path + ".loudness.json"); } catch { /* ignore */ }
                        try { File.Delete(path + ".loudness.txt"); } catch { /* ignore */ }
                    }
                });
                PreflightReportText = report;
            }
            catch (Exception ex)
            {
                PreflightReportText = Loc.Get("Mastering_Preflight_Failed", "Preflight failed: {0}", ex.Message);
            }
            OnPropertyChanged(nameof(PreflightReportText));
        }

        private void SuggestLufsAssist()
        {
            var integrated = _engine?.MasterIntegratedLufs ?? double.NegativeInfinity;
            if (double.IsNegativeInfinity(integrated))
            {
                LufsAssistSuggestionText = Loc.Get("Mastering_LufsAssist_NeedPlay", "Play the project to suggest LUFS assist.");
                OnPropertyChanged(nameof(LufsAssistSuggestionText));
                return;
            }
            var target = _deliveryTarget?.TargetIntegratedLufs ?? -14;
            var delta = target - integrated;
            var lim = _effects.OfType<PeakLimiterEffect>().LastOrDefault();
            if (lim is not null)
            {
                var suggested = lim.ThresholdDb + delta;
                LufsAssistSuggestionText = Loc.Get("Mastering_LufsAssist_Limiter",
                    "ΔI {0:+0.0;-0.0;0.0} LU — try Peak Limiter threshold {1:0.0} → {2:0.0} dB",
                    delta, lim.ThresholdDb, suggested);
            }
            else
            {
                LufsAssistSuggestionText = Loc.Get("Mastering_LufsAssist_Tool",
                    "ΔI {0:+0.0;-0.0;0.0} LU — add a Tool gain of about that amount before the limiter",
                    delta);
            }
            OnPropertyChanged(nameof(LufsAssistSuggestionText));
        }

        private void ToggleMono()
        {
            var tool = _effects.OfType<ToolEffect>().LastOrDefault();
            _history.Capture("Toggle mono audition");
            if (tool is null)
            {
                tool = new ToolEffect { Mono = true };
                _effects.Add(tool);
            }
            else tool.Mono = !tool.Mono;
            _commit();
            _changed();
            Rebuild();
            OnPropertyChanged(nameof(MonoAuditionActive));
        }

        private void SelectWorkflowStep(WorkflowStepItem? step)
        {
            if (step is null) return;
            var recipe = MasteringChains.Create(SelectedMasteringChain.Key);
            var index = Math.Clamp(step.Index, 0, Math.Max(0, recipe.Length - 1));
            var typeId = recipe.Length == 0 ? string.Empty : recipe[index].TypeId;
            HighlightedEffectIndex = -1;
            for (var i = 0; i < Effects.Count; i++)
            {
                var match = Effects[i].Effect.TypeId == typeId;
                Effects[i].IsHighlighted = match;
                if (match && HighlightedEffectIndex < 0)
                    HighlightedEffectIndex = i;
            }
            OnPropertyChanged(nameof(HighlightedEffectIndex));
        }

        private void UpdateCompareCompensation()
        {
            if (!CompareBypassActive || _compareTool is null || _engine is null ||
                double.IsNegativeInfinity(_compareTargetLufs)) return;
            var current = _engine.MasterShortTermLufs;
            if (double.IsNegativeInfinity(current)) return;
            var error = _compareTargetLufs - current;
            if (Math.Abs(error) <= 0.5) return;
            var adjustment = Math.Clamp(error, -1.0, 1.0);
            _compareMakeupDb = Math.Clamp(_compareMakeupDb + adjustment, -18, 18);
            _compareTool.GainDb = _compareToolOriginalGain + _compareMakeupDb;
        }

        private static string FormatAbDetail(AbSlotInfo? info)
        {
            if (info is null) return string.Empty;
            var lufs = double.IsNegativeInfinity(info.IntegratedLufs) ? "—" : $"{info.IntegratedLufs:0.0} LUFS";
            return $"{info.Name} · {info.StoredAt:HH:mm:ss} · {lufs}";
        }

        private void CaptureAb(ref List<IAudioEffect>? slot, ref AbSlotInfo? info, string name)
        {
            slot = _effects.Select(e => e.Clone()).ToList();
            info = new AbSlotInfo(name, DateTime.Now,
                _engine?.MasterIntegratedLufs ?? double.NegativeInfinity, string.Empty);
            OnPropertyChanged(nameof(AbLabel));
            OnPropertyChanged(nameof(HasAbSlotA));
            OnPropertyChanged(nameof(HasAbSlotB));
            OnPropertyChanged(nameof(AbSlotALabel));
            OnPropertyChanged(nameof(AbSlotBLabel));
            OnPropertyChanged(nameof(AbSlotADetail));
            OnPropertyChanged(nameof(AbSlotBDetail));
            ToggleAbCommand.RaiseCanExecuteChanged();
        }

        private void ToggleAb()
        {
            if (_abSlotA is null || _abSlotB is null) return;
            _abIsB = !_abIsB;
            var src = _abIsB ? _abSlotB : _abSlotA;
            _history.Capture(_abIsB ? "Master A/B → B" : "Master A/B → A");
            _effects.Clear();
            foreach (var effect in src) _effects.Add(effect.Clone());
            if (_abLoudnessMatch)
            {
                var fromInfo = _abIsB ? _abInfoA : _abInfoB;
                var toInfo = _abIsB ? _abInfoB : _abInfoA;
                if (fromInfo is not null && toInfo is not null
                    && !double.IsNegativeInfinity(fromInfo.IntegratedLufs)
                    && !double.IsNegativeInfinity(toInfo.IntegratedLufs))
                {
                    var delta = fromInfo.IntegratedLufs - toInfo.IntegratedLufs;
                    var tool = _effects.OfType<ToolEffect>().LastOrDefault();
                    if (tool is null)
                    {
                        tool = new ToolEffect();
                        _effects.Add(tool);
                    }
                    tool.GainDb += delta;
                }
            }
            _commit();
            _changed();
            Rebuild();
            OnPropertyChanged(nameof(AbIsB));
            OnPropertyChanged(nameof(AbLabel));
        }

        public async Task LoadReferenceAsync(string path, int slot = 0)
        {
            var files = App.ServiceProvider?.GetService<IAudioFileService>();
            if (files is null || string.IsNullOrWhiteSpace(path)) return;
            var loaded = await Task.Run(() => files.Load(path));
            if (loaded is null) return;

            var source = loaded.Samples;
            var meter = new LoudnessMeter();
            meter.Prepare(new AudioFormat(source.SampleRate, source.Channels));
            meter.Process(source.Samples);
            var referenceLufs = meter.IntegratedLufs;
            var target = _deliveryTarget?.TargetIntegratedLufs
                ?? _engine?.MasterIntegratedLufs
                ?? -14;
            if (double.IsNegativeInfinity(target)) target = -14;
            var matchDb = double.IsNegativeInfinity(referenceLufs) ? 0 : Math.Clamp(target - referenceLufs, -24, 24);
            var gain = (float)Math.Pow(10, (matchDb + _referenceGainDb) / 20);
            var samples = (float[])source.Samples.Clone();
            for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
            var buffer = new AudioSampleBuffer(samples, source.Channels, source.SampleRate);
            if (slot == 1)
            {
                _referenceB = buffer;
                ReferenceNameB = Path.GetFileName(path);
                OnPropertyChanged(nameof(HasReferenceB));
                OnPropertyChanged(nameof(ReferenceNameB));
            }
            else
            {
                _reference = buffer;
                ReferenceName = Path.GetFileName(path);
                ReferenceMatchText = $"{referenceLufs:0.0} → {target:0.0} LUFS ({matchDb:+0.0;-0.0;0.0} dB)";
                ReferenceSpectrumPoints = BuildSpectrum(_reference.Samples, _reference.Channels);
                OnPropertyChanged(nameof(HasReference));
                OnPropertyChanged(nameof(ReferenceName));
                OnPropertyChanged(nameof(ReferenceMatchText));
                OnPropertyChanged(nameof(ReferenceSpectrumPoints));
            }
            OnPropertyChanged(nameof(ActiveReferenceName));
            CaptureReferenceToMatchEqCommand.RaiseCanExecuteChanged();
            ToggleReferenceLatchCommand.RaiseCanExecuteChanged();
        }

        private void ToggleReferenceLatch()
        {
            if (ReferenceLatched)
            {
                StopReferenceAudition();
                ReferenceLatched = false;
            }
            else
            {
                StartReferenceAudition();
                ReferenceLatched = true;
            }
        }

        private AudioSampleBuffer? ActiveReferenceBuffer =>
            ActiveReferenceSlot == 1 && _referenceB is not null ? _referenceB : _reference;

        private void CaptureReferenceToMatchEq()
        {
            if (ActiveReferenceBuffer is not { } reference) return;
            _history.Capture("Capture reference to Match EQ");
            var matchEq = _effects.OfType<MatchEqEffect>().FirstOrDefault();
            if (matchEq is null)
            {
                matchEq = new MatchEqEffect();
                // Insert before first Peak Limiter when possible.
                var limIdx = _effects.FindIndex(e => e is PeakLimiterEffect);
                if (limIdx >= 0) _effects.Insert(limIdx, matchEq);
                else _effects.Add(matchEq);
            }
            matchEq.CaptureTargetFrom(reference.Samples, reference.Channels, reference.SampleRate);
            _commit();
            _changed();
            Rebuild();
        }

        public void StartReferenceAudition()
        {
            if (ActiveReferenceBuffer is not { } reference) return;
            var player = App.ServiceProvider?.GetService<IAuditionPlayer>();
            if (player is null) return;

            var startSeconds = 0.0;
            if (ReferenceAuditionSynced)
            {
                var bpm = _transport.Tempo.BeatsPerMinute;
                if (bpm > 0)
                    startSeconds = _transport.PlayheadBeats * 60.0 / bpm;
                var duration = reference.SampleRate > 0
                    ? reference.FrameCount / (double)reference.SampleRate
                    : 0;
                if (duration > 0)
                    startSeconds = Math.Clamp(startSeconds, 0, Math.Max(0, duration - 0.001));
            }

            player.Play(reference, startSeconds);
        }

        public void StopReferenceAudition()
        {
            App.ServiceProvider?.GetService<IAuditionPlayer>()?.Stop();
            if (ReferenceLatched) ReferenceLatched = false;
        }

        private void UpdateLiveSpectrum()
        {
            if (ActiveReferenceBuffer is null) return;
            float[]? samples = null;
            var channels = 1;
            if (_effects.OfType<SpectrumEffect>().LastOrDefault() is { } spectrum)
            {
                var count = spectrum.CaptureLatest(_liveSpectrumSamples);
                if (count >= 2)
                {
                    samples = _liveSpectrumSamples;
                    channels = 1;
                }
            }
            if (samples is null && _engine is not null)
            {
                // Fall back to a simple peak-derived spectrum placeholder using reference vs correlation.
                return;
            }
            if (samples is null) return;
            LiveSpectrumPoints = BuildSpectrum(samples.AsSpan(0, Math.Min(samples.Length, 2048)), channels);
            OnPropertyChanged(nameof(LiveSpectrumPoints));
        }

        private static Points BuildSpectrum(ReadOnlySpan<float> interleaved, int channels)
        {
            var points = new Points();
            channels = Math.Max(1, channels);
            var frames = Math.Min(2048, interleaved.Length / channels);
            if (frames < 2) return points;
            const int bins = 48;
            Span<double> binDb = stackalloc double[bins];
            for (var bin = 0; bin < bins; bin++)
            {
                // Log-spaced DFT bins for perceptual overlay.
                var t = (bin + 0.5) / bins;
                var k = 1 + (int)((Math.Pow(10, t) - 1) / 9.0 * (frames / 2 - 1));
                k = Math.Clamp(k, 1, frames / 2 - 1);
                double re = 0, im = 0;
                for (var n = 0; n < frames; n++)
                {
                    // Stereo mid average when available.
                    float sample;
                    if (channels >= 2)
                        sample = 0.5f * (interleaved[n * channels] + interleaved[n * channels + 1]);
                    else
                        sample = interleaved[n * channels];
                    var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (frames - 1));
                    var phase = 2 * Math.PI * k * n / frames;
                    re += sample * window * Math.Cos(phase);
                    im -= sample * window * Math.Sin(phase);
                }
                binDb[bin] = 20 * Math.Log10(Math.Sqrt(re * re + im * im) / frames + 1e-9);
            }
            // Simple 3-tap smoothing for Match-EQ-like display.
            for (var bin = 0; bin < bins; bin++)
            {
                var a = bin > 0 ? binDb[bin - 1] : binDb[bin];
                var b = binDb[bin];
                var c = bin < bins - 1 ? binDb[bin + 1] : binDb[bin];
                var db = 0.25 * a + 0.5 * b + 0.25 * c;
                var y = Math.Clamp(60 - (db + 60) * 0.9, 4, 60);
                points.Add(new Point(bin * (240.0 / (bins - 1)), y));
            }
            return points;
        }

        private void RemoveEffect(EffectViewModel vm)
        {
            _history.Capture("Remove effect");
            _effects.Remove(vm.Effect);
            _commit();
            _changed();
            Rebuild();
        }

        private void MoveEffect(EffectViewModel vm, int delta)
        {
            var index = _effects.IndexOf(vm.Effect);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _effects.Count) return;
            _history.Capture("Reorder effect");

            _effects.RemoveAt(index);
            _effects.Insert(target, vm.Effect);
            _commit();
            _changed();
            Rebuild();
        }

        private void MoveUp(EffectViewModel vm) => MoveEffect(vm, -1);
        private void MoveDown(EffectViewModel vm) => MoveEffect(vm, +1);

        private void Rebuild()
        {
            Effects.Clear();
            foreach (var effect in _effects)
            {
                Effects.Add(effect switch
                {
                    FieldEffect fe => new FieldEffectViewModel(fe, RemoveEffect, MoveUp, MoveDown),
                    EqEffect eq => new EqEffectViewModel(eq, RemoveEffect, MoveUp, MoveDown),
                    FilterEffect filter => new FilterEffectViewModel(filter, RemoveEffect, MoveUp, MoveDown),
                    SidechainEffect sc => new SidechainEffectViewModel(sc, RemoveEffect, MoveUp, MoveDown),
                    CompressorEffect comp => new CompressorEffectViewModel(comp, RemoveEffect, MoveUp, MoveDown)
                    {
                        HideSidechainPicker = _isMasterTrack
                    },
                    LiveDifferenceEffect ld => new LiveDifferenceEffectViewModel(ld, RemoveEffect, MoveUp, MoveDown),
                    StutteroEffect st => new StutteroEffectViewModel(st, RemoveEffect, MoveUp, MoveDown),
                    VocoderEffect vc => new VocoderEffectViewModel(vc, RemoveEffect, MoveUp, MoveDown),
                    WaveformVisualizerEffect wv => new WaveformVisualizerEffectViewModel(wv, RemoveEffect, MoveUp, MoveDown),
                    SpectrumEffect sp => new SpectrumEffectViewModel(sp, RemoveEffect, MoveUp, MoveDown),
                    ToolEffect tool => new ToolEffectViewModel(tool, RemoveEffect, MoveUp, MoveDown),
                    LoudnessMeterEffect lm => new LoudnessMeterEffectViewModel(lm, RemoveEffect, MoveUp, MoveDown),
                    TunerEffect tu => new TunerEffectViewModel(tu, RemoveEffect, MoveUp, MoveDown),
                    ConvolutionEffect cv => new ConvolutionEffectViewModel(cv, RemoveEffect, MoveUp, MoveDown),
                    MidSideEqEffect ms => new MidSideEqEffectViewModel(ms, RemoveEffect, MoveUp, MoveDown),
                    MatchEqEffect match => new MatchEqEffectViewModel(match, RemoveEffect, MoveUp, MoveDown),
                    LinearPhaseEqEffect linear => new LinearPhaseEqEffectViewModel(linear, RemoveEffect, MoveUp, MoveDown),
                    MultibandCompressorEffect mb => new MultibandCompressorEffectViewModel(mb, RemoveEffect, MoveUp, MoveDown),
                    ClipperEffect clip => new ClipperEffectViewModel(clip, RemoveEffect, MoveUp, MoveDown),
                    StereoWidthEffect sw => new StereoWidthEffectViewModel(sw, RemoveEffect, MoveUp, MoveDown),
                    PeakLimiterEffect lim => new PeakLimiterEffectViewModel(lim, RemoveEffect, MoveUp, MoveDown,
                        () => _deliveryTarget?.TargetTruePeakDbTp ?? -1),
                    _ => new EffectViewModel(effect, RemoveEffect, MoveUp, MoveDown)
                });
            }

            for (var i = 0; i < Effects.Count; i++)
            {
                Effects[i].Position = i + 1;
                Effects[i].IsFirst = i == 0;
                Effects[i].IsLast = i == Effects.Count - 1;
            }

            var detected = MasteringChainOptions.FirstOrDefault(option =>
            {
                var ids = MasteringChains.TypeIds(option.Key);
                if (ids.Length != _effects.Count) return false;
                for (var i = 0; i < ids.Length; i++)
                {
                    if (!string.Equals(ids[i], _effects[i].TypeId, StringComparison.Ordinal))
                        return false;
                }
                return true;
            });
            if (detected is not null && !ReferenceEquals(_selectedMasteringChain, detected))
            {
                _selectedMasteringChain = detected;
                OnPropertyChanged(nameof(SelectedMasteringChain));
            }

            OnPropertyChanged(nameof(HasEffects));
            BypassAllCommand.RaiseCanExecuteChanged();
            RestoreBypassCommand.RaiseCanExecuteChanged();
            ToggleCompareBypassCommand.RaiseCanExecuteChanged();
            ToggleAbCommand.RaiseCanExecuteChanged();
            CaptureReferenceToMatchEqCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ActiveWorkflowTitle));
            OnPropertyChanged(nameof(ActiveWorkflowDescription));
            OnPropertyChanged(nameof(WorkflowSteps));
            OnPropertyChanged(nameof(WorkflowStepItems));
            OnPropertyChanged(nameof(WorkflowStepsText));
            OnPropertyChanged(nameof(MonoAuditionActive));
            OnPropertyChanged(nameof(LimiterCeilingMismatch));
            OnPropertyChanged(nameof(LimiterCeilingMismatchText));
            RebuildSignalFlowStages();
        }

        private void RebuildSignalFlowStages()
        {
            SignalFlowStages.Clear();
            foreach (var fx in Effects)
            {
                var gr = fx.Effect is IGainReductionSource grs ? grs.GainReductionDb : 0;
                SignalFlowStages.Add(new SignalFlowStageItem(
                    fx.Position,
                    ShortStageName(fx.Effect),
                    fx.Effect.Enabled,
                    Math.Clamp(gr / 12.0, 0, 1),
                    fx.IsHighlighted));
            }
        }

        private void RebuildSignalFlowLevels()
        {
            if (!_isMasterTrack || SignalFlowStages.Count == 0) return;
            for (var i = 0; i < Effects.Count && i < SignalFlowStages.Count; i++)
            {
                var gr = Effects[i].Effect is IGainReductionSource grs ? grs.GainReductionDb : 0;
                var stage = SignalFlowStages[i];
                if (Math.Abs(stage.GrNormalized - Math.Clamp(gr / 12.0, 0, 1)) > 0.01
                    || stage.Enabled != Effects[i].Effect.Enabled
                    || stage.Highlighted != Effects[i].IsHighlighted)
                {
                    SignalFlowStages[i] = stage with
                    {
                        GrNormalized = Math.Clamp(gr / 12.0, 0, 1),
                        Enabled = Effects[i].Effect.Enabled,
                        Highlighted = Effects[i].IsHighlighted
                    };
                }
            }
        }

        private static string ShortStageName(IAudioEffect effect) => effect switch
        {
            EqEffect => "EQ",
            LinearPhaseEqEffect => "LP-EQ",
            MidSideEqEffect => "M/S",
            MatchEqEffect => "Match",
            MultibandCompressorEffect => "MB",
            CompressorEffect => "Comp",
            StereoWidthEffect => "Width",
            ClipperEffect => "Clip",
            PeakLimiterEffect => "Limit",
            SpectrumEffect => "Spec",
            LoudnessMeterEffect => "LUFS",
            ToolEffect => "Tool",
            _ => effect.Name.Length > 8 ? effect.Name[..8] : effect.Name
        };

        private void RebuildMasteringChainOptions()
        {
            var selectedKey = SelectedMasteringChain.Key;
            MasteringChainOptions = CreateMasteringChainOptions();
            _selectedMasteringChain = MasteringChainOptions.FirstOrDefault(x => x.Key == selectedKey)
                ?? MasteringChainOptions[0];
            OnPropertyChanged(nameof(MasteringChainOptions));
            OnPropertyChanged(nameof(SelectedMasteringChain));
        }

        private IReadOnlyList<MasteringChainOption> CreateMasteringChainOptions()
        {
            static string L(string key, string fallback) => Loc.Get(key, fallback);
            static string[] Steps(params string[] steps) => steps;
            List<MasteringChainOption> builtIn =
            [
                new("full", L("Mastering_Workflow_Full_Title", "Full Master"),
                    L("Mastering_ChainDesc_Full", "Balanced streaming master: EQ → M/S → glue → width → clip → Peak Limiter (−1 dBTP)"),
                    Steps(L("Mastering_Workflow_1", "1. Corrective EQ"), L("Mastering_Workflow_2", "2. Mid/Side EQ"),
                        L("Mastering_Workflow_3", "3. Glue compressor"), L("Mastering_Workflow_4", "4. Stereo width"),
                        L("Mastering_Workflow_5", "5. Clipper"), L("Mastering_Workflow_6", "6. Peak Limiter"),
                        L("Mastering_Workflow_7", "7. Spectrum"))),
                new("full+", L("Mastering_Workflow_FullPlus_Title", "Full Master+"),
                    L("Mastering_ChainDesc_FullPlus", "Full Master with Multiband OTT for dense electronic mixes"),
                    Steps(L("Mastering_Workflow_FullPlus_1", "1. Corrective EQ"), L("Mastering_Workflow_FullPlus_2", "2. Mid/Side EQ"), L("Mastering_Workflow_FullPlus_3", "3. Glue compressor"), L("Mastering_Workflow_FullPlus_4", "4. Multiband"), L("Mastering_Workflow_FullPlus_5", "5. Stereo width"), L("Mastering_Workflow_FullPlus_6", "6. Clipper"), L("Mastering_Workflow_FullPlus_7", "7. Peak Limiter"), L("Mastering_Workflow_FullPlus_8", "8. Spectrum"))),
                new("streaming", L("Mastering_Workflow_Streaming_Title", "Streaming Master"),
                    L("Mastering_ChainDesc_Streaming", "Light processing for Spotify/YouTube — no clipper"),
                    Steps(L("Mastering_Workflow_Streaming_1", "1. Corrective EQ"), L("Mastering_Workflow_Streaming_2", "2. Gentle glue"), L("Mastering_Workflow_Streaming_3", "3. Streaming limiter"), L("Mastering_Workflow_Streaming_4", "4. Spectrum"))),
                new("premaster", L("Mastering_Workflow_Premaster_Title", "Pre-master"),
                    L("Mastering_ChainDesc_Premaster", "Cleanup only (no limiter) for sending to a mastering engineer"),
                    Steps(L("Mastering_Workflow_Premaster_1", "1. DC offset removal"), L("Mastering_Workflow_Premaster_2", "2. Corrective EQ"), L("Mastering_Workflow_Premaster_3", "3. Mid/Side balance"), L("Mastering_Workflow_Premaster_4", "4. Gentle glue"))),
                new("club", L("Mastering_Workflow_Club_Title", "Club Loud"),
                    L("Mastering_ChainDesc_Club", "Hot club delivery (~−9 LUFS / −0.3 dBTP)"),
                    Steps(L("Mastering_Workflow_Club_1", "1. Multiband control"), L("Mastering_Workflow_Club_2", "2. Stereo width"), L("Mastering_Workflow_Club_3", "3. Saturation"), L("Mastering_Workflow_Club_4", "4. Clipper"), L("Mastering_Workflow_Club_5", "5. Loud limiter"))),
                new("podcast", L("Mastering_Workflow_Podcast_Title", "Podcast"),
                    L("Mastering_ChainDesc_Podcast", "Speech-focused chain with de-esser and safety limiter"),
                    Steps(L("Mastering_Workflow_Podcast_1", "1. High-pass filter"), L("Mastering_Workflow_Podcast_2", "2. De-esser"), L("Mastering_Workflow_Podcast_3", "3. Speech compression"), L("Mastering_Workflow_Podcast_4", "4. Safety limiter"))),
                new("glue", L("Mastering_Workflow_Glue_Title", "Master Glue"),
                    L("Mastering_ChainDesc_Glue", "Minimal glue + width + loud limiter"),
                    Steps(L("Mastering_Workflow_Glue_1", "1. Glue compressor"), L("Mastering_Workflow_Glue_2", "2. Stereo width"), L("Mastering_Workflow_Glue_3", "3. Peak limiter"))),
                new("techno", L("Mastering_Workflow_Techno_Title", "Techno Master"),
                    L("Mastering_ChainDesc_Techno", "HPF + multiband + exciter for techno/warehouse loudness"),
                    Steps(L("Mastering_Workflow_Techno_1", "1. High-pass filter"), L("Mastering_Workflow_Techno_2", "2. Multiband control"), L("Mastering_Workflow_Techno_3", "3. Stereo width"), L("Mastering_Workflow_Techno_4", "4. Exciter"), L("Mastering_Workflow_Techno_5", "5. Peak limiter"))),
                new("audiophile", L("Mastering_Workflow_Audiophile_Title", "Audiophile Master"),
                    L("Mastering_ChainDesc_Audiophile", "Linear-phase EQ variant — higher latency, experimental"),
                    Steps(L("Mastering_Workflow_Audiophile_1", "1. Linear-phase EQ"), L("Mastering_Workflow_Audiophile_2", "2. Mid/Side EQ"), L("Mastering_Workflow_Audiophile_3", "3. Glue compressor"), L("Mastering_Workflow_Audiophile_4", "4. Stereo width"), L("Mastering_Workflow_Audiophile_5", "5. Clipper"), L("Mastering_Workflow_Audiophile_6", "6. Peak limiter"), L("Mastering_Workflow_Audiophile_7", "7. Spectrum"))),
                new("reference", L("Mastering_Workflow_Reference_Title", "Reference Master"),
                    L("Mastering_ChainDesc_Reference", "EQ → Match EQ → glue → Peak Limiter — capture a reference first"),
                    Steps(L("Mastering_Workflow_Reference_1", "1. Corrective EQ"), L("Mastering_Workflow_Reference_2", "2. Match EQ"), L("Mastering_Workflow_Reference_3", "3. Glue compressor"), L("Mastering_Workflow_Reference_4", "4. Peak Limiter"), L("Mastering_Workflow_Reference_5", "5. Spectrum")))
            ];

            if (_presetLibrary is null) return builtIn;
            var factoryRoot = Path.GetFullPath(AppPaths.FactoryPresetsDirectory())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var userChains = _presetLibrary.ChainPresets
                .SelectMany(group => group.Items)
                .Where(item => !Path.GetFullPath(item.FullPath).StartsWith(factoryRoot,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => new MasteringChainOption(
                    $"user:{item.FullPath}",
                    item.Name,
                    L("Mastering_ChainDesc_User", "Saved FX chain"),
                    Steps(L("Mastering_Workflow_User", "User preset chain"))));
            builtIn.AddRange(userChains);
            return builtIn;
        }
    }

    public sealed record SignalFlowStageItem(int Position, string Name, bool Enabled, double GrNormalized, bool Highlighted);
}
