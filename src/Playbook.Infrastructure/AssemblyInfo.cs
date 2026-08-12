using System.Runtime.CompilerServices;

// Allows unit tests to exercise pure/deterministic internal logic (e.g. bookmaker priority
// ranking) directly, without needing to mock HTTP or duplicate the algorithm in test code.
[assembly: InternalsVisibleTo("Playbook.Tests")]
