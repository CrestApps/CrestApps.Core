using Jint;
using Jint.Native;
using SkiaSharp;

namespace CrestApps.Core.AI.Animations.Scripting;

/// <summary>
/// Replays the drawing commands an animation script recorded for one frame onto a SkiaSharp canvas, following the
/// HTML canvas rules the script was written against: state saved and restored as a stack, paths built and then
/// filled or stroked, styles that are colours or gradients, and the global alpha, blend, blur and shadow.
/// </summary>
/// <remarks>
/// The commands are a flat list of numbers written by <c>canvas-prelude.js</c>, with strings (colours and text)
/// held in a second list and referred to by index. The opcode numbers here and there must match.
/// </remarks>
internal sealed class CanvasCommandPlayer : IDisposable
{
    private static readonly SKBlendMode[] _blendModes =
    [
        SKBlendMode.SrcOver, SKBlendMode.Plus, SKBlendMode.Multiply, SKBlendMode.Screen, SKBlendMode.Overlay,
        SKBlendMode.Darken, SKBlendMode.Lighten, SKBlendMode.ColorDodge, SKBlendMode.ColorBurn, SKBlendMode.HardLight,
        SKBlendMode.SoftLight, SKBlendMode.Difference, SKBlendMode.Exclusion, SKBlendMode.SrcATop, SKBlendMode.DstOut,
        SKBlendMode.DstOver, SKBlendMode.SrcIn, SKBlendMode.DstIn, SKBlendMode.Xor, SKBlendMode.Src,
    ];

    private readonly Dictionary<string, SKColor> _colors = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _frameResources = [];
    private readonly Stack<DrawingState> _states = new();
    private readonly SKPath _path = new();
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly Dictionary<long, int> _specksBySpot = [];
    private readonly SKTypeface[] _typefaces = new SKTypeface[4];
    private readonly SKFont _font = new();
    private DrawingState _state;
    private JsArray _commands;
    private JsArray _strings;
    private int _position;

    /// <summary>
    /// Gets the most shapes that collapsed into a single 8-pixel spot in any one frame: the mark of a whole object
    /// drawn far too small to see, usually one designed one unit long and scaled by much less than its size in
    /// pixels. Scattered specks, such as stars, never pile up in one spot.
    /// </summary>
    public int LargestSpeckCluster { get; private set; }

    /// <summary>
    /// Gets where, in pixels, the largest cluster of specks was.
    /// </summary>
    public SKPoint SpeckClusterLocation { get; private set; }

    /// <summary>
    /// Replays one frame.
    /// </summary>
    /// <param name="canvas">The frame canvas, already cleared.</param>
    /// <param name="commands">The recorded commands.</param>
    /// <param name="strings">The strings the commands refer to.</param>
    public void Play(SKCanvas canvas, JsArray commands, JsArray strings)
    {
        _commands = commands;
        _strings = strings;
        _position = 0;
        _specksBySpot.Clear();
        _state = DrawingState.Default;
        _states.Clear();
        _path.Reset();

        var saveCount = canvas.Save();
        var length = (int)commands.Length;

        try
        {
            while (_position < length)
            {
                Execute(canvas, (int)Next());
            }
        }
        finally
        {
            canvas.RestoreToCount(saveCount);

            foreach (var resource in _frameResources)
            {
                resource.Dispose();
            }

            _frameResources.Clear();
        }
    }

    /// <summary>
    /// Releases the path and the paints.
    /// </summary>
    public void Dispose()
    {
        _path.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
        _font.Dispose();

        foreach (var typeface in _typefaces)
        {
            typeface?.Dispose();
        }
    }

