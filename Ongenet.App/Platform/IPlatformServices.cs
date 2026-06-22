using System;
using Microsoft.Extensions.DependencyInjection;

namespace Ongenet.App.Platform;

/// <summary>
/// The seam each application head (desktop, browser) implements to plug its platform-specific pieces
/// into the shared <see cref="App"/>. The shared App owns everything portable — the engine wiring, the
/// view-models, theming, automation, history — and defers to this for the parts that differ per host:
/// which audio backend(s) exist, the MIDI source, optional plugin hosts, and which shell (a desktop
/// <c>Window</c> vs an in-canvas <c>Control</c>) to show.
///
/// <para>The head sets <see cref="App.Platform"/> before starting Avalonia; the App calls these in order:
/// <see cref="RegisterServices"/> (after the portable services, so a head can override a default),
/// then <see cref="CreateShell"/> once the provider is built, then <see cref="OnStarted"/>.</para>
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Registers platform-specific services into the DI container. Called after the shared services are
    /// registered, so a later registration of the same interface wins (lets a head substitute, e.g.,
    /// a browser-safe settings store). Must register at least one <c>IAudioBackend</c> and an
    /// <c>IMidiInputService</c>.
    /// </summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// Creates the root visual. Returns a <c>Window</c> under a classic-desktop lifetime, or a
    /// <c>Control</c> (the in-canvas main view) under a single-view (browser) lifetime.
    /// </summary>
    object CreateShell(IServiceProvider services);

    /// <summary>
    /// Runs once the shell is shown and the engine has started: a hook for background work such as
    /// scanning for plugins. May be a no-op.
    /// </summary>
    void OnStarted(IServiceProvider services);
}
