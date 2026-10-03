import { FindingResponse } from '@caseauth/angular-client/src/models/finding-response';

export type TodoTag = 'Blocking' | 'FYI';

export interface TodoItem {
  findingId: string;
  tag: TodoTag;
  sentence: string;
}

interface TodoRule {
  tag: TodoTag;
  sentence: string;
}

// Illustrative only - the real screening engine (Teammate 3's module) doesn't exist yet
// (FixtureScreeningService returns no findings), so there's no canonical rule-code vocabulary
// to map against. These are example rule IDs matching the seeded Smyth demo case; anything
// else falls through to the generic sentence below. Per the brief: "All rules are illustrative,
// not LPL policy."
const RULES: Record<string, TodoRule> = {
  NAME_MISMATCH: {
    tag: 'Blocking',
    sentence: "The name on one of the uploaded documents doesn't match the application. Ask for a corrected or updated ID.",
  },
  ADDRESS_MISMATCH: {
    tag: 'Blocking',
    sentence: "The address on one of the uploaded documents doesn't match the application. Ask for a utility bill or an updated ID.",
  },
  DOB_MISMATCH: {
    tag: 'Blocking',
    sentence: "The date of birth on one of the uploaded documents doesn't match the application. Ask for a corrected ID.",
  },
  EXPIRED_ID: {
    tag: 'Blocking',
    sentence: 'The government ID on file has expired. Ask for a current, unexpired ID.',
  },
  LOW_CONFIDENCE_EXTRACTION: {
    tag: 'FYI',
    sentence: 'One of the extracted fields had low confidence - worth a second look, but not necessarily wrong.',
  },
};

export function findingToTodo(finding: FindingResponse): TodoItem {
  const rule = finding.code ? RULES[finding.code] : undefined;
  if (rule) {
    return { findingId: finding.id!, tag: rule.tag, sentence: rule.sentence };
  }

  // Unknown rule ID: fall back to severity to decide Blocking vs FYI, rather than guessing wrong
  // in either direction.
  return {
    findingId: finding.id!,
    tag: finding.severity === 'Low' ? 'FYI' : 'Blocking',
    sentence: `A reviewer flagged "${finding.message ?? finding.code ?? 'something'}" on this case for a second look.`,
  };
}
