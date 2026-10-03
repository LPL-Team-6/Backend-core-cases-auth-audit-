namespace CaseAuth.Api.Entities;

// Matches the architecture doc's state machine exactly, since Teammates 1/3/4 each drive a
// transition after their pipeline step finishes (extraction, screening, AI review).
public enum CaseStatus
{
    Uploaded,
    Extracted,
    Screened,
    AiReviewed,
    AwaitingDecision,
    Approved,
    Rejected,
    Escalated,
}

public enum DocumentType
{
    GovernmentId,
    ProofOfAddress,
    Financial,
    Other,

    // Added for Teammate 3's screening engine (Screening/ScreeningEngine.cs), whose required-
    // document/field rules are keyed on these types.
    Application,
    W9,
    BeneficialOwnership,
    FormationDocument,
}

// Low/Medium/High (not Info/Warning/Critical) to match the vocabulary Teammate 3's screening
// engine and Teammate 4's AI reviewer are built against.
public enum FindingSeverity
{
    Low,
    Medium,
    High,
}

public enum FindingSource
{
    Ai,
    Manual,

    // Teammate 3's rules engine - deterministic, not model-generated.
    Deterministic,
}

public enum AiRecommendation
{
    Approve,
    Reject,
    Escalate,
}

public enum DecisionOutcome
{
    Approved,
    Rejected,
    Escalated,
}

public enum AuditOutcome
{
    Success,
    Rejected,
    Failure,
}

// One per pipeline step a case needs run - see Pipeline/PipelineJobProcessor.cs. Maps onto
// CaseStateMachine actions: Extract -> "extract", Screen -> "screen", AiReview -> "mark-ai-reviewed".
public enum PipelineJobType
{
    Extract,
    Screen,
    AiReview,
}

public enum PipelineJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}

// Individual vs. business applicant, used by Teammate 3's screening engine to pick which
// documents/fields are required and which sanctions-list entries are comparable. This backend
// only models individual applicants today (Applicant has no Kind column), so
// RuleEngineScreeningService always passes Individual - see its comment.
public enum ApplicantKind
{
    Individual,
    Entity,
}