    private void Execute(SKCanvas canvas, int command)
    {
        switch (command)
        {
            case 1:
                _states.Push(_state);
                canvas.Save();
                break;
            case 2:
                if (_states.Count > 0)
                {
                    _state = _states.Pop();
                    canvas.Restore();
                }

                break;
            case 3:
                canvas.Translate(NextFloat(), NextFloat());
                break;
            case 4:
                canvas.RotateRadians(NextFloat());
                break;
            case 5:
                canvas.Scale(NextFloat(), NextFloat());
                break;
            case 6:
                {
                    var matrix = NextMatrix();
                    canvas.Concat(in matrix);
                    break;
                }

            case 7:
                {
                    var matrix = NextMatrix();
                    canvas.SetMatrix(in matrix);
                    break;
                }

            case 8:
                canvas.ResetMatrix();
                break;
            case 10:
                _path.Reset();
                break;
            case 11:
                _path.Close();
                break;
            case 12:
                _path.MoveTo(NextFloat(), NextFloat());
                break;
            case 13:
                LineTo(NextFloat(), NextFloat());
                break;
            case 14:
                _path.QuadTo(NextFloat(), NextFloat(), NextFloat(), NextFloat());
                break;
            case 15:
                _path.CubicTo(NextFloat(), NextFloat(), NextFloat(), NextFloat(), NextFloat(), NextFloat());
                break;
            case 16:
                {
                    var x = NextFloat();
                    var y = NextFloat();
                    var radius = NextFloat();

                    Arc(x, y, radius, radius, 0, Next(), Next(), Next() != 0);
                    break;
                }

            case 17:
                {
                    var x = NextFloat();
                    var y = NextFloat();
                    var radiusX = NextFloat();
                    var radiusY = NextFloat();

                    Arc(x, y, radiusX, radiusY, Next(), Next(), Next(), Next() != 0);
                    break;
                }

            case 18:
                _path.AddRect(SKRect.Create(NextFloat(), NextFloat(), NextFloat(), NextFloat()));
                break;
            case 19:
                {
                    var rect = SKRect.Create(NextFloat(), NextFloat(), NextFloat(), NextFloat());
                    var radius = NextFloat();

                    _path.AddRoundRect(rect, radius, radius);
                    break;
                }

            case 20:
                _path.FillType = Next() != 0 ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                Count(canvas);
                canvas.DrawPath(_path, PrepareFill());
                break;
            case 21:
                Count(canvas);
                canvas.DrawPath(_path, PrepareStroke());
                break;
            case 22:
                _path.FillType = Next() != 0 ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                canvas.ClipPath(_path, SKClipOperation.Intersect, antialias: true);
                break;
            case 23:
                canvas.DrawRect(SKRect.Create(NextFloat(), NextFloat(), NextFloat(), NextFloat()), PrepareFill());
                break;
            case 24:
                canvas.DrawRect(SKRect.Create(NextFloat(), NextFloat(), NextFloat(), NextFloat()), PrepareStroke());
                break;
            case 25:
                {
                    // A video frame has nothing behind it, so a cleared area is black rather than transparent.
                    using var clear = new SKPaint { Color = SKColors.Black, BlendMode = SKBlendMode.Src };

                    canvas.DrawRect(SKRect.Create(NextFloat(), NextFloat(), NextFloat(), NextFloat()), clear);
                    break;
                }

            case 30:
                _state = _state with { FillColor = NextColor(), FillShader = null };
                break;
            case 31:
                _state = _state with { StrokeColor = NextColor(), StrokeShader = null };
                break;
            case 32:
                _state = _state with { FillShader = NextGradient() };
                break;
            case 33:
                _state = _state with { StrokeShader = NextGradient() };
                break;
            case 34:
                _state = _state with { LineWidth = NextFloat() };
                break;
            case 35:
                _state = _state with { LineCap = (int)Next() switch { 1 => SKStrokeCap.Round, 2 => SKStrokeCap.Square, _ => SKStrokeCap.Butt } };
                break;
            case 36:
                _state = _state with { LineJoin = (int)Next() switch { 1 => SKStrokeJoin.Round, 2 => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter } };
                break;
            case 37:
                _state = _state with { Alpha = Math.Clamp(NextFloat(), 0, 1) };
                break;
            case 38:
                {
                    var mode = (int)Next();
                    var blendMode = mode >= 0 && mode < _blendModes.Length
                        ? _blendModes[mode]
                        : SKBlendMode.SrcOver;
                    _state = _state with { BlendMode = blendMode };
                    break;
                }

            case 39:
                _state = _state with { Blur = Math.Clamp(NextFloat(), 0, 200) };
                break;
            case 40:
                _state = _state with { ShadowColor = NextColor(), ShadowBlur = Math.Clamp(NextFloat(), 0, 200), ShadowOffsetX = NextFloat(), ShadowOffsetY = NextFloat() };
                break;
            case 41:
            case 42:
                DrawText(canvas, command == 41);
                break;
            default:
                throw new AnimationScriptException($"The drawing command list is corrupt (unknown command {command}).");
        }
    }

