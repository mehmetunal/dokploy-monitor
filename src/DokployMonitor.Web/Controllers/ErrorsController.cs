using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DokployMonitor.Web.Controllers;

/// <summary>Hata analizi: ayni kok nedene sahip build hatalarini gruplar.</summary>
public sealed class ErrorsController(DashboardQueryService dashboard, MonitorDbContext db) : Controller
{
    /// <summary>
    /// Gruplanmis hatalar. Filtre (proje / son N gun) FluentValidation ile dogrulanir;
    /// gecersizse sorgu hic calistirilmaz, ekranda sebep gosterilir.
    /// </summary>
    public async Task<IActionResult> Index(
        [FromQuery] ErrorFilter filter,
        [FromServices] IValidator<ErrorFilter> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(filter, ct);
        foreach (var error in validation.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return View(new ErrorAnalysisViewModel
        {
            Signatures = validation.IsValid ? await dashboard.GetTopErrorsAsync(filter, 20, ct) : [],
            RecentFailures = validation.IsValid ? await dashboard.GetRecentFailuresAsync(filter, 50, ct) : [],
            Projects = await dashboard.GetProjectNamesAsync(ct),
            Filter = filter,
        });
    }

    /// <summary>Bir hata imzasini paylasan tum deployment'lar.</summary>
    public async Task<IActionResult> Signature(string hash, CancellationToken ct)
    {
        var signature = await db.ErrorSignatures.FindAsync([hash], ct);
        if (signature is null)
        {
            return NotFound();
        }

        var affected = await db.Deployments
            .Where(d => d.ErrorSignatureHash == hash)
            .OrderByDescending(d => d.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        ViewData["Signature"] = signature;
        return View(affected);
    }
}
