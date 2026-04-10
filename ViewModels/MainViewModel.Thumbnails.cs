using System;
using System.Threading.Tasks;
using Vidvix.Core.Models;

namespace Vidvix.ViewModels;

public sealed partial class MainViewModel
{
    private async Task LoadQueueThumbnailAsync(MediaJobViewModel item)
    {
        if (!item.SupportsThumbnail)
        {
            return;
        }

        item.MarkThumbnailLoading();

        try
        {
            var thumbnailUri = await _videoThumbnailService.GetThumbnailUriAsync(item.InputPath).ConfigureAwait(false);
            _dispatcherService.TryEnqueue(() =>
            {
                if (thumbnailUri is not null)
                {
                    item.SetThumbnail(thumbnailUri);
                    return;
                }

                item.MarkThumbnailUnavailable();
            });
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, $"\u52a0\u8f7d\u961f\u5217\u7f29\u7565\u56fe\u65f6\u53d1\u751f\u5f02\u5e38\uff1a{item.InputFileName}", exception);
            _dispatcherService.TryEnqueue(item.MarkThumbnailUnavailable);
        }
    }
}
