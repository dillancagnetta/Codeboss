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
        Task UpdateLastStatusMessageAsync(ServiceJob job, string statusMessage, CancellationToken ct = default);
        Task<int> SaveChangesAsync(CancellationToken ct = default);
    }
}
