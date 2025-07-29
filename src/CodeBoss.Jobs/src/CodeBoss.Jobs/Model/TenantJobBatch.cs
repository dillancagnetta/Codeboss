using System;
using System.Collections.Generic;

namespace CodeBoss.Jobs.Model;

public record TenantJobBatch
{
    public int TenantId { get; set; }
    public List<ServiceJob> JobsToSchedule { get; set; } = new();
    public List<ServiceJob> JobsToRemove { get; set; } = new();
    public List<ServiceJob> JobsToUpdate { get; set; } = new();
}

public class JobSchedulingResult
{
    public int TenantId { get; set; }
    public int JobId { get; set; }
    public string Operation { get; set; } // Schedule, Remove, Update
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public DateTime ProcessedAt { get; set; }
}