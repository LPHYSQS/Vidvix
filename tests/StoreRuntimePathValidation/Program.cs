using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;
using Vidvix.Services;
using Vidvix.Services.AI;
using Vidvix.Services.FFmpeg;
using Vidvix.Services.MediaInfo;
using Vidvix.Services.VideoPreview;
using Vidvix.Utils;

var configuration = new ApplicationConfiguration();
var packageRootPath = AppContext.BaseDirectory;
var validationId = Guid.NewGuid().ToString("N");
var localValidationRootPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    configuration.LocalDataDirectoryName,
    configuration.RuntimeDirectoryName,
    "StoreRuntimePathValidation",
    validationId);

Directory.CreateDirectory(localValidationRootPath);

try
{
    ValidateDemucsStartInfo(configuration, packageRootPath, localValidationRootPath);
    ValidateFfmpegResolution(configuration, packageRootPath, localValidationRootPath);
    ValidateFfprobeResolution(configuration, localValidationRootPath);
    await ValidateAiPreparedModelCacheAsync(configuration, packageRootPath).ConfigureAwait(false);
    ValidateAiWorkflowPaths(configuration, packageRootPath, localValidationRootPath);
    ValidateMpvRuntimeDirectory(configuration, packageRootPath);

    Console.WriteLine("Store runtime path validation passed.");
    return 0;
}
finally
{
    TryDeleteDirectory(localValidationRootPath);
}

void ValidateDemucsStartInfo(
    ApplicationConfiguration configuration,
    string packageRootPath,
    string localValidationRootPath)
{
    var packageScriptPath = Path.Combine(
        packageRootPath,
        configuration.RuntimeDirectoryName,
        configuration.DemucsDirectoryName,
        configuration.DemucsScriptsDirectoryName,
        configuration.DemucsRunnerScriptFileName);
    Directory.CreateDirectory(Path.GetDirectoryName(packageScriptPath)!);
    File.WriteAllText(packageScriptPath, "# demucs runner validation");

    var runtimeRootPath = Path.Combine(
        localValidationRootPath,
        configuration.DemucsDirectoryName,
        configuration.DemucsRuntimeDirectoryName);
    var pythonExecutablePath = Path.Combine(runtimeRootPath, configuration.DemucsPythonExecutableFileName);
    var modelRepositoryPath = Path.Combine(localValidationRootPath, configuration.DemucsDirectoryName, configuration.DemucsModelsDirectoryName);
    Directory.CreateDirectory(runtimeRootPath);
    Directory.CreateDirectory(modelRepositoryPath);
    File.WriteAllText(pythonExecutablePath, "python");

    var executionPlan = new DemucsExecutionPlan
    {
        RequestedAccelerationMode = DemucsAccelerationMode.Cpu,
        SelectedDeviceKind = DemucsExecutionDeviceKind.Cpu,
        DeviceDisplayName = "CPU",
        DeviceArgument = "cpu",
        LauncherScriptPath = packageScriptPath,
        ResolutionSummary = "validation",
        RuntimeResolution = new DemucsRuntimeResolution
        {
            RuntimeVariant = DemucsRuntimeVariant.Cpu,
            PythonExecutablePath = pythonExecutablePath,
            RuntimeRootPath = runtimeRootPath,
            ModelRepositoryPath = modelRepositoryPath,
            WasExtracted = true
        }
    };

    var workflowService = CreateUninitializedWithConfiguration<AudioSeparationWorkflowService>(configuration);
    var startInfo = InvokePrivateInstanceMethod<ProcessStartInfo>(
        workflowService,
        "CreateDemucsStartInfo",
        executionPlan,
        Path.Combine(packageRootPath, "input.wav"),
        Path.Combine(localValidationRootPath, "demucs-output"));

    AssertPathEquals(pythonExecutablePath, startInfo.FileName, "Demucs should launch the extracted python runtime path.");
    AssertPathEquals(runtimeRootPath, startInfo.WorkingDirectory!, "Demucs should run with the extracted runtime root as working directory.");
    AssertPathEquals(packageScriptPath, startInfo.ArgumentList[0], "Demucs should still use the packaged launcher script path.");
    AssertContainsPath(startInfo.ArgumentList, modelRepositoryPath, "Demucs should pass the resolved model repository path.");
}

