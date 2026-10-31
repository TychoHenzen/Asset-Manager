using System.Collections.Generic;

namespace AssetManager.Core;

/// How a whole scan went. `FailedFolders` stayed unreadable through both
/// passes; what was indexed under them is kept. `Retries` counts requests
/// that failed and were retried, recovered or not. `Hashed` files were read
/// to find exact copies; `Unhashed` could not be and wait for the next scan.
public sealed record ScanReport(ScanSummary Summary, IReadOnlyList<string> FailedFolders, bool Cancelled, int Retries,
	int Hashed = 0, int Unhashed = 0);
