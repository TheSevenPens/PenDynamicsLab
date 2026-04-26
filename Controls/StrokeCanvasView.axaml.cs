using Avalonia;
using Avalonia.Controls;

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

    /// <summary>Fires when the user clicks the Save button.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>The Image control that should be registered with a DrawSurface.</summary>
    public Image Image => CanvasImage;

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
        SaveButton.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
    }
}
