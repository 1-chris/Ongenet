using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Ongenet.App.Controls;
using Ongenet.App.Controls.Engine3D;
using Ongenet.App.Services;
using Ongenet.Core.Models.Media;
using Ongenet.App.Localization;
using Ongenet.Core.Services.Interfaces;
using Ongenet.Engine3D.Abstractions;

namespace Ongenet.App.Views.Windows;

public partial class Engine3DSnapshotWindow : Window
{
    private enum SnapshotPreviewKind { Demo, Cube }

    private string? _result;
    private IProjectService? _project;
    private IProjectFileService? _projectFile;
    private SnapshotPreviewKind _previewKind = SnapshotPreviewKind.Demo;
    private float _demoTime;

    public Engine3DSnapshotWindow()
    {
        InitializeComponent();
        CancelButton.Click += OnCancel;
        CaptureButton.Click += OnCapture;
        VizCombo.ItemsSource = new[]
        {
            Localize("VideoTrack_3d_snapshot_viz_demo"),
            Localize("VideoTrack_3d_snapshot_viz_cube")
        };
        VizCombo.SelectedIndex = 0;
        VizCombo.SelectionChanged += (_, _) =>
        {
            _previewKind = VizCombo.SelectedIndex == 1 ? SnapshotPreviewKind.Cube : SnapshotPreviewKind.Demo;
            RebuildPreviewScene();
        };
        PreviewView.OnUpdate = OnPreviewUpdate;
        RebuildPreviewScene();
    }

    public static async Task<string?> ShowAsync(Window owner, IProjectService project, IProjectFileService projectFile)
    {
        var factory = App.ServiceProvider?.GetService<I3DEngineFactory>();
        if (factory is null || !factory.IsAvailable) return null;

        var window = new Engine3DSnapshotWindow
        {
            _project = project,
            _projectFile = projectFile
        };
        await window.ShowDialog(owner);
        return window._result;
    }

    private void RebuildPreviewScene()
    {
        var scene = PreviewView.Scene;
        scene.Root.Children.Clear();
        scene.Camera.Target = new Vector3(0f, 0.2f, 0f);
        scene.Camera.Distance = 4.5f;
        scene.TransparentBackground = TransparentCheck.IsChecked == true;

        if (_previewKind == SnapshotPreviewKind.Cube)
        {
            var layer = new VideoLayer { Engine3DTransparentBackground = scene.TransparentBackground };
            var viz = new TexturedCubeVisualization(layer);
            viz.Build(scene);
            viz.ApplyTheme(scene);
            return;
        }

        scene.Root.AddChild(new SceneNode
        {
            Name = "ground",
            Position = new Vector3(0f, -0.9f, 0f),
            Mesh = MeshData.Plane(6f),
            Material = new Material { BaseColor = new Vector4(0.15f, 0.16f, 0.20f, 1f), Roughness = 0.9f }
        });
        scene.Root.AddChild(new SceneNode
        {
            Name = "cube",
            Mesh = MeshData.Box(1.2f),
            Material = new Material
            {
                BaseColor = new Vector4(0.18f, 0.72f, 0.74f, 1f),
                Metallic = 0.35f,
                Roughness = 0.35f
            }
        });
        scene.Root.AddChild(new SceneNode
        {
            Name = "sphere",
            Position = new Vector3(1.6f, 0.1f, -0.3f),
            Mesh = MeshData.Sphere(0.55f),
            Material = new Material
            {
                BaseColor = new Vector4(0.80f, 0.55f, 0.95f, 1f),
                Roughness = 0.5f
            }
        });
    }

    private void OnPreviewUpdate(Scene scene, double dt)
    {
        scene.TransparentBackground = TransparentCheck.IsChecked == true;
        if (_previewKind != SnapshotPreviewKind.Demo) return;

        _demoTime += (float)dt;
        var cube = scene.Root.Children.Find(n => n.Name == "cube");
        var sphere = scene.Root.Children.Find(n => n.Name == "sphere");
        if (cube is not null)
            cube.Rotation = Quaternion.CreateFromYawPitchRoll(_demoTime * 0.7f, _demoTime * 0.4f, 0f);
        if (sphere is not null)
            sphere.Position = new Vector3(1.6f, 0.1f + 0.25f * MathF.Sin(_demoTime * 1.5f), -0.3f);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    private void OnCapture(object? sender, RoutedEventArgs e)
    {
        var factory = App.ServiceProvider?.GetService<I3DEngineFactory>();
        if (factory is null || _project is null) return;

        PreviewView.Scene.TransparentBackground = TransparentCheck.IsChecked == true;
        var projectDir = _projectFile?.CurrentPath is { } p
            ? System.IO.Path.GetDirectoryName(p)
            : null;
        var w = Math.Max(16, _project.Current.VideoCanvasWidth);
        var h = Math.Max(16, _project.Current.VideoCanvasHeight);
        _result = Engine3DSnapshotExporter.Export(factory, PreviewView.Scene, w, h, projectDir);
        Close();
    }

    private static string Localize(string key) => Loc.Get(key);
}

file static class SceneNodeListExtensions
{
    public static SceneNode? Find(this IReadOnlyList<SceneNode> nodes, Func<SceneNode, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (predicate(node)) return node;
            var child = Find(node.Children, predicate);
            if (child is not null) return child;
        }

        return null;
    }
}
