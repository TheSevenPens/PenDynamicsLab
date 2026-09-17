using Avalonia;
using Avalonia.Controls;

using StrokeKit.Avalonia;

namespace PenDynamicsLab.Controls;

/// <summary>
/// One stroke-rendering surface: header label + Save button + Image. Doesn't own the
/// pixel data — MainWindow owns the <see cref="Drawing.DrawSurface"/> and registers
/// this view's <see cref="Image"/> with it (so the same surface can appear in multiple
/// tabs without duplicating state).
/// </summary>
public partial class StrokeCanvasView : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<StrokeCanvasView, string>(nameof(Header), defaultValue: "");

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Fires when the user picks "Save as PNG..." from the Export menu.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Fires when the user picks "Copy to clipboard" from the Export menu.</summary>
    public event EventHandler? CopyRequested;

    /// <summary>Fires when the user picks "Clear canvas" from the Export menu.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>The Image control that should be registered with a DrawSurface.</summary>
    /// <summary>The view this canvas draws through. Given its surface by DrawingSession.</summary>
    /// <remarks>
    /// Declared in markup, which StrokeKit's view could not be until it was given a
    /// parameterless constructor. It renders nothing until <c>Show</c> hands it a surface.
    /// </remarks>
    public SurfaceView View => CanvasSurface;

    /// <summary>The host Border whose bounds drive the surface size.</summary>
    public Border Host => ImageHost;

    public StrokeCanvasView()
    {
        InitializeComponent();
        HeaderText.Text = Header;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == HeaderProperty) HeaderText.Text = (string?)e.NewValue ?? "";
        };
    }

    // Handlers are named in XAML rather than wired from x:Name'd MenuItems, because the
    // flyout's contents live in their own namescope and are not reachable as fields here.
    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void Copy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void Clear_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ClearRequested?.Invoke(this, EventArgs.Empty);
}
