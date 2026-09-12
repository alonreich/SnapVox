using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using snapvox.editor.forms;
using snapvox.foundation.core;
using snapvox.foundation.IniFile;
using snapvox.native.foundation;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpImage32 = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

[assembly: AvaloniaTestApplication(typeof(snapvox.tests.SafetyTestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace snapvox.tests;

public class SafetyTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SafetyTestApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class SafetyTestApp : Application
{
    public override void Initialize()
    {
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://snapvox.editor/"))
            { Source = new Uri("avares://snapvox.editor/Themes/DesignSystem.axaml") });
        Styles.Add(new FluentTheme());
    }
}

public class EditorSafetyTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(ImageEditorWindow editor, string method, params object[] args)
        => typeof(ImageEditorWindow).GetMethod(method, Private)!.Invoke(editor, args);
    private static T Field<T>(ImageEditorWindow editor, string name)
        => (T)typeof(ImageEditorWindow).GetField(name, Private)!.GetValue(editor)!;
    private static Task<SharpImage> Export(ImageEditorWindow editor)
        => (Task<SharpImage>)Call(editor, "GetFlattenedImageAsync")!;

    private static async Task<ImageEditorWindow> CreateEditor()
    {
        var config = IniConfig.GetIniSection<CoreConfiguration>(allowSave: false);
        config.AddFrameBorders = false;
        config.WarnBeforeClosingEditor = false;
        var editor = new ImageEditorWindow();
        await editor.SetImageAsync(new SharpImage32(64, 64, new Rgba32(255, 255, 255)), RECT.FromXYWH(0, 0, 64, 64));
        return editor;
    }

    [AvaloniaFact]
    public async Task HighlightOverBlackout_DoesNotRestoreUnderlyingWhitePixels()
    {
        var editor = await CreateEditor();
        try
        {
            Call(editor, "AddAnnotation", new Avalonia.Controls.Shapes.Rectangle { Width = 32, Height = 32, Fill = Brushes.Black });
            var highlight = Call(editor, "CreateHighlightAnnotation", new Point(0, 0), new Point(32, 32))!;
            Call(editor, "AddAnnotation", highlight);
            using var exported = await Export(editor);
            using var pixels = exported.CloneAs<Rgba32>();
            Assert.True(pixels[16, 16].R < 120 && pixels[16, 16].G < 120 && pixels[16, 16].B < 120,
                "Highlight must tint the blackout, not recover the original white pixels.");
            Assert.True(pixels[48, 48].R > 240);
        }
        finally { editor.Close(); }
    }

    [AvaloniaFact]
    public async Task RenderFailure_ThrowsInsteadOfExportingUneditedOriginal()
    {
        var editor = await CreateEditor();
        try
        {
            Field<Canvas>(editor, "_canvas").Children.Add(new BrokenRender { Width = 32, Height = 32 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => Export(editor));
            Assert.False(Field<bool>(editor, "_forceClose"));
        }
        finally { editor.Close(); }
    }

    [AvaloniaFact]
    public async Task Export_WaitsForPixelation_AndForAReplacementRequest()
    {
        var editor = await CreateEditor();
        try
        {
            var pixelation = (Control)Call(editor, "CreatePixelateAnnotation", new Point(0, 0), new Point(32, 32))!;
            Call(editor, "AddAnnotation", pixelation);
            await (Task)pixelation.Resources["PixelateTask"]!;
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var replacement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pixelation.Resources["PixelateTask"] = first.Task;
            var exporting = Export(editor);
            Assert.False(exporting.IsCompleted);
            pixelation.Resources["PixelateTask"] = replacement.Task;
            first.SetResult();
            await Task.Yield();
            Assert.False(exporting.IsCompleted);
            replacement.SetResult();
            using var result = await exporting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(result);
        }
        finally { editor.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedPixelation_PreventsExport()
    {
        var editor = await CreateEditor();
        try
        {
            var pixelation = (Control)Call(editor, "CreatePixelateAnnotation", new Point(0, 0), new Point(32, 32))!;
            Call(editor, "AddAnnotation", pixelation);
            await (Task)pixelation.Resources["PixelateTask"]!;
            pixelation.Resources["PixelateTask"] = Task.FromException(new IOException("Injected render failure"));
            await Assert.ThrowsAsync<IOException>(() => Export(editor));
            Assert.False(Field<bool>(editor, "_forceClose"));
        }
        finally { editor.Close(); }
    }

    [AvaloniaFact]
    public async Task SliderAndColorPreviewLayout_MetricsAreAccurate()
    {
        var editor = await CreateEditor();
        try
        {
            var preview = editor.FindControl<Border>("CurrentColorPreview");
            Assert.NotNull(preview);
            Assert.Equal(24.0, preview.Width);
            Assert.Equal(24.0, preview.Height);

            var slider = editor.FindControl<Slider>("ThicknessFlyoutSlider");
            Assert.NotNull(slider);
            Assert.False(slider.ClipToBounds);
            Assert.True(slider.Resources.ContainsKey("SliderPreContentMargin"));
            Assert.Equal(new GridLength(0.0), slider.Resources["SliderPreContentMargin"]);
            Assert.True(slider.Resources.ContainsKey("SliderPostContentMargin"));
            Assert.Equal(new GridLength(0.0), slider.Resources["SliderPostContentMargin"]);
        }
        finally { editor.Close(); }
    }

    private sealed class BrokenRender : Control
    {
        public override void Render(DrawingContext context) => throw new InvalidOperationException("Injected renderer failure");
    }
}
