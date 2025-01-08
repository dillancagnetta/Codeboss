using CodeBoss.Jobs.Model;
using Quartz;

namespace CodeBoss.Jobs.Abstractions;

public interface IServiceJobService
{
    IJobDetail BuildQuartzJob(ServiceJob job);
    ITrigger BuildQuartzTrigger(ServiceJob job);
}