void ValidateFfmpegResolution(
    ApplicationConfiguration configuration,
    string packageRootPath,
    string localValidationRootPath)
{
    var localCurrentBinPath = Path.Combine(
        localValidationRootPath,
        configuration.RuntimeVendorDirectoryName,
        configuration.RuntimeCurrentVersionDirectoryName,
        "bin");
    Directory.CreateDirectory(localCurrentBinPath);

    var localFfmpegPath = Path.Combine(localCurrentBinPath, configuration.FFmpegExecutableFileName);
    var localFfprobePath = Path.Combine(localCurrentBinPath, configuration.FFprobeExecutableFileName);
    var localFfplayPath = Path.Combine(localCurrentBinPath, configuration.FFplayExecutableFileName);
    File.WriteAllText(localFfmpegPath, "ffmpeg");
    File.WriteAllText(localFfprobePath, "ffprobe");
    File.WriteAllText(localFfplayPath, "ffplay");

    var packagedFfprobePath = Path.Combine(
        packageRootPath,
        configuration.RuntimeDirectoryName,
        configuration.BundledRuntimeDirectoryName,
        configuration.FFprobeExecutableFileName);
    Directory.CreateDirectory(Path.GetDirectoryName(packagedFfprobePath)!);
    File.WriteAllText(packagedFfprobePath, "packaged-ffprobe");

    var terminalService = CreateUninitializedWithConfiguration<FFmpegTerminalService>(configuration);
    var resolution = new FFmpegRuntimeResolution
    {
        ExecutablePath = localFfmpegPath,
        StorageRootPath = Path.Combine(localValidationRootPath, configuration.RuntimeVendorDirectoryName),
        WasDownloaded = true
    };

    var resolvedFfmpegPath = InvokePrivateInstanceMethod<string>(terminalService, "ResolveExecutablePath", "ffmpeg", resolution);
    var resolvedFfprobePath = InvokePrivateInstanceMethod<string>(terminalService, "ResolveExecutablePath", "ffprobe", resolution);
    var resolvedFfplayPath = InvokePrivateInstanceMethod<string>(terminalService, "ResolveExecutablePath", "ffplay", resolution);

    AssertPathEquals(localFfmpegPath, resolvedFfmpegPath, "FFmpeg should keep using the resolved runtime executable path.");
    AssertPathEquals(localFfprobePath, resolvedFfprobePath, "FFprobe should resolve next to the extracted FFmpeg runtime before falling back to packaged roots.");
    AssertPathEquals(localFfplayPath, resolvedFfplayPath, "FFplay should resolve next to the extracted FFmpeg runtime before falling back to packaged roots.");
}

void ValidateFfprobeResolution(
    ApplicationConfiguration configuration,
    string localValidationRootPath)
{
    var ffmpegExecutablePath = Path.Combine(
        localValidationRootPath,
        configuration.RuntimeVendorDirectoryName,
        configuration.RuntimeCurrentVersionDirectoryName,
        "bin",
        configuration.FFmpegExecutableFileName);
    Directory.CreateDirectory(Path.GetDirectoryName(ffmpegExecutablePath)!);
    File.WriteAllText(ffmpegExecutablePath, "ffmpeg");
    var expectedFfprobePath = Path.Combine(Path.GetDirectoryName(ffmpegExecutablePath)!, configuration.FFprobeExecutableFileName);

    var mediaInfoService = CreateUninitializedWithConfiguration<MediaInfoService>(configuration);
    var thumbnailService = CreateUninitializedWithConfiguration<VideoThumbnailService>(configuration);
    var trimWorkflowService = CreateUninitializedWithConfiguration<VideoTrimWorkflowService>(configuration);

    AssertPathEquals(
        expectedFfprobePath,
        InvokePrivateInstanceMethod<string>(mediaInfoService, "ResolveFfprobePath", ffmpegExecutablePath),
        "MediaInfoService should derive ffprobe from the resolved FFmpeg path.");
    AssertPathEquals(
        expectedFfprobePath,
        InvokePrivateInstanceMethod<string>(thumbnailService, "ResolveFfprobePath", ffmpegExecutablePath),
        "VideoThumbnailService should derive ffprobe from the resolved FFmpeg path.");
    AssertPathEquals(
        expectedFfprobePath,
        InvokePrivateInstanceMethod<string>(trimWorkflowService, "ResolveFfprobePath", ffmpegExecutablePath),
        "VideoTrimWorkflowService should derive ffprobe from the resolved FFmpeg path.");
}

