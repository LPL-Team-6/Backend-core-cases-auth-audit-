#!/usr/bin/env bash
# Seeds the "Smyth" demo case the Angular workbench's UX acceptance criteria reference
# (name-mismatch and address-mismatch findings, reachable from the queue), using only real,
# in-spec endpoints - there is no backend reset/seed endpoint (see MISSING_ENDPOINTS.md in the
# frontend repo, LPL-Team-6/FrontEnd - "Reset demo" is stubbed, not built). Run this against a
# running CaseAuth.Api instead:
#
#   BASE_URL=http://localhost:5020 ./scripts/seed-smyth-case.sh
#
# Safe to re-run: each run creates a new case rather than reusing one.
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:5020}"

json_get() {
  python3 -c "import sys,json; print(json.load(sys.stdin)$1)"
}

echo "==> Creating the Smyth case"
CASE=$(curl -sf -X POST "$BASE_URL/api/cases" -H "X-Dev-User: analyst1" -H "Content-Type: application/json" \
  -d '{"applicantFullName":"Robert Smyth"}')
CASE_ID=$(echo "$CASE" | json_get "['id']")
echo "    caseId=$CASE_ID"

echo "==> Uploading two documents"
ID_FILE="$(mktemp)"
ADDRESS_FILE="$(mktemp)"
echo "specimen government id" > "$ID_FILE"
echo "specimen utility bill" > "$ADDRESS_FILE"

ID_DOC=$(curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/documents" -H "X-Dev-User: analyst1" \
  -F "documentType=GovernmentId" -F "file=@$ID_FILE;type=application/pdf;filename=smyth-id.pdf")
ID_DOC_ID=$(echo "$ID_DOC" | json_get "['id']")

ADDRESS_DOC=$(curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/documents" -H "X-Dev-User: analyst1" \
  -F "documentType=ProofOfAddress" -F "file=@$ADDRESS_FILE;type=application/pdf;filename=smyth-address.pdf")
ADDRESS_DOC_ID=$(echo "$ADDRESS_DOC" | json_get "['id']")
rm -f "$ID_FILE" "$ADDRESS_FILE"

echo "==> Recording extracted fields (deliberately mismatched - that's the point)"
ID_FIELDS=$(curl -sf -X POST "$BASE_URL/api/documents/$ID_DOC_ID/extracted-fields" -H "X-Dev-User: analyst1" \
  -H "Content-Type: application/json" \
  -d '{"fields":[{"fieldName":"FullName","fieldValue":"Robert Smith","confidence":0.92},{"fieldName":"Address","fieldValue":"123 Main St, Springfield","confidence":0.88}]}')
ID_NAME_FIELD_ID=$(echo "$ID_FIELDS" | json_get "[0]['id']")
ID_ADDRESS_FIELD_ID=$(echo "$ID_FIELDS" | json_get "[1]['id']")

ADDRESS_FIELDS=$(curl -sf -X POST "$BASE_URL/api/documents/$ADDRESS_DOC_ID/extracted-fields" -H "X-Dev-User: analyst1" \
  -H "Content-Type: application/json" \
  -d '{"fields":[{"fieldName":"FullName","fieldValue":"Robert Smyth","confidence":0.95},{"fieldName":"Address","fieldValue":"456 Oak Ave, Springfield","confidence":0.90}]}')
ADDRESS_NAME_FIELD_ID=$(echo "$ADDRESS_FIELDS" | json_get "[0]['id']")
ADDRESS_ADDRESS_FIELD_ID=$(echo "$ADDRESS_FIELDS" | json_get "[1]['id']")

echo "==> Recording findings (rule IDs match core/finding-todo.ts in the frontend repo)"
curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/findings" -H "X-Dev-User: analyst1" -H "Content-Type: application/json" \
  -d "{\"code\":\"NAME_MISMATCH\",\"severity\":\"High\",\"message\":\"The name on the government ID doesn't match the proof of address.\",\"score\":0.9,\"sourceFieldIds\":[\"$ID_NAME_FIELD_ID\",\"$ADDRESS_NAME_FIELD_ID\"]}" \
  > /dev/null
curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/findings" -H "X-Dev-User: analyst1" -H "Content-Type: application/json" \
  -d "{\"code\":\"ADDRESS_MISMATCH\",\"severity\":\"Medium\",\"message\":\"The address on the government ID doesn't match the proof of address.\",\"score\":0.75,\"sourceFieldIds\":[\"$ID_ADDRESS_FIELD_ID\",\"$ADDRESS_ADDRESS_FIELD_ID\"]}" \
  > /dev/null

echo "==> Advancing the case to AwaitingDecision"
curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/extract" -H "X-Dev-User: analyst1" > /dev/null
curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/screen" -H "X-Dev-User: analyst1" > /dev/null
# modelName is literally "deterministic-fallback" - the real, current state of this system
# (Bedrock was never wired up; DeterministicAiReviewer is the only registered IAiReviewer), not
# a staged fallback scenario. See Pipeline/IAiReviewer.cs.
curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/ai-reviews" -H "X-Dev-User: analyst1" -H "Content-Type: application/json" \
  -d '{"modelName":"deterministic-fallback","modelVersion":"1.0","recommendation":"Escalate","rationale":"No live AI reviewer is configured; escalating for manual review."}' \
  > /dev/null
curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/mark-ai-reviewed" -H "X-Dev-User: analyst1" > /dev/null
curl -sf -X POST "$BASE_URL/api/cases/$CASE_ID/request-decision" -H "X-Dev-User: analyst1" > /dev/null

echo "==> Done. Smyth case is ready at AwaitingDecision:"
echo "    $CASE_ID"
echo "    Open the frontend queue (http://localhost:4200/cases) as analyst1 for the advisor"
echo "    to-do list, or as supervisor for the reviewer's findings/decision flow."
