using BenchmarkDotNet.Running;

// Pass --filter '*' to run everything, or e.g. --filter '*Pipeline*' for a subset.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
