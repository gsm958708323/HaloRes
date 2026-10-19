using System;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Maps <see cref="LessonDemoKind"/> to its demo implementation. Kept as a
    /// plain switch so adding a lesson only touches two files (the enum and
    /// this factory).
    /// </summary>
    public static class LessonDemoFactory
    {
        public static ILessonDemo Create(LessonDemoKind kind)
        {
            switch (kind)
            {
                case LessonDemoKind.MainThreadRunner:
                    return new L31MainThreadRunnerDemo();

                case LessonDemoKind.Cancellation:
                    return new L32CancellationDemo();

                case LessonDemoKind.SharedLoadRegistry:
                    return new L33SharedLoadRegistryDemo();

                case LessonDemoKind.FileTransaction:
                    return new L34FileTransactionDemo();

                case LessonDemoKind.PatchDownload:
                    return new L35PatchDownloadDemo();

                case LessonDemoKind.Scheduler:
                    return new L36SchedulerDemo();

                case LessonDemoKind.BundleRuntime:
                    return new L44BundleRuntimeDemo();

                case LessonDemoKind.AssetCache:
                    return new L45AssetCacheDemo();

                case LessonDemoKind.AssetMemory:
                    return new L46AssetMemoryDemo();

                case LessonDemoKind.PatchActivation:
                    return new L47PatchActivationDemo();

                case LessonDemoKind.IntegrationScenario:
                    return new L51IntegrationScenarioDemo();

                case LessonDemoKind.ReleaseValidation:
                    return new L52ReleaseValidationDemo();

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unknown lesson demo kind.");
            }
        }
    }
}
