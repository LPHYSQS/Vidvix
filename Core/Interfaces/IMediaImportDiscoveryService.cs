using System.Collections.Generic;
using Vidvix.Core.Models;

namespace Vidvix.Core.Interfaces;

public interface IMediaImportDiscoveryService
{
    MediaImportDiscoveryResult Discover(IEnumerable<string> inputPaths);
}
