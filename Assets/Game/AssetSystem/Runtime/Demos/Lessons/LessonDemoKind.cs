namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Selects which restored lesson demo <see cref="LessonDemoBootstrap"/> runs.
    ///
    /// The lessons lost their per-lesson bootstrap scripts when the old
    /// Assets/Game/Lessons folder was deleted. They are restored here as
    /// payloads behind a single scene + a single entry point, so a lesson can be
    /// picked from the Inspector (or from code) without adding one scene per
    /// lesson.
    ///
    /// Every demo runs against the current framework (Game.AssetSystem.*), not
    /// against the old per-lesson framework snapshots.
    /// </summary>
    public enum LessonDemoKind
    {
        /// <summary>L3.1 main-thread completion: naive vs pump-dispatched.</summary>
        MainThreadRunner = 0,

        /// <summary>L3.2 waiter cancellation and abandoned late success.</summary>
        Cancellation = 1,

        /// <summary>L3.3 shared load registry reuse / detach / replace.</summary>
        SharedLoadRegistry = 2,

        /// <summary>L3.4 bundle file transaction: commit, activate, rollback.</summary>
        FileTransaction = 3,

        /// <summary>L3.5 patch download with resume and range fallback.</summary>
        PatchDownload = 4,

        /// <summary>L3.6 scheduler priority, frame budget and scope cancel.</summary>
        Scheduler = 5,

        /// <summary>L4.4 bundle runtime dependencies, leases and idle unload.</summary>
        BundleRuntime = 6,

        /// <summary>L4.5 asset cache: shared loads, scopes and idle unload.</summary>
        AssetCache = 7,

        /// <summary>L4.6 memory policy reclaim and diagnostics leak view.</summary>
        AssetMemory = 8,

        /// <summary>L4.7 patch activation guards: platform, hash, handles, safe point.</summary>
        PatchActivation = 9,

        /// <summary>L5.1 end-to-end integration scenario A..H.</summary>
        IntegrationScenario = 10,

        /// <summary>L5.2 release gates: load gate, safe point, version cleaner.</summary>
        ReleaseValidation = 11
    }
}