    private void Count(SKCanvas canvas)
    {
        const float SpeckSize = 4;
        const float SpotSize = 8;

        var bounds = canvas.TotalMatrix.MapRect(_path.Bounds);

        if (Math.Max(bounds.Width, bounds.Height) >= SpeckSize)
        {
            return;
        }

        var spot = ((long)Math.Floor(bounds.MidX / SpotSize) << 32) ^ (uint)(int)Math.Floor(bounds.MidY / SpotSize);
        var count = _specksBySpot.TryGetValue(spot, out var existing) ? existing + 1 : 1;

        _specksBySpot[spot] = count;

        if (count > LargestSpeckCluster)
        {
            LargestSpeckCluster = count;
            SpeckClusterLocation = new SKPoint(bounds.MidX, bounds.MidY);
        }
    }

    private void LineTo(float x, float y)
    {
        // A canvas lineTo on an empty path starts it there, where Skia would start it at the origin.
        if (_path.IsEmpty)
        {
            _path.MoveTo(x, y);

            return;
        }

        _path.LineTo(x, y);
    }

    private void Arc(float x, float y, float radiusX, float radiusY, double rotation, double start, double end, bool counterclockwise)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            LineTo(x, y);

            return;
        }

        // The sweep follows the canvas rules: a full turn or more is a whole ellipse, otherwise the sweep is the
        // shortest one in the asked direction.
        var sweep = end - start;
        var fullTurn = Math.PI * 2;

        if (!counterclockwise)
        {
            sweep = sweep >= fullTurn ? fullTurn : ((sweep % fullTurn) + fullTurn) % fullTurn;
        }
        else
        {
            sweep = sweep <= -fullTurn ? -fullTurn : -((((-sweep) % fullTurn) + fullTurn) % fullTurn);
        }

        var oval = new SKRect(x - radiusX, y - radiusY, x + radiusX, y + radiusY);
        var startDegrees = (float)(start * 180 / Math.PI);
        var sweepDegrees = (float)(sweep * 180 / Math.PI);

        if (rotation == 0)
        {
            AddArc(_path, oval, startDegrees, sweepDegrees);

            return;
        }

        using var arc = new SKPath();

        if (!_path.IsEmpty)
        {
            // Keeps the connecting line from the current point that a canvas arc draws.
            var last = _path.LastPoint;
            var inverse = SKMatrix.CreateRotation((float)-rotation, x, y);

            arc.MoveTo(inverse.MapPoint(last));
        }

        AddArc(arc, oval, startDegrees, sweepDegrees);

        var matrix = SKMatrix.CreateRotation((float)rotation, x, y);

        _path.AddPath(arc, in matrix, SKPathAddMode.Extend);
    }

    private static void AddArc(SKPath path, SKRect oval, float startDegrees, float sweepDegrees)
    {
        // Skia draws nothing for a single 360-degree arc, so a whole turn is drawn in two halves.
        if (Math.Abs(sweepDegrees) >= 359.99f)
        {
            path.ArcTo(oval, startDegrees, sweepDegrees / 2, forceMoveTo: false);
            path.ArcTo(oval, startDegrees + (sweepDegrees / 2), sweepDegrees / 2, forceMoveTo: false);

            return;
        }

        path.ArcTo(oval, startDegrees, sweepDegrees, forceMoveTo: false);
    }

    private SKPaint PrepareFill()
    {
        Apply(_fill, _state.FillColor, _state.FillShader);

        return _fill;
    }

    private SKPaint PrepareStroke()
    {
        Apply(_stroke, _state.StrokeColor, _state.StrokeShader);
        _stroke.StrokeWidth = _state.LineWidth;
        _stroke.StrokeCap = _state.LineCap;
        _stroke.StrokeJoin = _state.LineJoin;

        return _stroke;
    }

    private void Apply(SKPaint paint, SKColor color, SKShader shader)
    {
        paint.Shader = shader;
        paint.Color = shader is null
            ? color.WithAlpha((byte)(color.Alpha * _state.Alpha))
            : SKColors.Black.WithAlpha((byte)(255 * _state.Alpha));
        paint.BlendMode = _state.BlendMode;
        paint.ImageFilter = CreateFilter();
    }

    private SKImageFilter CreateFilter()
    {
        SKImageFilter filter = null;

        if (_state.Blur > 0)
        {
            // CSS blur(Npx) is a standard deviation of N pixels.
            filter = SKImageFilter.CreateBlur(_state.Blur, _state.Blur);
            _frameResources.Add(filter);
        }

        if (_state.ShadowColor.Alpha > 0 && (_state.ShadowBlur > 0 || _state.ShadowOffsetX != 0 || _state.ShadowOffsetY != 0))
        {
            // A canvas shadowBlur is twice the standard deviation of the blur it draws.
            var sigma = _state.ShadowBlur / 2;
            var shadow = SKImageFilter.CreateDropShadow(_state.ShadowOffsetX, _state.ShadowOffsetY, sigma, sigma, _state.ShadowColor, filter);

            _frameResources.Add(shadow);
            filter = shadow;
        }

        return filter;
    }

    private void DrawText(SKCanvas canvas, bool fill)
    {
        var text = NextString();
        var x = NextFloat();
        var y = NextFloat();
        var size = Math.Clamp(NextFloat(), 1, 1000);
        var bold = Next() != 0;
        var italic = Next() != 0;
        var align = (int)Next() switch { 1 => SKTextAlign.Center, 2 => SKTextAlign.Right, _ => SKTextAlign.Left };

        _font.Typeface = GetTypeface(bold, italic);
        _font.Size = size;

        canvas.DrawText(text, x, y, align, _font, fill ? PrepareFill() : PrepareStroke());
    }

    private SKTypeface GetTypeface(bool bold, bool italic)
    {
        // Looking a typeface up asks the platform's font manager, so each of the four styles is looked up once.
        var index = (bold ? 1 : 0) + (italic ? 2 : 0);

        if (_typefaces[index] is null)
        {
            var weight = bold
                ? SKFontStyleWeight.Bold
                : SKFontStyleWeight.Normal;
            var slant = italic
                ? SKFontStyleSlant.Italic
                : SKFontStyleSlant.Upright;

            _typefaces[index] = SKTypeface.FromFamilyName(null, new SKFontStyle(weight, SKFontStyleWidth.Normal, slant));
        }

        return _typefaces[index];
    }

    private SKShader NextGradient()
    {
        // A linear gradient arrives as (x0, y0, x1, y1, 0, 0); a radial one as (x0, y0, r0, x1, y1, r1).
        var kind = (int)Next();
        var a = NextFloat();
        var b = NextFloat();
        var c = NextFloat();
        var d = NextFloat();
        var e = NextFloat();
        var f = NextFloat();
        var stopCount = Math.Clamp((int)Next(), 0, 256);
        var colors = new SKColor[Math.Max(stopCount, 1)];
        var positions = new float[Math.Max(stopCount, 1)];

        for (var stop = 0; stop < stopCount; stop++)
        {
            positions[stop] = NextFloat();
            colors[stop] = NextColor();
        }

        if (stopCount == 0)
        {
            colors[0] = SKColors.Transparent;
        }

        var shader = kind == 0
            ? SKShader.CreateLinearGradient(new SKPoint(a, b), new SKPoint(c, d), colors, positions, SKShaderTileMode.Clamp)
            : SKShader.CreateTwoPointConicalGradient(new SKPoint(a, b), c, new SKPoint(d, e), f, colors, positions, SKShaderTileMode.Clamp);

        _frameResources.Add(shader);

        return shader;
    }

    private SKMatrix NextMatrix()
    {
        var a = NextFloat();
        var b = NextFloat();
        var c = NextFloat();
        var d = NextFloat();
        var e = NextFloat();
        var f = NextFloat();

        return new SKMatrix(a, c, e, b, d, f, 0, 0, 1);
    }

    private SKColor NextColor()
    {
        var text = NextString();

        if (!_colors.TryGetValue(text, out var color))
        {
            color = CssColorParser.Parse(text);
            _colors[text] = color;
        }

        return color;
    }

    private string NextString()
    {
        var index = (int)Next();

        return index >= 0 && index < _strings.Length ? _strings[index].ToString() : string.Empty;
    }

    private float NextFloat()
    {
        return (float)Next();
    }

    private double Next()
    {
        if (_position >= _commands.Length)
        {
            throw new AnimationScriptException("The drawing command list ended in the middle of a command.");
        }

        var value = _commands[_position++];

        return value.IsNumber() ? value.AsNumber() : 0;
    }

    private readonly record struct DrawingState(
        SKColor FillColor,
        SKShader FillShader,
        SKColor StrokeColor,
        SKShader StrokeShader,
        float LineWidth,
        SKStrokeCap LineCap,
        SKStrokeJoin LineJoin,
        float Alpha,
        SKBlendMode BlendMode,
        float Blur,
        SKColor ShadowColor,
        float ShadowBlur,
        float ShadowOffsetX,
        float ShadowOffsetY)
    {
        public static DrawingState Default => new(SKColors.Black, null, SKColors.Black, null, 1, SKStrokeCap.Butt, SKStrokeJoin.Miter, 1, SKBlendMode.SrcOver, 0, SKColors.Transparent, 0, 0, 0);
    }
}
