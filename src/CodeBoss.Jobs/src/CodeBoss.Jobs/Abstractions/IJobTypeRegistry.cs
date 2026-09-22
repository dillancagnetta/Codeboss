using System;
using System.Collections.Generic;
using System.Reflection;

namespace CodeBoss.Jobs.Abstractions;

/// <summary>A job type registered under a stable id.</summary>
public sealed record JobTypeRegistration(string Id, Type JobType);

/// <summary>
/// Maps the identifier stored in <c>ServiceJob.Class</c> to a CLR type.
/// </summary>
public interface IJobTypeRegistry
{
    /// <summary>
    /// Resolves a job type, or null when it cannot be found.
    /// </summary>
    /// <param name="idOrTypeName">
    /// A <see cref="JobDefinitionIdAttribute"/> id, or a type's full name for rows written before ids
    /// existed.
    /// </param>
    /// <param name="assembly">Assembly name, used only by the legacy fallback.</param>
    Type Resolve(string idOrTypeName, string assembly = null);

    /// <summary>
    /// The id a job type should be stored under: its <see cref="JobDefinitionIdAttribute"/> if it has
    /// one, otherwise its full type name.
    /// </summary>
    string IdFor(Type jobType);

    /// <summary>Everything registered, keyed by id. Useful for an admin UI's "job class" picker.</summary>
    IReadOnlyDictionary<string, Type> Registrations { get; }
}

/// <inheritdoc />
public sealed class JobTypeRegistry : IJobTypeRegistry
{
    private readonly Dictionary<string, Type> _byId = new(StringComparer.OrdinalIgnoreCase);

    public JobTypeRegistry(IEnumerable<JobTypeRegistration> registrations)
    {
        foreach (var registration in registrations ?? [])
        {
            if (registration?.JobType is null) continue;

            _byId[registration.Id] = registration.JobType;

            // Alias every type by its full name too, so rows written before ids existed keep
            // resolving through the registry rather than falling back to reflection.
            if (registration.JobType.FullName is { } fullName)
            {
                _byId.TryAdd(fullName, registration.JobType);
            }
        }
    }

    public IReadOnlyDictionary<string, Type> Registrations => _byId;

    public Type Resolve(string idOrTypeName, string assembly = null)
    {
        if (string.IsNullOrWhiteSpace(idOrTypeName)) return null;

        if (_byId.TryGetValue(idOrTypeName, out var registered)) return registered;

        // Legacy fallback for rows that predate registration. Never throws on a bad name.
        return string.IsNullOrWhiteSpace(assembly)
            ? Type.GetType(idOrTypeName, throwOnError: false, ignoreCase: true)
            : Type.GetType($"{idOrTypeName}, {assembly}", throwOnError: false, ignoreCase: true);
    }

    public string IdFor(Type jobType) =>
        jobType is null
            ? null
            : jobType.GetCustomAttribute<JobDefinitionIdAttribute>()?.Id ?? jobType.FullName;
}
