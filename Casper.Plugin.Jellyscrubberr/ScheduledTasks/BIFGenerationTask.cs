using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Casper.Plugin.Jellyscrubberr.Drawing;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Configuration;
using Casper.Plugin.Jellyscrubberr.FileManagement;
using System.Runtime.InteropServices;
using Casper.Plugin.Jellyscrubberr.Configuration;

namespace Casper.Plugin.Jellyscrubberr.ScheduledTasks;

/// <summary>
/// Class BIFGenerationTask.
/// </summary>
public class BIFGenerationTask : IScheduledTask
{
    private static ILogger<BIFGenerationTask> _logger = null!;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILocalizationManager _localization;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly EncodingHelper _encodingHelper;
    private readonly PluginConfiguration _config;

    public BIFGenerationTask(
        ILibraryManager libraryManager,
        ILogger<BIFGenerationTask> logger,
        ILoggerFactory loggerFactory,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        ILocalizationManager localization,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager,
        EncodingHelper encodingHelper)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _localization = localization;
        _mediaEncoder = mediaEncoder;
        _configurationManager = configurationManager;
        _encodingHelper = encodingHelper;
        _config = JellyscrubberrPlugin.Instance!.Configuration;
    }

    /// <inheritdoc />
    public string Name => "Generate BIF Files";

    /// <inheritdoc />
    public string Key => "GenerateBIFFiles";

    /// <inheritdoc />
    public string Description => "Generates BIF files to be used for jellyscrub scrubbing preview.";

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksLibraryCategory");

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
            IsVirtualItem = false,
            Recursive = true

        }).OfType<Video>().ToList();

        var numComplete = 0;
        _logger.LogInformation("Processing {0} items", items.Count);

        foreach (var item in items)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await new VideoProcessor(_loggerFactory, _loggerFactory.CreateLogger<VideoProcessor>(), _mediaEncoder, _configurationManager, _fileSystem, _appPaths, _libraryMonitor, _encodingHelper)
                    .Run(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating trickplay files for {0}: {1}", item.Name, ex);
            }

            numComplete++;
            double percent = numComplete;
            percent /= items.Count;
            percent *= 100;

            progress.Report(percent);
        }

        progress.Report(100);
    }
}
