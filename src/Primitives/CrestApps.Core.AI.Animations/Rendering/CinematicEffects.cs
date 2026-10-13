using SkiaSharp;

namespace CrestApps.Core.AI.Animations.Rendering;

/// <summary>
/// Lays a film look over a finished frame: a vignette that darkens the corners, grain that shifts every frame, and
/// widescreen letterbox bars.
/// </summary>
internal sealed class CinematicEffects : IDisposable
{
    private const int GrainTileSize = 256;
    private const double LetterboxAspectRatio = 2.39;

    private readonly CinematicLook _look;
    private readonly int _width;
    private readonly int _height;
    private readonly SKPaint _paint = new() { IsAntialias = false };
    private readonly SKShader _vignette;
    private readonly SKImage _grain;
    private readonly Random _random = new(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="CinematicEffects"/> class.
    /// </summary>
    /// <param name="look">The look.</param>
    /// <param name="width">The frame width.</param>
    /// <param name="height">The frame height.</param>
    public CinematicEffects(
        CinematicLook look,
        int width,
        int height)
    {
        _look = look;
        _width = width;
        _height = height;

        if (look.Vignette > 0)
        {
            var edge = SKColors.Black.WithAlpha((byte)(220 * look.Vignette));

            _vignette = SKShader.CreateRadialGradient(
                new SKPoint(width / 2f, height / 2f),
                MathF.Sqrt((width * width) + (height * height)) / 2,
                [SKColors.Transparent, SKColors.Transparent, edge],
                [0f, 0.55f, 1f],
                SKShaderTileMode.Clamp);
        }

        if (look.Grain > 0)
        {
            _grain = CreateGrainTile(look.Grain);
        }
    }

    /// <summary>
    /// Lays the look over a frame.
    /// </summary>
    /// <param name="canvas">The frame canvas.</param>
    public void Apply(SKCanvas canvas)
    {
        if (_vignette is not null)
        {
            _paint.Shader = _vignette;
            canvas.DrawRect(0, 0, _width, _height, _paint);
            _paint.Shader = null;
        }

        if (_grain is not null)
        {
            // A new random offset every frame makes the grain crawl like film rather than sit on the picture.
            var offset = SKMatrix.CreateTranslation(_random.Next(GrainTileSize), _random.Next(GrainTileSize));

            using var shader = _grain.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, offset);

            _paint.Shader = shader;
            _paint.BlendMode = SKBlendMode.Overlay;
            canvas.DrawRect(0, 0, _width, _height, _paint);
            _paint.Shader = null;
            _paint.BlendMode = SKBlendMode.SrcOver;
        }

        if (_look.Letterbox)
        {
            var barHeight = Math.Max(0, (_height - (float)(_width / LetterboxAspectRatio)) / 2);

            _paint.Color = SKColors.Black;
            canvas.DrawRect(0, 0, _width, barHeight, _paint);
            canvas.DrawRect(0, _height - barHeight, _width, barHeight, _paint);
        }
    }

    /// <summary>
    /// Releases the paint, the vignette and the grain tile.
    /// </summary>
    public void Dispose()
    {
        _paint.Dispose();
        _vignette?.Dispose();
        _grain?.Dispose();
    }

    private static SKImage CreateGrainTile(float strength)
    {
        var random = new Random(7);
        var info = new SKImageInfo(GrainTileSize, GrainTileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        var pixels = new byte[info.BytesSize];
        var alpha = (byte)(110 * strength);

        // Mid-grey noise under an overlay blend lightens and darkens the picture about its own tones.
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var gray = (byte)random.Next(80, 176);
            var premultiplied = (byte)(gray * alpha / 255);

            pixels[index] = premultiplied;
            pixels[index + 1] = premultiplied;
            pixels[index + 2] = premultiplied;
            pixels[index + 3] = alpha;
        }

        using var data = SKData.CreateCopy(pixels);

        return SKImage.FromPixels(info, data, info.RowBytes);
    }
}