async Task ValidateAiPreparedModelCacheAsync(
    ApplicationConfiguration configuration,
    string packageRootPath)
{
    var assetSourceRootPath = Path.Combine(packageRootPath, "validation-ai-assets");
    Directory.CreateDirectory(assetSourceRootPath);

    var configAssetPath = Path.Combine(assetSourceRootPath, "model.param");
    var weightAssetPath = Path.Combine(assetSourceRootPath, "model.bin");
    File.WriteAllText(configAssetPath, "param");
    File.WriteAllText(weightAssetPath, "bin");

    var cacheType = typeof(AiInterpolationWorkflowService).Assembly.GetType("Vidvix.Services.AI.AiPreparedModelCache", throwOnError: true)
        ?? throw new InvalidOperationException("AiPreparedModelCache type is unavailable.");
    var cache = Activator.CreateInstance(
                    cacheType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object?[] { configuration, new ValidationLogger() },
                    culture: null)
        ?? throw new InvalidOperationException("Failed to create AiPreparedModelCache.");
    var runtimeDescriptor = new AiRuntimeDescriptor
    {
        Id = "validation-rife",
        DisplayName = "Validation RIFE",
        Availability = AiRuntimeAvailability.Available
    };
    var modelDescriptor = new AiRuntimeModelDescriptor
    {
        Id = "validation-model",
        DisplayName = "Validation Model",
        PreparedDirectoryName = "prepared-model",
        Assets = new[]
        {
            new AiRuntimeModelAssetDescriptor
            {
                FileStem = "model",
                DisplayName = "model",
                ConfigPath = configAssetPath,
                WeightPath = weightAssetPath
            }
        }
    };

    var ensurePreparedDirectoryMethod = cacheType.GetMethod(
        "EnsurePreparedModelDirectoryAsync",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(cacheType.FullName, "EnsurePreparedModelDirectoryAsync");
    var ensurePreparedDirectoryTask = (Task<string>)(ensurePreparedDirectoryMethod.Invoke(
        cache,
        new object?[] { runtimeDescriptor, modelDescriptor, CancellationToken.None })
        ?? throw new InvalidOperationException("AiPreparedModelCache returned a null task."));
    var preparedDirectoryPath = await ensurePreparedDirectoryTask.ConfigureAwait(false);

    var expectedLocalRootPath = MutableRuntimeStorage.GetLocalStorageRootPath(
        configuration.LocalDataDirectoryName,
        configuration.RuntimeDirectoryName,
        configuration.AiRuntimeDirectoryName,
        configuration.AiPreparedModelsDirectoryName);

    AssertStartsWith(
        expectedLocalRootPath,
        preparedDirectoryPath,
        "AI prepared model cache should live in local app data rather than beside the packaged app.");
    AssertPathExists(Path.Combine(preparedDirectoryPath, Path.GetFileName(configAssetPath)), "Prepared AI config asset should be copied into the local cache.");
    AssertPathExists(Path.Combine(preparedDirectoryPath, Path.GetFileName(weightAssetPath)), "Prepared AI weight asset should be copied into the local cache.");
}

void ValidateAiWorkflowPaths(
    ApplicationConfiguration configuration,
    string packageRootPath,
    string localValidationRootPath)
{
    var packagedRifeExecutablePath = Path.Combine(
        packageRootPath,
        configuration.RuntimeDirectoryName,
        configuration.AiRuntimeDirectoryName,
        configuration.RifeDirectoryName,
        "Bin",
        configuration.RifeExecutableFileName);
    var monitoredOutputDirectory = Path.Combine(localValidationRootPath, "ai-workflow", "outputs");

    var interpolationWorkingDirectory = InvokePrivateStaticMethod<string>(
        typeof(AiInterpolationWorkflowService),
        "ResolveProcessWorkingDirectory",
        packagedRifeExecutablePath,
        monitoredOutputDirectory);
    var enhancementWorkingDirectory = InvokePrivateStaticMethod<string>(
        typeof(AiEnhancementWorkflowService),
        "ResolveProcessWorkingDirectory",
        packagedRifeExecutablePath,
        monitoredOutputDirectory);

    AssertPathEquals(
        monitoredOutputDirectory,
        interpolationWorkingDirectory,
        "AI interpolation should run external executables from the writable monitored output directory.");
    AssertPathEquals(
        monitoredOutputDirectory,
        enhancementWorkingDirectory,
        "AI enhancement should run external executables from the writable monitored output directory.");

    var interpolationService = CreateUninitializedWithConfiguration<AiInterpolationWorkflowService>(configuration);
    var enhancementService = CreateUninitializedWithConfiguration<AiEnhancementWorkflowService>(configuration);

    var interpolationSessionRootPath = InvokePrivateInstanceMethod<string>(interpolationService, "CreateSessionRootPath");
    var enhancementSessionRootPath = InvokePrivateInstanceMethod<string>(enhancementService, "CreateSessionRootPath");
    var expectedWorkflowRootPath = MutableRuntimeStorage.GetLocalStorageRootPath(
        configuration.LocalDataDirectoryName,
        configuration.RuntimeDirectoryName,
        configuration.AiRuntimeDirectoryName,
        "WorkflowSessions");

    AssertStartsWith(
        expectedWorkflowRootPath,
        interpolationSessionRootPath,
        "AI interpolation session directories should be created in local app data.");
    AssertStartsWith(
        expectedWorkflowRootPath,
        enhancementSessionRootPath,
        "AI enhancement session directories should be created in local app data.");
}

void ValidateMpvRuntimeDirectory(
    ApplicationConfiguration configuration,
    string packageRootPath)
{
    var packagedMpvRuntimePath = Path.Combine(
        packageRootPath,
        configuration.RuntimeDirectoryName,
        configuration.MpvBundledRuntimeDirectoryName);
    Directory.CreateDirectory(packagedMpvRuntimePath);

    var mpvService = CreateUninitializedWithConfiguration<MpvVideoPreviewService>(configuration);
    var resolvedRuntimeDirectory = InvokePrivateInstanceMethod<string>(mpvService, "ResolveRuntimeDirectory");

    AssertPathEquals(
        packagedMpvRuntimePath,
        resolvedRuntimeDirectory,
        "MPV should keep loading from the packaged runtime directory because it is not an extracted mutable runtime.");
}

T CreateUninitializedWithConfiguration<T>(ApplicationConfiguration configuration)
    where T : class
{
    var instance = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    var configurationField = typeof(T).GetField("_configuration", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Type {typeof(T).FullName} does not have a _configuration field.");
    configurationField.SetValue(instance, configuration);
    return instance;
}

TResult InvokePrivateInstanceMethod<TResult>(object instance, string methodName, params object?[] arguments)
{
    var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
    return (TResult)(method.Invoke(instance, arguments)
        ?? throw new InvalidOperationException($"Method {instance.GetType().FullName}.{methodName} returned null."));
}

TResult InvokePrivateStaticMethod<TResult>(Type type, string methodName, params object?[] arguments)
{
    var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(type.FullName, methodName);
    return (TResult)(method.Invoke(null, arguments)
        ?? throw new InvalidOperationException($"Method {type.FullName}.{methodName} returned null."));
}

void AssertPathEquals(string expectedPath, string actualPath, string message)
{
    var normalizedExpectedPath = Path.GetFullPath(expectedPath);
    var normalizedActualPath = Path.GetFullPath(actualPath);
    if (!string.Equals(normalizedExpectedPath, normalizedActualPath, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{message}{Environment.NewLine}Expected: {normalizedExpectedPath}{Environment.NewLine}Actual:   {normalizedActualPath}");
    }
}

void AssertStartsWith(string expectedRootPath, string actualPath, string message)
{
    var normalizedExpectedRootPath = Path.GetFullPath(expectedRootPath);
    var normalizedActualPath = Path.GetFullPath(actualPath);
    if (!normalizedActualPath.StartsWith(normalizedExpectedRootPath, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{message}{Environment.NewLine}Expected root: {normalizedExpectedRootPath}{Environment.NewLine}Actual path:   {normalizedActualPath}");
    }
}

void AssertContainsPath(IEnumerable<string> arguments, string expectedPath, string message)
{
    foreach (var argument in arguments)
    {
        if (string.Equals(
                Path.GetFullPath(argument),
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    throw new InvalidOperationException($"{message}{Environment.NewLine}Missing path: {Path.GetFullPath(expectedPath)}");
}

void AssertPathExists(string path, string message)
{
    if (!File.Exists(path) && !Directory.Exists(path))
    {
        throw new FileNotFoundException(message, path);
    }
}

void TryDeleteDirectory(string directoryPath)
{
    try
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
    catch
    {
    }
}

file sealed class ValidationLogger : ILogger
{
    private readonly List<LogEntry> _entries = new();

    public event EventHandler<LogEntry>? EntryLogged;

    public IReadOnlyList<LogEntry> Entries => _entries;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, exception is null ? message : $"{message}{Environment.NewLine}{exception}");
        _entries.Add(entry);
        EntryLogged?.Invoke(this, entry);
    }
}
