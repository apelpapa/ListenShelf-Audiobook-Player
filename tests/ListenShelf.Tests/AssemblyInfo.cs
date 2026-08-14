// Database recovery, restore, and TestWorkspace cleanup clear SQLite's
// process-wide connection pools. Keep independent fixtures from racing those
// global operations. Explicit concurrency tests still exercise parallel work.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
