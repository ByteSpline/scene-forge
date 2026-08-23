using SceneForge.Media.Domain;

namespace SceneForge.Media.Extraction.Intervals;

// A TimeRange tagged with the index of the original scene range it was
// derived from - carried through IntervalSubtractor -> ClipCandidateGenerator
// so every CleanClip can report SourceSceneIndex even after the timeline has
// been reshaped by subtraction and sliding-window candidate generation.
internal readonly record struct IndexedTimeRange(int SourceSceneIndex, TimeRange Range);
