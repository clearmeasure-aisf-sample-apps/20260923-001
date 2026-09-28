// Run test fixtures concurrently to shorten the unit-test stage of the build.
// Fixtures that share mutable static state opt out with [NonParallelizable].
[assembly: NUnit.Framework.Parallelizable(NUnit.Framework.ParallelScope.Fixtures)]
