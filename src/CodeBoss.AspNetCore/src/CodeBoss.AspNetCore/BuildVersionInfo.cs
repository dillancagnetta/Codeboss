using System;
using System.IO;
using CodeBoss.Extensions;

namespace CodeBoss.AspNetCore
{
    internal class BuildVersion
    {
        public string Version { get; set; }
        public string BuildNumber { get; set; }
        public string BuildId { get; set; }
    }

    public interface IBuildVersionInfo
    {
        string BuildNumber { get; }
        string BuildId { get; }
        string Version { get; }
    }

    public class BuildVersionInfo : IBuildVersionInfo
    {
        private static readonly string _buildFileName = "buildinfo.json";
        private readonly string _buildFilePath;

        public BuildVersionInfo(string contentRootPath)
        {
            _ = contentRootPath ?? throw new ArgumentNullException(nameof(contentRootPath));

            _buildFilePath = Path.Combine(contentRootPath, _buildFileName);

            // Build number format should be yyyyMMdd.# (e.g. 20200308.1)
            if (File.Exists(_buildFilePath))
            {
                string fileContents = File.ReadAllText(_buildFilePath);

                var build = fileContents.FromJsonOrNull<BuildVersion>();

                // First line is build number, second is build id
                if (fileContents != null)
                {
                    BuildNumber = build.BuildNumber;
                    BuildId = build.BuildId;
                    Version = build.Version;
                }
            }
        }

        public string BuildNumber { get; } = DateTime.UtcNow.ToString("yyyyMMdd") + ".0";

        public string BuildId { get; } = "123456";

        public string Version { get; } = "development";
    }
}
