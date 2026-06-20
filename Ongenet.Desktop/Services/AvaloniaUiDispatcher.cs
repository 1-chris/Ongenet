using System;
using Avalonia.Threading;
using Ongenet.Core.Services.Interfaces;

namespace Ongenet.Desktop.Services;

/// <summary>
/// Avalonia-backed <see cref="IUiThreadDispatcher"/>: posts work onto the UI thread so Core services
/// (which must not reference Avalonia) can hand UI-affecting notifications back safely.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiThreadDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
