using CodeBoss.Jobs.Model;
using Quartz;

namespace CodeBoss.Jobs.Abstractions;

public interface IServiceJobService
{
    IJobDetail BuildQuartzJob(ServiceJob job);
    ITrigger BuildQuartzTrigger(ServiceJob job);

    ITrigger BuildJobTrigger(ServiceJob job, int? tenantId);
    IJobDetail BuildQuartzJob(ServiceJob job, int? tenantId);

    JobKey GetJobKey(ServiceJob job, int? tenantId);

}