using System.Globalization;
using System.Text;
using Vidvix.Core.Models;
using Vidvix.Services;
using Vidvix.Services.AI;
using Vidvix.Services.FFmpeg;
using Vidvix.Services.MediaInfo;
using Vidvix.Utils;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    return ShowUsage();
}

try
{
    var command = args[0].Trim().ToLowerInvariant();
    var commandArguments = args.Skip(1).ToArray();

    return command switch
    {
        "catalog" => await RunCatalogAsync().ConfigureAwait(false),
        "interpolate" => await RunInterpolationAsync(commandArguments).ConfigureAwait(false),
        "enhance" => await RunEnhancementAsync(commandArguments).ConfigureAwait(false),
        _ => ShowUsage()
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 10;
}

static async Task<int> RunCatalogAsync()
{
    var services = await CreateServicesAsync().ConfigureAwait(false);
    await services.RuntimeCatalogService
        .EnsureExecutionSupportAsync(AiRuntimeKind.Rife)
        .ConfigureAwait(false);
    var catalog = await services.RuntimeCatalogService
        .EnsureExecutionSupportAsync(AiRuntimeKind.RealEsrgan)
        .ConfigureAwait(false);

    WriteKeyValue("RIFE_AVAILABILITY", catalog.Rife.Availability);
    WriteKeyValue("RIFE_RUNTIME_VERSION", catalog.Rife.RuntimeVersion);
    WriteKeyValue("RIFE_GPU_STATE", catalog.Rife.GpuSupport.State);
    WriteKeyValue("RIFE_GPU_DIAGNOSTIC", catalog.Rife.GpuSupport.DiagnosticMessage);
    WriteKeyValue("RIFE_CPU_STATE", catalog.Rife.CpuSupport.State);
    WriteKeyValue("RIFE_CPU_DIAGNOSTIC", catalog.Rife.CpuSupport.DiagnosticMessage);

    WriteKeyValue("REALESRGAN_AVAILABILITY", catalog.RealEsrgan.Availability);
    WriteKeyValue("REALESRGAN_RUNTIME_VERSION", catalog.RealEsrgan.RuntimeVersion);
    WriteKeyValue("REALESRGAN_GPU_STATE", catalog.RealEsrgan.GpuSupport.State);
    WriteKeyValue("REALESRGAN_GPU_DIAGNOSTIC", catalog.RealEsrgan.GpuSupport.DiagnosticMessage);
    WriteKeyValue("REALESRGAN_CPU_STATE", catalog.RealEsrgan.CpuSupport.State);
    WriteKeyValue("REALESRGAN_CPU_DIAGNOSTIC", catalog.RealEsrgan.CpuSupport.DiagnosticMessage);

    return 0;
}

static async Task<int> RunInterpolationAsync(string[] arguments)
{
    if (arguments.Length < 4)
    {
        Console.Error.WriteLine("Usage: AiOfflineSmoke interpolate <inputPath> <outputDirectory> <scaleFactor> <devicePreference> [outputExtension] [outputBaseName] [enableUhdMode]");
        return 1;
    }

    var inputPath = Path.GetFullPath(arguments[0]);
    var outputDirectory = Path.GetFullPath(arguments[1]);
    var scaleFactor = ParseInterpolationScaleFactor(arguments[2]);
    var devicePreference = ParseInterpolationDevicePreference(arguments[3]);
    var outputExtension = arguments.Length >= 5 ? arguments[4] : ".mp4";
    var outputBaseName = arguments.Length >= 6 && !string.IsNullOrWhiteSpace(arguments[5])
        ? arguments[5]
        : Path.GetFileNameWithoutExtension(inputPath) + "-interpolation-smoke";
    var enableUhdMode = arguments.Length >= 7 &&
                        bool.TryParse(arguments[6], out var parsedEnableUhdMode) &&
                        parsedEnableUhdMode;

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Input file not found: {inputPath}");
        return 2;
    }

    Directory.CreateDirectory(outputDirectory);

    var services = await CreateServicesAsync().ConfigureAwait(false);
    var outputFormat = ResolveVideoOutputFormat(services.Configuration, outputExtension);
    var progress = new Progress<AiInterpolationProgress>(update =>
    {
        var progressText = update.ProgressRatio is double ratio
            ? $"{Math.Round(ratio * 100d):0}%"
            : "N/A";
        Console.WriteLine($"[interpolation-progress] {update.Stage} | {progressText} | {update.DetailText}");
    });

    var result = await services.InterpolationWorkflowService.InterpolateAsync(
            new AiInterpolationRequest(
                inputPath,
                outputBaseName,
                outputFormat,
                outputDirectory,
                scaleFactor,
                devicePreference,
                enableUhdMode,
                progress))
        .ConfigureAwait(false);

    WriteKeyValue("RESULT_KIND", "Interpolation");
    WriteKeyValue("INPUT_PATH", result.InputPath);
    WriteKeyValue("OUTPUT_PATH", result.OutputPath);
    WriteKeyValue("OUTPUT_DIRECTORY", result.OutputDirectory);
    WriteKeyValue("OUTPUT_FILE_NAME", result.OutputFileName);
    WriteKeyValue("OUTPUT_EXISTS", File.Exists(result.OutputPath));
    WriteKeyValue("OUTPUT_EXTENSION", result.OutputFormat.Extension);
    WriteKeyValue("SCALE_FACTOR", (int)result.ScaleFactor);
    WriteKeyValue("INTERPOLATION_PASS_COUNT", result.InterpolationPassCount);
    WriteKeyValue("EXECUTION_DEVICE_KIND", result.ExecutionDeviceKind);
    WriteKeyValue("EXECUTION_DEVICE_NAME", result.ExecutionDeviceDisplayName);
    WriteKeyValue("SOURCE_FRAME_RATE", result.SourceFrameRate.ToString("0.###", CultureInfo.InvariantCulture));
    WriteKeyValue("TARGET_FRAME_RATE", result.TargetFrameRate.ToString("0.###", CultureInfo.InvariantCulture));
    WriteKeyValue("EXTRACTED_FRAME_COUNT", result.ExtractedFrameCount);
    WriteKeyValue("OUTPUT_FRAME_COUNT", result.OutputFrameCount);
    WriteKeyValue("USED_UHD_MODE", result.UsedUhdMode);
    WriteKeyValue("PRESERVED_AUDIO", result.PreservedOriginalAudio);
    WriteKeyValue("AUDIO_TRANSCODED", result.AudioWasTranscoded);
    WriteKeyValue("WORKFLOW_DURATION_MS", Math.Round(result.WorkflowDuration.TotalMilliseconds, 0).ToString(CultureInfo.InvariantCulture));

    return 0;
}

