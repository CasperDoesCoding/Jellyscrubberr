using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Casper.Plugin.Jellyscrubberr.Configuration;
using Casper.Plugin.Jellyscrubberr.Drawing;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using Casper.Plugin.Jellyscrubberr.FileManagement;

namespace Casper.Plugin.Jellyscrubberr.Api;

/// <summary>
/// Controller for accessing trickplay data.
/// </summary>
[ApiController]
[Route("Trickplay")]
public class TrickplayController : ControllerBase
{
    private readonly Assembly _assembly;
    private readonly string _trickplayScriptPath;

    private readonly ILogger<TrickplayController> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IApplicationPaths _appPaths;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly EncodingHelper _encodingHelper;
    private readonly PluginConfiguration _config;
    private static ManifestManager _manifestManager = null!;
    private static BifManager _bifManager = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayController"/> class.
    /// </summary>
    public TrickplayController(
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ILogger<TrickplayController> logger,
        ILoggerFactory loggerFactory,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager,
        EncodingHelper encodingHelper)
    {
        _assembly = Assembly.GetExecutingAssembly();
        _trickplayScriptPath = GetType().Namespace + ".trickplay.js";

        _libraryManager = libraryManager;
        _logger = logger;
        _fileSystem = fileSystem;
        _loggerFactory = loggerFactory;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _mediaEncoder = mediaEncoder;
        _configurationManager = configurationManager;
        _encodingHelper = encodingHelper;
        _config = JellyscrubberrPlugin.Instance!.Configuration;
        _manifestManager = new ManifestManager(loggerFactory, loggerFactory.CreateLogger<ManifestManager>(), fileSystem, libraryMonitor);
        _bifManager = new BifManager(loggerFactory, loggerFactory.CreateLogger<BifManager>(), mediaEncoder, configurationManager, fileSystem, appPaths, libraryMonitor, encodingHelper);
    }

    /// <summary>
    /// Get embedded javascript file for client-side code.
    /// </summary>
    /// <response code="200">Javascript file successfully returned.</response>
    /// <response code="404">File not found.</response>
    /// <returns>The "trickplay.js" embedded file.</returns>
    [HttpGet("ClientScript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var scriptStream = _assembly.GetManifestResourceStream(_trickplayScriptPath);

        if (scriptStream != null)
        {
            return File(scriptStream, "application/javascript");
        }

        return NotFound();
    }

    /// <summary>
    /// Get an video's trickplay manifest.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">Manifest successfully found and returned.</response>
    /// <response code="404">Item not found.</response>
    /// <response code="503">If on-demand generation is enabled, this indicates the server hasn't completed generation.</response>
    /// <returns>A JSON response as read from manfiest file, or a <see cref="NotFoundResult"/>.</returns>
    [HttpGet("{itemId}/GetManifest")]
    [Authorize(Policy = "DefaultAuthorization")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetManifest([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);

        if (item != null)
        {
            var path = _manifestManager.GetExistingManifestPath(item);
            if (path != null)
            {
                return PhysicalFile(path, MediaTypeNames.Application.Json);
            }
        }

        return NotFound();
    }

    /// <summary>
    /// Gets specific BIF file for a video.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <response code="200">BIF file successfully found and returned.</response>
    /// <response code="404">BIF file not found.</response>
    /// <response code="503">If on-demand generation is enabled, this indicates the server hasn't completed generation.</response>
    /// <returns>Associated BIF file, or a <see cref="NotFoundResult"/>.</returns>
    [HttpGet("{itemId}/GetBIF")]
    [HttpGet("{itemId}/GetBIF.bif")]
    [Authorize(Policy = "DefaultAuthorization")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [Produces(MediaTypeNames.Application.Octet)]
    public ActionResult GetBIF([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);

        if (item != null)
        {
            var path = _bifManager.GetExistingBifPath(item);
            if (path != null)
            {
                return PhysicalFile(path, MediaTypeNames.Application.Octet);
            }
            else if (_config.OnDemandGeneration)
            {
                _ = new VideoProcessor(_loggerFactory, _loggerFactory.CreateLogger<VideoProcessor>(), _mediaEncoder, _configurationManager, _fileSystem, _appPaths, _libraryMonitor, _encodingHelper)
                    .Run(item, CancellationToken.None).ConfigureAwait(false);
                return StatusCode(503);
            }
        }

        return NotFound();
    }
}
