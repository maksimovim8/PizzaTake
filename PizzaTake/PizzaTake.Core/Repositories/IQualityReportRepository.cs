using PizzaTake.Core.Models;

namespace PizzaTake.Core.Repositories;

/// <summary>
/// Repository contract for storing and querying customer quality reports.
/// </summary>
public interface IQualityReportRepository
{
    Task<List<QualityReport>> GetAllAsync();
    Task<QualityReport?> GetByIdAsync(int id);
    Task<List<QualityReport>> GetByOrderIdAsync(int orderId);
    Task SaveAsync(QualityReport report);
    Task DeleteAsync(QualityReport report);
}
