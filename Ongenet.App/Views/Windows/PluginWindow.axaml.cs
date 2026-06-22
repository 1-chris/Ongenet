using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.App.Views.Windows
{
    /// <summary>
    /// A dedicated, resizable host window for an embedded CLAP plugin GUI. The plugin reparents its
    /// native GUI into this window. To avoid a destroy/recreate cycle (which leaves many plugin GUIs
    /// black), the user's close button only HIDES the window — the GUI is kept alive and reshown on
    /// reopen; it is destroyed for real only when the app/owner closes (or the instrument is removed).
    /// </summary>
    public partial class PluginWindow : Window
    {
        private IPluginEditor? _editor;
        private DispatcherTimer? _pump;

        /// <summary>Raised when the user hides the window via its close button (so the inspector can refresh).</summary>
        public event Action? HiddenByUser;

        public PluginWindow()
        {
            InitializeComponent();
        }

        /// <summary>Binds the editor whose GUI is embedded here so it gets pumped (while visible) + closed.</summary>
        public void Bind(IPluginEditor editor)
        {
            _editor = editor;
            _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _pump.Tick += (_, _) => { if (IsVisible) _editor?.PumpEditor(); };
            _pump.Start();
        }

        protected override void OnResized(WindowResizedEventArgs e)
        {
            base.OnResized(e);
            if (IsVisible) _editor?.SetEditorSize((int)ClientSize.Width, (int)ClientSize.Height);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // A user-initiated close just hides (keeps the GUI alive for reopen). Real closes
            // (app/owner shutdown) fall through so the window can be destroyed.
            if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
            {
                e.Cancel = true;
                _editor?.CloseEditor();
                Hide();
                HiddenByUser?.Invoke();
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _pump?.Stop();
            _pump = null;
            _editor = null;
            base.OnClosed(e);
        }
    }
}
