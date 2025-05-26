namespace CodeBoss.MultiTenant
{
    public interface ITenant
    {
        int Id { get; set; }
        string Name { get; set; }

        string ConnectionString { get; set; }
    }
}
