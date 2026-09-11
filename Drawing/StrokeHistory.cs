using Avalonia;
using SkiaSharp;

namespace PenDynamicsLab.Drawing;

/// <summary>
/// The strokes drawn so far, in order, and the parameter generation they were drawn under.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> a document model. There are no layers, no tools, and no selection —
/// just the list a stroke can be recorded into and replayed from, which is the minimum that makes
/// undo possible without inventing history after the fact.
/// </para>
/// <para>
/// <b>Input is authoritative; the pipeline output on each sample is a cache.</b> That is the
/// decision this type exists to make real. Replaying a stroke whose
/// <see cref="Stroke.ParamsVersion"/> still matches <see cref="ParamsVersion"/> can use the cached
/// values; a stroke from an older generation has to be re-run from its raw pressures, or the
/// canvas would show two curve generations at once with nothing to say which is which.
/// </para>
/// </remarks>
public sealed class StrokeHistory
{
    private readonly List<Stroke> _strokes = [];
    private Stroke? _current;

    /// <summary>The current parameter generation. Bumped whenever the curve params change.</summary>
    public int ParamsVersion { get; private set; }

    /// <summary>Completed strokes, oldest first.</summary>
    public IReadOnlyList<Stroke> Strokes => _strokes;

    /// <summary>Whether there is a completed stroke to undo.</summary>
    public bool CanUndo => _strokes.Count > 0;

    /// <summary>
    /// Note that the curve parameters have changed, invalidating every cached output.
    /// </summary>
    /// <remarks>
    /// Cheap and unconditional — it does not walk the strokes. Staleness is discovered per stroke
    /// at replay time by comparing versions, so changing a curve a hundred times costs a hundred
    /// increments rather than a hundred passes over the history.
    /// </remarks>
    public void NoteParamsChanged() => ParamsVersion++;

    /// <summary>Begin recording a stroke under the state currently in force.</summary>
    public void BeginStroke(BrushSettings brush, SKColor color)
        => _current = new Stroke(brush, color, ParamsVersion);

    /// <summary>Record one sample into the stroke in progress, if there is one.</summary>
    public void AddSample(Point position, double rawPressure, PenOrientation orientation, double processedPressure)
        => _current?.Add(new StrokeSample(position, rawPressure, orientation, processedPressure));

    /// <summary>
    /// Finish the stroke in progress and keep it, unless it never got a sample.
    /// </summary>
    /// <remarks>
    /// A stroke with no samples draws nothing, so keeping one would make undo appear to do
    /// nothing — the user would press undo and watch an empty entry disappear.
    /// </remarks>
    public void EndStroke()
    {
        if (_current is { Samples.Count: > 0 }) _strokes.Add(_current);
        _current = null;
    }

    /// <summary>Drop the most recent completed stroke. Returns false if there was none.</summary>
    public bool RemoveLast()
    {
        if (_strokes.Count == 0) return false;
        _strokes.RemoveAt(_strokes.Count - 1);
        return true;
    }

    /// <summary>Forget everything, including any stroke in progress.</summary>
    public void Clear()
    {
        _strokes.Clear();
        _current = null;
    }
}
