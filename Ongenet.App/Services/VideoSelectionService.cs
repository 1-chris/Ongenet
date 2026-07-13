using System;
using System.Linq;
using Ongenet.Core.Models.Media;

namespace Ongenet.App.Services;

public sealed class VideoSelectionService : IVideoSelectionService
{
    private VideoLayer? _layer;
    private VideoLayerItem? _item;
    private VideoTrigger? _trigger;
    private VideoVisibilityRegion? _region;

    public event Action? SelectionChanged;

    public VideoLayer? SelectedLayer
    {
        get => _layer;
        set
        {
            if (_layer == value) return;
            _layer = value;
            if (value is null)
                _item = null;
            else if (_item is not null && !value.Items.Contains(_item))
                _item = value.Items.FirstOrDefault();
            SelectionChanged?.Invoke();
        }
    }

    public VideoLayerItem? SelectedLayerItem
    {
        get => _item;
        set
        {
            if (_item == value) return;
            _item = value;
            SelectionChanged?.Invoke();
        }
    }

    public VideoTrigger? SelectedTrigger
    {
        get => _trigger;
        set
        {
            if (_trigger == value) return;
            _trigger = value;
            SelectionChanged?.Invoke();
        }
    }

    public VideoVisibilityRegion? SelectedVisibilityRegion
    {
        get => _region;
        set
        {
            if (_region == value) return;
            _region = value;
            SelectionChanged?.Invoke();
        }
    }

    public void Clear()
    {
        _layer = null;
        _item = null;
        _trigger = null;
        _region = null;
        SelectionChanged?.Invoke();
    }
}
