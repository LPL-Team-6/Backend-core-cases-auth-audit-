// Must match Pipeline/IAiReviewer.cs's DeterministicAiReviewer.ModelName exactly - that's the
// backend's own label for "no live AI reviewer is configured," not a frontend guess.
export const FALLBACK_MODEL_NAME = 'deterministic-fallback';
