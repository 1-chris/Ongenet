using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Ongenet.Desktop.ViewModels;
using Ongenet.Core.Models.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;

namespace Ongenet.Desktop.Views.Windows;

/// <summary>
/// Window for displaying application logs with filtering by log level.
/// </summary>
public partial class LogWindow : ChromedWindow
{
    private MainViewModel? _viewModel;
    private ObservableCollection<LogEntry> _debugLogs = new ObservableCollection<LogEntry>();
    private ObservableCollection<LogEntry> _infoLogs = new ObservableCollection<LogEntry>();
    private ObservableCollection<LogEntry> _warningLogs = new ObservableCollection<LogEntry>();
    private ObservableCollection<LogEntry> _errorLogs = new ObservableCollection<LogEntry>();

    /// <summary>
    /// Initializes a new instance of the <see cref="LogWindow"/> class.
    /// </summary>
    public LogWindow()
    {
        InitializeComponent();
        
        // Custom TitleBar event handlers
        var titleBar = this.FindControl<Grid>("TitleBar");
        if (titleBar != null)
        {
            titleBar.PointerPressed += TitleBar_PointerPressed;
        }
        
        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null)
        {
            closeButton.Click += CloseButton_Click;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control c || c is Button || c.Parent is Button) return;
        BeginMoveDrag(e);
    }

    private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag)
        {
            var edge = tag switch
            {
                "Left" => WindowEdge.West,
                "Right" => WindowEdge.East,
                "Top" => WindowEdge.North,
                "Bottom" => WindowEdge.South,
                "TopLeft" => WindowEdge.NorthWest,
                "TopRight" => WindowEdge.NorthEast,
                "BottomLeft" => WindowEdge.SouthWest,
                "BottomRight" => WindowEdge.SouthEast,
                _ => WindowEdge.North
            };

            BeginResizeDrag(edge, e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Sets the view model for the log window and subscribes to log changes.
    /// </summary>
    /// <param name="viewModel">The main view model.</param>
    public void SetViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // Subscribe to log collection changes
        _viewModel.LogEntries.CollectionChanged += LogEntries_CollectionChanged;

        // Set up filtered collections for each tab
        UpdateFilteredCollections();

        var logListBoxDebug = this.FindControl<ListBox>("LogListBoxDebug");
        var logListBoxInfo = this.FindControl<ListBox>("LogListBoxInfo");
        var logListBoxWarning = this.FindControl<ListBox>("LogListBoxWarning");
        var logListBoxError = this.FindControl<ListBox>("LogListBoxError");

        if (logListBoxDebug != null) logListBoxDebug.ItemsSource = _debugLogs;
        if (logListBoxInfo != null) logListBoxInfo.ItemsSource = _infoLogs;
        if (logListBoxWarning != null) logListBoxWarning.ItemsSource = _warningLogs;
        if (logListBoxError != null) logListBoxError.ItemsSource = _errorLogs;

        ScrollToBottom();
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Update filtered collections when logs change
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (LogEntry entry in e.NewItems)
            {
                AddToFilteredCollection(entry);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _debugLogs.Clear();
            _infoLogs.Clear();
            _warningLogs.Clear();
            _errorLogs.Clear();
        }
        else
        {
            // For other actions, rebuild filtered collections
            UpdateFilteredCollections();
        }

        ScrollToBottom();
    }

    private void AddToFilteredCollection(LogEntry entry)
    {
        switch (entry.Level)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
                _debugLogs.Add(entry);
                break;
            case LogLevel.Information:
                _infoLogs.Add(entry);
                break;
            case LogLevel.Warning:
                _warningLogs.Add(entry);
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                _errorLogs.Add(entry);
                break;
        }
    }

    private void UpdateFilteredCollections()
    {
        if (_viewModel == null) return;

        _debugLogs.Clear();
        _infoLogs.Clear();
        _warningLogs.Clear();
        _errorLogs.Clear();

        foreach (var entry in _viewModel.LogEntries)
        {
            AddToFilteredCollection(entry);
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.LogEntries.Count > 0)
            {
                var tabControl = this.FindControl<TabControl>("LogTabs");
                if (tabControl?.SelectedIndex == null) return;

                ListBox? listBox = tabControl.SelectedIndex switch
                {
                    0 => this.FindControl<ListBox>("LogListBoxAll"),
                    1 => this.FindControl<ListBox>("LogListBoxDebug"),
                    2 => this.FindControl<ListBox>("LogListBoxInfo"),
                    3 => this.FindControl<ListBox>("LogListBoxWarning"),
                    4 => this.FindControl<ListBox>("LogListBoxError"),
                    _ => null
                };

                if (listBox?.ItemCount > 0)
                {
                    listBox.ScrollIntoView(listBox.ItemCount - 1);
                }
            }
        }, DispatcherPriority.Background);
    }

    private void LogTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ScrollToBottom();
    }

    private async void CopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var tabControl = this.FindControl<TabControl>("LogTabs");
        if (tabControl?.SelectedIndex == null) return;

        IEnumerable<LogEntry> logsToCopy = tabControl.SelectedIndex switch
        {
            0 => _viewModel.LogEntries,
            1 => _debugLogs,
            2 => _infoLogs,
            3 => _warningLogs,
            4 => _errorLogs,
            _ => _viewModel.LogEntries
        };

        var logsText = string.Join(Environment.NewLine, logsToCopy.Select(entry => entry.FormattedMessage));

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && !string.IsNullOrEmpty(logsText))
        {
            await clipboard.SetTextAsync(logsText);
        }
    }

    private void ClearLogs_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.LogEntries.Clear();
        _debugLogs.Clear();
        _infoLogs.Clear();
        _warningLogs.Clear();
        _errorLogs.Clear();
    }

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
        }
        base.OnClosing(e);
    }
}
