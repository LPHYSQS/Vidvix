using System;
using System.Threading;
using System.Threading.Tasks;
using Vidvix.Core.Interfaces;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class AiWorkspaceViewModel
{
    private readonly IAiRuntimeCatalogService? _aiRuntimeCatalogService;
    private AiRuntimeCatalog? _runtimeCatalog;
    private bool _isRuntimeInspectionInProgress;
    private bool _hasRuntimeInspectionCompleted;

    public async Task InitializeRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (_aiRuntimeCatalogService is null ||
            _isRuntimeInspectionInProgress ||
            _hasRuntimeInspectionCompleted)
        {
            return;
        }

        _isRuntimeInspectionInProgress = true;

        try
        {
            _runtimeCatalog = await _aiRuntimeCatalogService.GetCatalogAsync(cancellationToken);
            _hasRuntimeInspectionCompleted = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Log(LogLevel.Warning, "预加载 AI runtime 目录时发生异常。", exception);
        }
        finally
        {
            _isRuntimeInspectionInProgress = false;
        }
    }
}
