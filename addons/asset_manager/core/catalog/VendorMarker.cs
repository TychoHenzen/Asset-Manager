using System.Collections.Generic;

namespace AssetManager.Core;

/// A file named assetmanager.vendor in a folder marks it as a vendor's root
/// (normally <type>/<vendor>/). Assets below a marked folder get their type as
/// a tag too, so "kenney" + "models" filters across the whole library.
/// Not a dot-file: Samba hides those by default and the scanner skips hidden
/// files. GDScript writes it under the same name (core/shared/vendor_marker.gd).
public static class VendorMarker
{
	public const string FileName = "assetmanager.vendor";

	/// True when `relPath`'s folder, or any folder above it, is in `vendorRoots`.
	public static bool IsUnderVendorRoot(string relPath, IReadOnlySet<string> vendorRoots)
	{
		if (vendorRoots.Count == 0)
			return false;
		var slash = relPath.IndexOf('/');
		while (slash > 0)
		{
			if (vendorRoots.Contains(relPath[..slash]))
				return true;
			slash = relPath.IndexOf('/', slash + 1);
		}
		return false;
	}
}
