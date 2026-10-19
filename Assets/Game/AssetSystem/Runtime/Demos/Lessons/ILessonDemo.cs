using System.Threading;
using System.Threading.Tasks;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// One restored lesson demo. A demo owns its scenario code and its
    /// simulated backends; it never starts itself, <see cref="LessonDemoBootstrap"/>
    /// is the only entry point.
    /// </summary>
    public interface ILessonDemo
    {
        /// <summary>Short title used in the bootstrap header log.</summary>
        string Title { get; }

        /// <summary>
        /// Runs every scenario of the lesson. Implementations log with the
        /// original lesson prefixes so the old console output stays comparable.
        /// </summary>
        Task RunAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Per-frame tick for demos that drive a scheduler or a load gate.
        /// </summary>
        void Tick();
    }
}