static async Task<int> RunEnhancementAsync(string[] arguments)
{
    if (arguments.Length < 4)
    {
        Console.Error.WriteLine("Usage: AiOfflineSmoke enhance <inputPath> <outputDirectory> <modelTier> <targetScaleFactor> [outputExtension] [outputBaseName]");
        return 1;
    }

    var inputPath = Path.GetFullPath(arguments[0]);
    var outputDirectory = Path.GetFullPath(arguments[1]);
    var modelTier = ParseEnhancementModelTier(arguments[2]);
    var targetScaleFactor = int.Parse(arguments[3], CultureInfo.InvariantCulture);
    var outputExtension = arguments.Length >= 5 ? arguments[4] : ".mp4";
    var outputBaseName = arguments.Length >= 6 && !string.IsNullOrWhiteSpace(arguments[5])
        ? arguments[5]
        : Path.GetFileNameWithoutExtension(inputPath) + "-enhancement-smoke";

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Input file not found: {inputPath}");
        return 2;
    }

    Directory.CreateDirectory(outputDirectory);

    var services = await CreateServicesAsync().ConfigureAwait(false);
    var outputFormat = ResolveVideoOutputFormat(services.Configuration, outputExtension);
    var progress = new Progress<AiEnhancementProgress>(update =>
    {
        var progressText = update.ProgressRatio is double ratio
            ? $"{Math.Round(ratio * 100d):0}%"
            : "N/A";
        Console.WriteLine($"[enhancement-progress] {update.Stage} | {progressText} | {update.DetailText}");
    });

    var result = await services.EnhancementWorkflowService.EnhanceAsync(
            new AiEnhancementRequest(
                inputPath,
                outputBaseName,
                outputFormat,
                outputDirectory,
                modelTier,
                targetScaleFactor,
                AiEnhancementDevicePreference.Automatic,
                progress))
        .ConfigureAwait(false);

    WriteKeyValue("RESULT_KIND", "Enhancement");
    WriteKeyValue("INPUT_PATH", result.InputPath);
    WriteKeyValue("OUTPUT_PATH", result.OutputPath);
    WriteKeyValue("OUTPUT_DIRECTORY", result.OutputDirectory);
    WriteKeyValue("OUTPUT_FILE_NAME", result.OutputFileName);
    WriteKeyValue("OUTPUT_EXISTS", File.Exists(result.OutputPath));
    WriteKeyValue("OUTPUT_EXTENSION", result.OutputFormat.Extension);
    WriteKeyValue("MODEL_TIER", result.ModelTier);
    WriteKeyValue("MODEL_DISPLAY_NAME", result.ModelDisplayName);
    WriteKeyValue("REQUESTED_SCALE", result.ScalePlan.RequestedScale);
    WriteKeyValue("ACHIEVED_SCALE", result.ScalePlan.AchievedScale);
    WriteKeyValue("PASS_COUNT", result.ScalePlan.PassCount);
    WriteKeyValue("PASS_SCALES", string.Join(",", result.ScalePlan.PassScales));
    WriteKeyValue("REQUIRES_DOWNSCALE", result.ScalePlan.RequiresDownscale);
    WriteKeyValue("EXECUTION_DEVICE_KIND", result.ExecutionDeviceKind);
    WriteKeyValue("EXECUTION_DEVICE_NAME", result.ExecutionDeviceDisplayName);
    WriteKeyValue("SOURCE_FRAME_RATE", result.SourceFrameRate.ToString("0.###", CultureInfo.InvariantCulture));
    WriteKeyValue("EXTRACTED_FRAME_COUNT", result.ExtractedFrameCount);
    WriteKeyValue("OUTPUT_FRAME_COUNT", result.OutputFrameCount);
    WriteKeyValue("PRESERVED_AUDIO", result.PreservedOriginalAudio);
    WriteKeyValue("AUDIO_TRANSCODED", result.AudioWasTranscoded);
    WriteKeyValue("WORKFLOW_DURATION_MS", Math.Round(result.WorkflowDuration.TotalMilliseconds, 0).ToString(CultureInfo.InvariantCulture));

    return 0;
}

