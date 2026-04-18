using Vidvix.Core.Models;
using Vidvix.Services;
using Vidvix.Services.Demucs;
using Vidvix.Services.FFmpeg;
using Vidvix.Services.MediaInfo;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SplitAudioOfflineSmoke <inputPath> <outputDirectory> [outputExtension]");
    return 1;
}

var inputPath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
var outputExtension = args.Length >= 3 ? args[2] : ".wav";

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    return 2;
}

var configuration = new ApplicationConfiguration();
var logger = new SimpleLogger(mirrorToConsole: true);
var packageSource = new FFmpegPackageSource(configuration, logger);
var ffmpegRuntimeService = new FFmpegRuntimeService(configuration, packageSource, logger);
var ffmpegService = new FFmpegService(logger);
var mediaInfoService = new MediaInfoService(ffmpegRuntimeService, configuration, logger);
var commandBuilder = new FFmpegCommandBuilder(configuration.FFmpegExecutableFileName);
var mediaProcessingCommandFactory = new MediaProcessingCommandFactory(configuration, commandBuilder);
var demucsRuntimeService = new DemucsRuntimeService(configuration, logger);
var workflowService = new AudioSeparationWorkflowService(
    configuration,
    ffmpegRuntimeService,
    ffmpegService,
    mediaInfoService,
    mediaProcessingCommandFactory,
    commandBuilder,
    demucsRuntimeService,
    logger);

var outputFormat = configuration.SupportedAudioOutputFormats.FirstOrDefault(format =>
    string.Equals(format.Extension, NormalizeExtension(outputExtension), StringComparison.OrdinalIgnoreCase));

if (outputFormat is null)
{
    Console.Error.WriteLine($"Unsupported output extension: {outputExtension}");
    return 3;
}

Directory.CreateDirectory(outputDirectory);

var progress = new Progress<AudioSeparationProgress>(update =>
{
    var progressText = update.ProgressRatio is double ratio
        ? $"{Math.Round(ratio * 100d):0}%"
        : "N/A";
    Console.WriteLine($"[progress] {update.StageTitle} | {progressText} | {update.DetailText}");
});

try
{
    var result = await workflowService.SeparateAsync(
        new AudioSeparationRequest(inputPath, outputFormat, outputDirectory, progress));

    Console.WriteLine($"INPUT={result.InputPath}");
    Console.WriteLine($"OUTPUT_DIR={result.OutputDirectory}");
    Console.WriteLine($"DURATION_MS={Math.Round(result.Duration.TotalMilliseconds, 0)}");

    foreach (var stem in result.StemOutputs.OrderBy(item => item.StemKind))
    {
        Console.WriteLine($"STEM_{stem.StemKind.ToString().ToUpperInvariant()}={stem.FilePath}");
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.ToString());
    return 10;
}

static string NormalizeExtension(string value) =>
    value.StartsWith(".", StringComparison.Ordinal) ? value : $".{value}";
