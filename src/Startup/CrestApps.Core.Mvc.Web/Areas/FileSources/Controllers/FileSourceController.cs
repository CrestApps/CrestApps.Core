using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.FileSources.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.FileSources.Controllers;

[Area("FileSources")]
[Authorize(Policy = "Admin")]
public sealed class FileSourceController : Controller
{
    private readonly ISourceCatalogManager<WebCrawler> _manager;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IFileSourceRunService _runService;
    private readonly IWebCrawlStateStore _stateStore;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IngestionConnectorDescriptor[] _connectors;

    public FileSourceController(
        ISourceCatalogManager<WebCrawler> manager,
        IAIDataSourceStore dataSourceStore,
        IFileSourceRunService runService,
        IWebCrawlStateStore stateStore,
        IAIDeploymentManager deploymentManager,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<IngestionConnectorOptions> connectorOptions)
    {
        _manager = manager;
        _dataSourceStore = dataSourceStore;
        _runService = runService;
        _stateStore = stateStore;
        _deploymentManager = deploymentManager;
        _dataProtectionProvider = dataProtectionProvider;
        _connectors = connectorOptions.Value.Connectors
            .OrderBy(connector => connector.DisplayName.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IActionResult> Index()
    {
        var records = await _manager.GetAllAsync();

        // A record's source is either a crawl strategy or an ingestion connector, and that is the only
        // thing separating a web crawler from a file source. Anything not backed by a registered connector
        // belongs on the Web Crawlers screen.
        var fileSources = records
            .Where(record => IsRegisteredConnector(record.Source))
            .ToList();

        return View(fileSources);
    }

    public async Task<IActionResult> Create()
    {
        var model = new FileSourceViewModel
        {
            Source = _connectors.Length > 0 ? _connectors[0].Name : null,
        };

        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FileSourceViewModel model)
    {
        if (!IsRegisteredConnector(model.Source))
        {
            ModelState.AddModelError(nameof(FileSourceViewModel.Source), "Select where the files are read from.");
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        var fileSource = await _manager.NewAsync(model.Source, cancellationToken: HttpContext.RequestAborted);
        model.ApplyTo(fileSource, _dataProtectionProvider);

        await ValidateAsync(model, fileSource);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        await _manager.CreateAsync(fileSource);
        TempData["SuccessMessage"] = "File source created successfully. Initial synchronization has been queued.";

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var fileSource = await _manager.FindByIdAsync(id);

        if (fileSource == null || !IsRegisteredConnector(fileSource.Source))
        {
            return NotFound();
        }

        var model = FileSourceViewModel.FromFileSource(fileSource);
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(FileSourceViewModel model)
    {
        var fileSource = await _manager.FindByIdAsync(model.ItemId);

        if (fileSource == null || !IsRegisteredConnector(fileSource.Source))
        {
            return NotFound();
        }

        if (!IsRegisteredConnector(model.Source))
        {
            ModelState.AddModelError(nameof(FileSourceViewModel.Source), "Select where the files are read from.");
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        model.ApplyTo(fileSource, _dataProtectionProvider);

        await ValidateAsync(model, fileSource);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        await _manager.UpdateAsync(fileSource);
        TempData["SuccessMessage"] = "File source updated successfully. Synchronization has been queued.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var fileSource = await _manager.FindByIdAsync(id);

        if (fileSource != null && IsRegisteredConnector(fileSource.Source))
        {
            await _manager.DeleteAsync(fileSource);
            TempData["SuccessMessage"] = "File source deleted successfully. Knowledge-base cleanup has been queued.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(string id)
    {
        var fileSource = await _manager.FindByIdAsync(id);

        if (fileSource == null || !IsRegisteredConnector(fileSource.Source))
        {
            return NotFound();
        }

        var run = await _runService.RunAsync(fileSource, HttpContext.RequestAborted);

        if (run.Status == FileSourceRunStatus.Failed)
        {
            TempData["ErrorMessage"] = run.Error;
        }
        else
        {
            var note = run.DiscoveryCompleted
                ? string.Empty
                : " The source was only partly listed, so nothing was removed.";

            TempData["SuccessMessage"] =
                $"Synchronization finished: {run.ItemsIndexed} indexed, {run.ItemsSkipped} unchanged, {run.ItemsDeleted} removed, {run.ItemsFailed} failed.{note}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetState(string id)
    {
        var fileSource = await _manager.FindByIdAsync(id);

        if (fileSource == null || !IsRegisteredConnector(fileSource.Source))
        {
            return NotFound();
        }

        // Forgetting what was read, not what was stored. The next run sees every file as new and re-reads
        // it, which is how an operator recovers from a bad run without deleting the data source.
        await _stateStore.DeleteByCrawlerIdAsync(fileSource.ItemId, HttpContext.RequestAborted);

        TempData["SuccessMessage"] = "Stored progress cleared. The next run will re-read every file.";

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Tells a file source's connector apart from a crawl strategy. The connectors are whatever the host
    /// registered, so this screen never carries a list of its own.
    /// </summary>
    /// <param name="source">The record's stored source.</param>
    /// <returns><see langword="true"/> when the source names a registered connector.</returns>
    /// <remarks>
    /// A connector that was renamed still claims the name it was registered under before, so a record
    /// written before the rename stays on this screen rather than disappearing off both.
    /// </remarks>
    private bool IsRegisteredConnector(string source)
    {
        return !string.IsNullOrWhiteSpace(source) &&
            _connectors.Any(connector => connector.Matches(source));
    }

    private async Task ValidateAsync(FileSourceViewModel model, WebCrawler fileSource)
    {
        var validation = await _manager.ValidateAsync(fileSource);

        foreach (var error in validation.Errors)
        {
            var memberNames = error.MemberNames?.Any() == true ? error.MemberNames : [string.Empty];

            foreach (var memberName in memberNames)
            {
                ModelState.AddModelError(MapValidationMemberName(model, memberName), error.ErrorMessage);
            }
        }
    }

    private static string MapValidationMemberName(FileSourceViewModel model, string memberName)
    {
        return memberName switch
        {
            nameof(WebCrawler.DisplayText) => nameof(FileSourceViewModel.DisplayText),
            nameof(WebCrawler.AIDataSourceId) => nameof(FileSourceViewModel.AIDataSourceId),
            nameof(WebCrawler.Source) => nameof(FileSourceViewModel.Source),
            nameof(LocalFolderIndexerMetadata.RootPath) => model.IsFileSystem
                ? nameof(FileSourceViewModel.LocalRootPath)
                : nameof(FileSourceViewModel.RemoteRootPath),
            nameof(FtpConnectionMetadata.Host) => nameof(FileSourceViewModel.RemoteHost),
            nameof(SftpConnectionMetadata.Username) => nameof(FileSourceViewModel.RemoteUsername),
            nameof(SftpConnectionMetadata.Password) => nameof(FileSourceViewModel.RemotePassword),
            _ => memberName,
        };
    }

    private async Task PopulateDropdownsAsync(FileSourceViewModel model)
    {
        var dataSources = await _dataSourceStore.GetAsync(AIDataSourceSourceTypes.File);

        model.Connectors = _connectors
            .Select(connector => new SelectListItem(connector.DisplayName.Value, connector.Name))
            .ToList();

        model.DataSources = dataSources
            .Select(dataSource => new SelectListItem(dataSource.DisplayText, dataSource.ItemId))
            .ToList();

        model.VisionDeployments = await BuildDeploymentsAsync(AIDeploymentSlotNames.Vision);
        model.UtilityDeployments = await BuildDeploymentsAsync(AIDeploymentSlotNames.Utility);
    }

    /// <summary>
    /// Lists the deployments that qualify for a slot, with an empty entry meaning "use the host's".
    /// </summary>
    /// <param name="slotName">The slot.</param>
    /// <returns>The list items.</returns>
    private async Task<List<SelectListItem>> BuildDeploymentsAsync(string slotName)
    {
        var deployments = await _deploymentManager.GetAllBySlotAsync(slotName);

        var items = new List<SelectListItem>
        {
            new("Use the application default", string.Empty),
        };

        items.AddRange(deployments.Select(deployment => new SelectListItem(deployment.Name, deployment.Name)));

        return items;
    }
}
