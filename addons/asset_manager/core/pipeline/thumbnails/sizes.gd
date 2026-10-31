@tool
class_name ThumbnailSizes
extends RefCounted

## Thumbnail dimensions. The cache itself (where files live, how they are
## keyed, loading and saving) is ThumbnailService and ThumbnailPaths in C#.

const THUMB_SIZE: int = 256
const ANIMATED_THUMB_SIZE: int = 128

## An animated type stores a strip of frames rather than one image, so its
## pixels cost that multiple to read back and encode. Stills stay large
## because they're cheap either way.
static func size_for(animated: bool) -> int:
	return ANIMATED_THUMB_SIZE if animated else THUMB_SIZE
