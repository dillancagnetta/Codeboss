namespace CodeBoss.MultiTenant
{
    public interface ITenant
    {
        string Name { get; set; }

        string ConnectionString { get; set; }
    }
}
