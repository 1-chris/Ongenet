using System;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Core.Services.Implementation;

/// <summary>Default <see cref="IEditModeService"/>.</summary>
public class EditModeService : IEditModeService
{
    private EditMode _mode = EditMode.Edit;

    public EditMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            ModeChanged?.Invoke();
        }
    }

    public event Action? ModeChanged;
}
