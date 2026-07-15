using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ongenet.App.Localization;

namespace Ongenet.App.ViewModels;

/// <summary>In-app guide — localized topics with optional external resource links.</summary>
public sealed class GuideViewModel : ViewModelBase
{
    public GuideViewModel(ILocalizationService localization)
    {
        _localization = localization;
        _localization.CultureChanged += RebuildSections;
        RebuildSections();
    }

    private readonly ILocalizationService _localization;

    public ObservableCollection<GuideSectionViewModel> Sections { get; } = new();

    private GuideSectionViewModel _selected = null!;
    public GuideSectionViewModel Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    private void RebuildSections()
    {
        var previousTitle = _selected?.Title;
        Sections.Clear();
        foreach (var section in GuideContent.Build())
            Sections.Add(section);

        Selected = previousTitle is not null
            ? Sections.FirstOrDefault(s => s.Title == previousTitle) ?? Sections[0]
            : Sections[0];
    }
}

public sealed record GuideLink(string Label, string Url);

public sealed class GuideSectionViewModel
{
    public GuideSectionViewModel(string title, string body, string? linksHeading = null,
        IReadOnlyList<GuideLink>? links = null)
    {
        Title = title;
        Body = body;
        LinksHeading = linksHeading ?? string.Empty;
        Links = links ?? [];
    }

    public string Title { get; }
    public string Body { get; }
    public string LinksHeading { get; }
    public IReadOnlyList<GuideLink> Links { get; }
    public bool HasLinks => Links.Count > 0;
}

internal static class GuideContent
{
    internal static GuideSectionViewModel[] Build() =>
    [
        new(L("Guide_GettingStarted_Title"), L("Guide_GettingStarted_Body")),
        Samples(),
        Plugins(),
        new(L("Guide_Sidechain_Title"), L("Guide_Sidechain_Body")),
        new(L("Guide_Production_Title"), L("Guide_Production_Body")),
        new(L("Guide_Timeline_Title"), L("Guide_Timeline_Body")),
        new(L("Guide_Session_Title"), L("Guide_Session_Body")),
        new(L("Guide_Field_Title"), L("Guide_Field_Body")),
        new(L("Guide_Mixer_Title"), L("Guide_Mixer_Body")),
        new(L("Guide_Mastering_Title"), L("Guide_Mastering_Body")),
        new(L("Guide_AudioEditor_Title"), L("Guide_AudioEditor_Body")),
        new(L("Guide_PitchEditor_Title"), L("Guide_PitchEditor_Body")),
        new(L("Guide_Scripting_Title"), L("Guide_Scripting_Body")),
        new(L("Guide_Shortcuts_Title"), L("Guide_Shortcuts_Body")),
    ];

    private static GuideSectionViewModel Samples() =>
        new(L("Guide_Samples_Title"), L("Guide_Samples_Body"), L("Guide_Links_Heading"),
        [
            new(L("Guide_Samples_Link_Freesound"), "https://freesound.org"),
            new(L("Guide_Samples_Link_Bbc"), "https://sound-effects.bbcrewind.co.uk"),
            new(L("Guide_Samples_Link_99Sounds"), "https://99sounds.org"),
            new(L("Guide_Samples_Link_Looperman"), "https://www.looperman.com"),
            new(L("Guide_Samples_Link_PianoBook"), "https://www.pianobook.co.uk"),
            new(L("Guide_Samples_Link_SampleFocus"), "https://samplefocus.com"),
        ]);

    private static GuideSectionViewModel Plugins() =>
        new(L("Guide_Plugins_Title"), L("Guide_Plugins_Body"), L("Guide_Links_Heading"),
        [
            new(L("Guide_Plugins_Link_Kvr"), "https://www.kvraudio.com/plugins/"),
            new(L("Guide_Plugins_Link_P4f"), "https://plugins4free.com"),
            new(L("Guide_Plugins_Link_Surge"), "https://surge-synthesizer.github.io"),
            new(L("Guide_Plugins_Link_Chowdsp"), "https://chowdsp.com"),
            new(L("Guide_Plugins_Link_Dragonfly"), "https://michaelwillis.github.io/dragonfly-reverb/"),
            new(L("Guide_Plugins_Link_Lsp"), "https://lsp-plug.in"),
            new(L("Guide_Plugins_Link_Vital"), "https://vital.audio"),
        ]);

    private static string L(string key) => Loc.Get(key);
}
