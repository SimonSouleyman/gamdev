using Drift.Core;

namespace Drift.SaveSystem
{
    // The pieces of "sammeln und beobachten". Every entry point that shows one of these asks WatchRules first, so a
    // new call site cannot quietly reintroduce it in a mode that does not want it.
    public enum WatchFeature
    {
        // Marking species seen/collected in the journal of the run (and, through it, in the book across runs).
        DiscoveryRecord,
        // "Zum ersten Mal gesehen", "Neu auf deiner Insel" and every other line of the news chip.
        DiscoveryToast,
        // The floating camera icon over a creature doing the move of an open photo task, and its hint line.
        PhotoTaskCue,
        // Ticking a photo task off (book entry, picture) and its "Fotoaufgabe erfüllt!".
        PhotoTaskRecord,
        // The hint chips over islands and the arrows at the screen edge ("Neue Art", "Schauspiel", "Feuer", ...).
        IslandHint,
        // Tapping an animal: its card and the close watch camera.
        Watch,
        // The photo camera.
        PhotoMode,
        // The species journal.
        Journal,
        // The album of saved photos.
        PhotoAlbum,
    }

    // Watching, collecting and photographing are the cozy game. The adventure run is a race ("es geht nur ums
    // Rennen"), so none of it exists there - not the toasts, not the camera cues, not the journal, and nothing is
    // written to the cozy books while it runs. The album is the one exception: it is not part of a run at all, it
    // hangs off the title screen next to both modes, so it stays reachable.
    public static class WatchRules
    {
        public static bool Allowed(WatchFeature feature, GameMode mode) =>
            feature == WatchFeature.PhotoAlbum || mode == GameMode.Cozy;

        public static bool Allowed(WatchFeature feature) => Allowed(feature, GameModes.Current);

        // True while the whole package is on; the call sites that gate several features at once use this.
        public static bool Enabled => GameModes.Current == GameMode.Cozy;
    }
}
