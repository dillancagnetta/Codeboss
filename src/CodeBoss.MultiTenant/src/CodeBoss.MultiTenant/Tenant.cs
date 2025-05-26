﻿using System.ComponentModel.DataAnnotations;

namespace CodeBoss.MultiTenant
{
    public class Tenant : ITenant
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(500)]
        public string ConnectionString { get; set; }
    }
}
