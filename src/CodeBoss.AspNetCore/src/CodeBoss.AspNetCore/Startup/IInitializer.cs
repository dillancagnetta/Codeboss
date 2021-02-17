using System.Threading.Tasks;

namespace CodeBoss.AspNetCore.Startup
{
    public interface IInitializer
    {
        Task InitializeAsync();
    }
}