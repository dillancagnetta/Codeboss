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
}