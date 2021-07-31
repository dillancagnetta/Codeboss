using System;
using System.IO;
using CodeBoss.Extensions;

namespace CodeBoss.AspNetCore
{
    internal class BuildVersion
    {
        public string Version { get; set; }
        public string Build { get; set; }
    }

    public interface IBuildVersionInfo
    {
        string Version { get; }
        string Build { get; }
    }

    /// <summary>
    /// Version should be '20211203-1-Production'
    /// </summary>
    public class BuildVersionInfo : IBuildVersionInfo
    {
        private static readonly string _buildFileName = "buildinfo.json";
        private readonly string _buildFilePath;

        public BuildVersionInfo(string contentRootPath)
        {
            _ = contentRootPath ?? throw new ArgumentNullException(nameof(contentRootPath));

            _buildFilePath = Path.Combine(contentRootPath, _buildFileName);

            if (File.Exists(_buildFilePath))
            {
                string fileContents = File.ReadAllText(_buildFilePath);

                var build = fileContents.FromJsonOrNull<BuildVersion>();

                // Overwrite defaults here
                if (fileContents != null)
                {
                    Build = build.Build;
                    Version = build.Version;
                }
            }
        }

        // Defaults
        public string Build { get; } = "1";
        public string Version { get; } = $"{DateTime.UtcNow:yyyyMMdd}-local";
    }
}
