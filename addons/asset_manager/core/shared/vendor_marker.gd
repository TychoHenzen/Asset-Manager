@tool
class_name VendorMarker
extends RefCounted

## Marks a folder as a vendor's root (<type>/<vendor>/), so the scan gives its
## assets their type as a tag too. Same file name the C# scanner looks for
## (core/catalog/VendorMarker.cs).

const FILE_NAME: String = "assetmanager.vendor"

## Writes the marker into `vendor_dir` unless one is there. The file holds the
## vendor's name, for anyone browsing the library by hand.
static func ensure(vendor_dir: String) -> void:
	var marker := vendor_dir.path_join(FILE_NAME)
	if FileAccess.file_exists(marker):
		return
	DirAccess.make_dir_recursive_absolute(vendor_dir)
	var file := FileAccess.open(marker, FileAccess.WRITE)
	if file == null:
		push_warning("AssetManager: could not write vendor marker: ", marker)
		return
	file.store_line(vendor_dir.get_file())
	file.close()
