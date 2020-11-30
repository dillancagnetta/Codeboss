using System.Threading.Tasks;

namespace Codeboss.Types
{
    public interface IInitializer
    {
        Task InitializeAsync();
    }
}