// Run test fixtures concurrently to shorten the unit-test stage of the build.
// Fixtures that share mutable static state opt out with [NonParallelizable].
[assembly: Parallelizable(ParallelScope.Fixtures)]
