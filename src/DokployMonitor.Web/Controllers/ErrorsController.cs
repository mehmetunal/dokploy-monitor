using DokployMonitor.Core.Deployments;
using DokployMonitor.Infrastructure.Persistence;
using DokployMonitor.Web.Models;
using DokployMonitor.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DokployMonitor.Web.Controllers;

/// <summary>Hata analizi: ayni kok nedene sahip build hatalarini gruplar.</summary>
public sealed class ErrorsController(DashboardQueryService dashboard, MonitorDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var recentFailures = await db.Deployments
            .Where(d => d.Status == DeploymentStatus.Error || d.Status == DeploymentStatus.Cancelled)
            .OrderByDescending(d => d.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return View(new ErrorAnalysisViewModel
        {
            Signatures = await dashboard.GetTopErrorsAsync(20, ct),
            RecentFailures = recentFailures,
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
