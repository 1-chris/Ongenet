using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Rendering.Composition;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Controls.Engine3D
{
    /// <summary>
    /// Phase 2 capability probe + presenter selection. Avalonia 12 can import an external GPU texture into
    /// the compositor (zero-copy) via <see cref="ICompositionGpuInterop"/>, but only for the handle types the
    /// active backend advertises, and (per Avalonia's interop spec) not yet via Metal/IOSurface on macOS. We
    /// therefore probe at runtime and only choose the zero-copy presenter when both the compositor advertises
    /// a usable handle type AND the engine session actually produces shared-handle frames; otherwise the
    /// universal <see cref="ReadbackPresenter"/> is used (always, on macOS).
    /// </summary>
    internal static class Engine3DInterop
    {
        internal readonly record struct Capabilities(bool ZeroCopyPossible, string[] HandleTypes, string Note);

        /// <summary>Asynchronously asks the compositor what external GPU image handle types it can import.</summary>
        public static async Task<Capabilities> ProbeAsync(Visual visual)
        {
            try
            {
                var compositor = ElementComposition.GetElementVisual(visual)?.Compositor;
                if (compositor is null)
                    return new Capabilities(false, Array.Empty<string>(), "no compositor");

                var interop = await compositor.TryGetCompositionGpuInterop();
                if (interop is null)
                    return new Capabilities(false, Array.Empty<string>(), "GPU interop unavailable");

                var types = interop.SupportedImageHandleTypes?.ToArray() ?? Array.Empty<string>();
                // macOS interop is OpenGL-shared-context only today; our Vulkan export can't feed it, so
                // zero-copy is gated off there and readback is used.
                var zeroCopy = !OperatingSystem.IsMacOS() && types.Length > 0;
                var note = zeroCopy ? $"zero-copy available ({string.Join(", ", types)})"
                    : OperatingSystem.IsMacOS() ? "macOS: readback (compositor Metal interop not yet available)"
                    : "readback (no importable handle types)";
                return new Capabilities(zeroCopy, types, note);
            }
            catch (Exception ex)
            {
                return new Capabilities(false, Array.Empty<string>(), $"probe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Picks the presenter for a session. Zero-copy (<see cref="CompositionInteropPresenter"/>) is used only
        /// when the session produces shared-handle frames and the compositor can import them; otherwise the
        /// universal readback presenter. The Vulkan backend produces CPU frames today, so this resolves to
        /// readback - the zero-copy export path is the documented follow-up that flips a session to
        /// <see cref="FramePresentKind.SharedHandle"/>, at which point this seam selects the interop presenter.
        /// </summary>
        public static IEngine3DPresenter CreatePresenter(I3DRenderSession session, Visual host, in Capabilities caps)
        {
            if (session.PresentKind == FramePresentKind.SharedHandle && caps.ZeroCopyPossible)
                return new CompositionInteropPresenter(host);
            return new ReadbackPresenter();
        }
    }
}
