using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

/// <summary>
/// SQLite repository for customer quality reports with photo paths and GPS coordinates.
/// </summary>
public sealed class QualityReportRepository : SqliteRepositoryBase, IQualityReportRepository
{
    private readonly ILogger<QualityReportRepository> logger;

    public QualityReportRepository(SqliteDatabase database, ILogger<QualityReportRepository>? logger = null)
        : base(database)
    {
        this.logger = logger ?? NullLogger<QualityReportRepository>.Instance;
    }

    public async Task<List<QualityReport>> GetAllAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<QualityReport>()
            .OrderByDescending(report => report.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task<QualityReport?> GetByIdAsync(int id)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<QualityReport>()
            .Where(report => report.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<QualityReport>> GetByOrderIdAsync(int orderId)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<QualityReport>()
            .Where(report => report.OrderId == orderId)
            .OrderByDescending(report => report.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task SaveAsync(QualityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var connection = await GetConnectionAsync();
        await InsertOrUpdateAsync(connection, report, report.Id);
        logger.LogInformation(
            "Saved quality report (Id={ReportId}, OrderNumber={OrderNumber}, HasPhoto={HasPhoto}, HasCoords={HasCoords})",
            report.Id,
            report.OrderNumber,
            report.HasPhoto,
            report.HasCoordinates);
    }

    public async Task DeleteAsync(QualityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var connection = await GetConnectionAsync();
        await connection.DeleteAsync(report);
        logger.LogInformation("Deleted quality report (Id={ReportId})", report.Id);
    }
}
