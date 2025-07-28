using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Jobs.Model;

namespace CodeBoss.Jobs.Abstractions
{
    public interface IServiceJobRepository
    {
        Task<IEnumerable<ServiceJob>> GetActiveJobsAsync(CancellationToken ct = default);
        Task AddOrUpdateAsync(ServiceJob job, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);
        Task<ServiceJob> GetByIdAsync(int id, CancellationToken ct = default);
        Task UpdateLastStatusMessageAsync(int serviceJobId, string statusMessage, CancellationToken ct = default);
        Task UpdateStatusMessagesAsync(int serviceJobId, string message, string status, CancellationToken ct = default);
        Task ClearStatusesAsync(ServiceJob job, CancellationToken ct = default);
        /*Task UpdateLastStatusMessageAsync(ServiceJob job, string statusMessage, CancellationToken ct = default);
        Task<int> SaveChangesAsync(CancellationToken ct = default);*/
        
        // tenant-aware overloads
        Task<IEnumerable<ServiceJob>> GetActiveJobsAsync(int? tenantId, CancellationToken ct = default);
        Task<ServiceJob> GetByIdAsync(int id, int? tenantId, CancellationToken ct = default);
        Task UpdateLastStatusMessageAsync(int serviceJobId, int? tenantId, string statusMessage, CancellationToken ct = default);
        Task UpdateStatusMessagesAsync(int serviceJobId, int? tenantId, string message, string status, CancellationToken ct = default);
        Task ClearStatusesAsync(ServiceJob job, int? tenantId, CancellationToken ct = default);
    }
}
