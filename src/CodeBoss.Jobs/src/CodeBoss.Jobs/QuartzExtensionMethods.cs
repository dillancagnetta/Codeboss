using System;
using CodeBoss.Extensions;
using CodeBoss.Jobs.Model;

namespace CodeBoss.Jobs;

public static class QuartzExtensionMethods
{
    public static int GetJobIdFromQuartz( this Quartz.IJobExecutionContext context )
    {
        return context.JobDetail.Description.AsInteger();
    }

    public static Type GetCompiledType(this ServiceJob job)
    {
        var jobType = Type.GetType( $"{job.Class}, {job.Assembly}", false, true );
        return jobType;
    }
    
    public static int? GetTenantIdFromQuartz(this Quartz.IJobExecutionContext context)
    {
        if (context.JobDetail.JobDataMap.ContainsKey("TenantId"))
        {
            return context.JobDetail.JobDataMap.GetIntValue("TenantId");
        }
        return null;
    }
    
    public static bool IsValidCronDescription( this string cronExpression )
    {
        return Quartz.CronExpression.IsValidExpression( cronExpression );
    }
}