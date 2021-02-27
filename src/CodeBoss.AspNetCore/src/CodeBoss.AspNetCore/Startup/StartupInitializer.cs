using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CodeBoss.AspNetCore.Startup
{
    public class StartupInitializer : IStartupInitializer
    {
        private readonly IEnumerable<IInitializer> _initializers;
        private readonly ILogger<StartupInitializer> _logger;

        public StartupInitializer(IEnumerable<IInitializer> initializers, ILogger<StartupInitializer> logger)
        {
            _initializers = initializers;
            _logger = logger;
        }

        public int OrderNumber { get; } = -1;

        public Task InitializeAsync()
        {
            _logger.LogInformation($"Startup Initializer found: '{_initializers.Count()}', initializers to run.");

            foreach(var initializer in _initializers.OrderBy(x => x.OrderNumber))
            {
                // Returns the Task i.e. does not await the result
                return initializer.InitializeAsync();
            }

            return Task.CompletedTask;
        }
    }
}