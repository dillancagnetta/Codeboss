using System;

namespace CodeBoss.Jobs.Abstractions;

/// <summary>
/// Gives a job class a stable identity that survives renaming, moving namespace, or being relocated
/// to another assembly.
///
/// <para>Without it a job row is bound to its class by <c>Type.GetType("Namespace.Class, Assembly")</c>.
/// Refactoring the class silently breaks every row pointing at it: the type no longer resolves, and
/// before this library recorded that as a scheduling error the job simply stopped running with
/// nothing anywhere saying why.</para>
///
/// <para>Store the id in <c>ServiceJob.Class</c> and leave <c>ServiceJob.Assembly</c> empty. Choose
/// something descriptive and permanent — it lives in the database.</para>
/// </summary>
/// <example>
/// <code>
/// [JobDefinitionId("goals.recalculate-progress")]
/// public class RecalculateGoalProgressJob : CodeBossJob { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobDefinitionIdAttribute(string id) : Attribute
{
    /// <summary>The stable id. Compared case-insensitively.</summary>
    public string Id { get; } = !string.IsNullOrWhiteSpace(id)
        ? id
        : throw new ArgumentException("A job definition id cannot be empty.", nameof(id));
}
