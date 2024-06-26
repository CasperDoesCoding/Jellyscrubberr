using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Casper.Plugin.Jellyscrubberr.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Configuration;
using Casper.Plugin.Jellyscrubberr.FileManagement;
using System.Text.Json;

namespace Casper.Plugin.Jellyscrubberr.Drawing;

public class VideoProcessor
{
    private readonly ILogger<VideoProcessor> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly PluginConfiguration _config;
    private readonly BifManager _bifManager;
    private static ManifestManager _manifestManager = null!;

    public VideoProcessor(
        ILoggerFactory loggerFactory,
        ILogger<VideoProcessor> logger,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        EncodingHelper encodingHelper)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _config = JellyscrubberrPlugin.Instance!.Configuration;
        _bifManager = new BifManager(loggerFactory, loggerFactory.CreateLogger<BifManager>(), mediaEncoder, configurationManager, fileSystem, appPaths, libraryMonitor, encodingHelper);
        _manifestManager = new ManifestManager(loggerFactory, loggerFactory.CreateLogger<ManifestManager>(), fileSystem, libraryMonitor);
    }

    /*
     * Entry point to tell VideoProcessor to generate BIF from item
     */
    public async Task Run(BaseItem item, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing item {0}", item.Name);

        if (!EnableForItem(item, _fileSystem, _config.imageInterval)) return;

        var mediaSources = ((IHasMediaSources)item).GetMediaSources(false)
            .ToList();

        foreach (var mediaSource in mediaSources)
        {
            /*
                * It seems that in Jellyfin multiple files in the same folder exist both as separate items
                * and as sub-media sources under a single head item. Because of this, it is worth a simple check
                * to make sure we are not writing a "sub-items" trickplay data to the metadata folder of the "main" item.
                */
            if (!item.Id.Equals(Guid.Parse(mediaSource.Id)))
            {
                _logger.LogInformation("Skipping file, item is a sub-item");
                continue;
            };

            // check if item has a previous Manifest file and if it matches the current configuration
            bool doesManifestMatch = _manifestManager.ManifestMatches(item);
            if (doesManifestMatch)
            {
                _logger.LogInformation("Manifest matches current configuration, skipping BIF generation");
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Run(item, mediaSource, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task Run(BaseItem item, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        // if bif file already exists delete it
        if (_bifManager.HasBif(item))
        {
            _logger.LogInformation("BIF file already exists, deleting");
            _bifManager.DeleteBif(item);
        }

        await _bifManager.BifWriterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _bifManager.CreateBif(item, mediaSource, cancellationToken).ConfigureAwait(false);
            await _manifestManager.CreateManifest(item).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while creating BIF file");
        }
        finally
        {
            _bifManager.BifWriterSemaphore.Release();
        }
    }

    public static async Task<Manifest?> GetItemManifest(BaseItem item, IFileSystem fileSystem)
    {
        var path = _manifestManager.GetExistingManifestPath(item);
        if (path == null) return null;

        using FileStream openStream = File.OpenRead(path);
        Manifest? newManifest = await JsonSerializer.DeserializeAsync<Manifest>(openStream);

        return newManifest;
    }

    public async Task<bool> DoesItemHaveManifest(BaseItem item, IFileSystem fileSystem)
    {
        Manifest? itemManifest = await GetItemManifest(item, fileSystem);
        if (itemManifest == null) return false;

        return itemManifest.imageWidthResolution == _config.imageWidthResolution;
    }
    public static bool EnableForItem(BaseItem item, IFileSystem fileSystem, int interval)
    {
        if (item is not Video) return false;

        var video = (Video)item;
        var videoType = video.VideoType;

        if (videoType == VideoType.Iso || videoType == VideoType.BluRay || videoType == VideoType.Dvd)
        {
            return false;
        }

        if (video.IsShortcut)
        {
            return false;
        }

        if (!video.IsCompleteMedia)
        {
            return false;
        }

        if (!video.RunTimeTicks.HasValue || video.RunTimeTicks.Value < TimeSpan.FromMilliseconds(interval).Ticks)
        {
            return false;
        }

        if (video.IsFileProtocol)
        {
            if (!fileSystem.FileExists(item.Path))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }
}