static async Task<SmokeServices> CreateServicesAsync()
{
    var configuration = new ApplicationConfiguration();
    var logger = new SimpleLogger(mirrorToConsole: true);
    var userPreferencesService = new UserPreferencesService(configuration, logger);
    var localizationService = new LocalizationService(configuration, userPreferencesService, logger);
    await localizationService.InitializeAsync().ConfigureAwait(false);

    var packageSource = new FFmpegPackageSource(configuration, logger);
    var ffmpegRuntimeService = new FFmpegRuntimeService(configuration, packageSource, logger);
    var ffmpegService = new FFmpegService(logger);
    var mediaInfoService = new MediaInfoService(ffmpegRuntimeService, configuration, localizationService, logger);
    var aiRuntimeCatalogService = new AiRuntimeCatalogService(configuration, logger);
    var interpolationWorkflowService = new AiInterpolationWorkflowService(
        configuration,
        aiRuntimeCatalogService,
        mediaInfoService,
        ffmpegRuntimeService,
        ffmpegService,
        localizationService,
        logger);
    var enhancementWorkflowService = new AiEnhancementWorkflowService(
        configuration,
        aiRuntimeCatalogService,
        mediaInfoService,
        ffmpegRuntimeService,
        ffmpegService,
        localizationService,
        logger);

    return new SmokeServices(
        configuration,
        aiRuntimeCatalogService,
        interpolationWorkflowService,
        enhancementWorkflowService);
}

static OutputFormatOption ResolveVideoOutputFormat(ApplicationConfiguration configuration, string value)
{
    var normalizedExtension = value.StartsWith(".", StringComparison.Ordinal)
        ? value
        : $".{value}";
    var outputFormat = configuration.SupportedVideoOutputFormats.FirstOrDefault(format =>
        string.Equals(format.Extension, normalizedExtension, StringComparison.OrdinalIgnoreCase));

    return outputFormat ?? throw new InvalidOperationException($"Unsupported video output extension: {value}");
}

static AiInterpolationScaleFactor ParseInterpolationScaleFactor(string value) =>
    value.Trim().ToLowerInvariant() switch
    {
        "2" or "x2" => AiInterpolationScaleFactor.X2,
        "4" or "x4" => AiInterpolationScaleFactor.X4,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported interpolation scale factor: {value}")
    };

static AiInterpolationDevicePreference ParseInterpolationDevicePreference(string value) =>
    value.Trim().ToLowerInvariant() switch
    {
        "automatic" or "auto" => AiInterpolationDevicePreference.Automatic,
        "gpupreferred" or "gpu" => AiInterpolationDevicePreference.GpuPreferred,
        "cpu" => AiInterpolationDevicePreference.Cpu,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported interpolation device preference: {value}")
    };

static AiEnhancementModelTier ParseEnhancementModelTier(string value) =>
    value.Trim().ToLowerInvariant() switch
    {
        "standard" => AiEnhancementModelTier.Standard,
        "anime" => AiEnhancementModelTier.Anime,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported enhancement model tier: {value}")
    };

static void WriteKeyValue(string key, object? value)
{
    var normalizedValue = value switch
    {
        null => string.Empty,
        string text => NormalizeSingleLine(text),
        Enum enumeration => enumeration.ToString(),
        bool flag => flag ? "True" : "False",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    Console.WriteLine($"{key}={normalizedValue}");
}

static string NormalizeSingleLine(string value) =>
    string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

static int ShowUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  AiOfflineSmoke catalog");
    Console.Error.WriteLine("  AiOfflineSmoke interpolate <inputPath> <outputDirectory> <scaleFactor> <devicePreference> [outputExtension] [outputBaseName] [enableUhdMode]");
    Console.Error.WriteLine("  AiOfflineSmoke enhance <inputPath> <outputDirectory> <modelTier> <targetScaleFactor> [outputExtension] [outputBaseName]");
    return 1;
}

sealed record SmokeServices(
    ApplicationConfiguration Configuration,
    AiRuntimeCatalogService RuntimeCatalogService,
    AiInterpolationWorkflowService InterpolationWorkflowService,
    AiEnhancementWorkflowService EnhancementWorkflowService);
