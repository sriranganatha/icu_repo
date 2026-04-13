namespace GNex.Core.Enums;

/// <summary>
/// Scope of a learning record — determines how widely it applies.
/// Learnings auto-promote: Project → Domain → Global as recurrence increases.
/// </summary>
public enum LearningScope
{
    /// <summary>Applies only to the originating project.</summary>
    Project = 0,

    /// <summary>Applies to all projects within the same domain.</summary>
    Domain = 1,

    /// <summary>Universal — applies to all projects regardless of domain.</summary>
    Global = 2
}
