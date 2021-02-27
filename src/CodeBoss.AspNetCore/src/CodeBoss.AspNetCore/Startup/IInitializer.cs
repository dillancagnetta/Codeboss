using System.Threading.Tasks;

namespace CodeBoss.AspNetCore.Startup
{
    public interface IInitializer
    {
        int OrderNumber { get; }
        Task InitializeAsync();
    }
}