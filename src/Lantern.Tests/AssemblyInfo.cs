using Xunit;

// Several suites bind real TCP ports (the loopback transport tests) and one of them scans the whole
// port range, so they must not interleave. The suite runs in about a second either way.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
