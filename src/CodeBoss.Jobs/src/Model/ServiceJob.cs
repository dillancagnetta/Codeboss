using System;
using System.Collections.Generic;

namespace CodeBoss.Jobs.Model
{
    public class ServiceJob
    {
        public int Id { get; set; }
        public Guid Guid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Assembly { get; set; }
        public string Class { get; set; }
        public string CronExpression { get; set; }
        public bool IsActive { get; set; }
        public string LastStatus { get; set; }
        public string LastStatusMessage { get; set; }
        public Dictionary<string, string> JobParameters { get; set; }
        
        /// <summary>
        /// The never scheduled cron expression. This will only fire the job in the year 2200. This is useful for jobs
        /// that should be run only on demand, such as rebuilding Streak data.
        /// </summary>
        public static string NeverScheduledCronExpression = "0 0 0 1 1 ? 2200";
    }
}