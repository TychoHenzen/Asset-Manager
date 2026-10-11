using System.Collections.Generic;

namespace AssetManager.Core;

/// How a whole scan went. `FailedFolders` stayed unreadable through both
/// passes; what was indexed under them is kept. `Retries` counts requests
/// that failed and were retried, recovered or not.
public sealed record ScanReport(ScanSummary Summary, IReadOnlyList<string> FailedFolders, bool Cancelled, int Retries);
