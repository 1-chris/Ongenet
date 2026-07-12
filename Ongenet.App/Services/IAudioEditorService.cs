using Ongenet.Core.Models.Audio;

namespace Ongenet.App.Services;

/// <summary>Opens the standalone Audio Editor window for one or more clips.</summary>
public interface IAudioEditorService
{
    void Open();
    void OpenClip(Clip clip);
}
