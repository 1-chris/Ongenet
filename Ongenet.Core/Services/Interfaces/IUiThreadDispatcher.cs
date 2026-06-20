using System;

namespace Ongenet.Core.Services.Interfaces;

/// <summary>
/// Marshals an action onto the UI thread. A seam so Core services (which must not reference Avalonia)
/// can hand UI-affecting notifications back to the UI thread; the desktop host supplies the
/// dispatcher implementation. When none is registered, callers run the action synchronously.
/// </summary>
public interface IUiThreadDispatcher
{
    /// <summary>Posts <paramref name="action"/> to run on the UI thread (non-blocking).</summary>
    void Post(Action action);
}
