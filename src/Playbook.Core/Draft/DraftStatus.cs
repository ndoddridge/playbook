namespace Playbook.Core.Draft;

/// <summary>Mirrors real Sleeper draft status values (pre_draft / drafting / paused / complete).</summary>
public enum DraftStatus
{
    Unknown,
    NotStarted,
    Drafting,
    Paused,
    Complete
}
