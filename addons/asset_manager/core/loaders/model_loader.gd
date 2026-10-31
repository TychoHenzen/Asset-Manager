@tool
class_name ModelLoader
extends RefCounted

## Loads a .glb/.gltf/.fbx from outside the project into a drawable node tree,
## for the preview panel and the thumbnail renderer alike.
## FBX textures get a second chance. Godot's importer only looks for a texture
## at <model folder>/<referenced name> (fbx_document.cpp:1051), which misses
## textures that ship in a Textures folder elsewhere in the pack, or that the
## author referenced from their own disk or as a .psd that never shipped
## (Synty). ModelTextures finds them the way a person would, and the
## materials the importer left blank get them here.
## Safe on a worker thread: no node here is in the tree yet, and texture
## creation is queued to the rendering server.

## Null when the file could not be read. `filled`, when given, receives
## material name -> texture path for every material this filled in, which
## Send to Project needs to link them the same way in the project.
static func load_model(path: String, filled: Dictionary = {}) -> Node3D:
	# FBXDocument rejects a plain GLTFState (fbx_document.cpp:2261), it needs
	# its own subclass.
	var is_fbx := path.get_extension().to_lower() == "fbx"
	var state: GLTFState = FBXState.new() if is_fbx else GLTFState.new()
	state.handle_binary_image_mode = GLTFState.HANDLE_BINARY_IMAGE_MODE_EMBED_AS_UNCOMPRESSED

	var document: GLTFDocument = FBXDocument.new() if is_fbx else GLTFDocument.new()
	if document.append_from_file(path, state) != OK:
		return null
	var node := document.generate_scene(state)
	if node == null:
		return null

	# GLTF hands back ImporterMeshInstance3D, which carries no drawable mesh.
	node = _convert_importer_meshes(node)

	# Only when the importer actually failed to load something: a material
	# with no texture is often meant that way (Kenney's colour-only parts).
	if is_fbx and _any_image_missing(state):
		_fill_missing_textures(node, path, filled)
	return node as Node3D

## The importer keeps a null placeholder per texture it could not load.
## Checked by hand: Array.has(null) on the typed image array is always false.
static func _any_image_missing(state: GLTFState) -> bool:
	for image in state.get_images():
		if image == null:
			return true
	return false

## Each texture reference the FBX makes that could be found, as
## {reference, path, exact}. `exact` is false for a fallback match (another
## format, a numbered variant), which Godot's importer cannot find by itself.
static func resolved_textures(fbx_path: String) -> Array[Dictionary]:
	var flat: PackedStringArray = ModelTextures.new().Resolve(fbx_path)
	var result: Array[Dictionary] = []
	for i in range(0, flat.size() - 2, 3):
		result.append({"reference": flat[i], "path": flat[i + 1], "exact": flat[i + 2] == "1"})
	return result

static func _fill_missing_textures(root: Node, path: String, filled: Dictionary) -> void:
	var blank := _untextured_materials(root)
	if blank.is_empty():
		return
	var found := resolved_textures(path)
	if found.is_empty():
		return

	var loaded: Dictionary = {}
	for material: BaseMaterial3D in blank:
		var texture_path := _texture_for(material, found)
		if texture_path.is_empty():
			continue
		if not loaded.has(texture_path):
			loaded[texture_path] = _load_texture(texture_path)
		var texture: Texture2D = loaded[texture_path]
		if texture == null:
			continue
		material.albedo_texture = texture
		filled[material.resource_name] = texture_path

## One texture for the whole model is the normal case (an atlas). With
## several, a material takes the one whose name it shares; anything else is
## left alone rather than guessed.
static func _texture_for(material: BaseMaterial3D, found: Array[Dictionary]) -> String:
	var paths: Dictionary = {}
	for entry in found:
		paths[entry["path"]] = true
	if paths.size() == 1:
		return paths.keys()[0]

	var material_name := material.resource_name.to_lower()
	for texture_path: String in paths:
		var stem := texture_path.get_file().get_basename().to_lower()
		if not material_name.is_empty() and (material_name.contains(stem) or stem.contains(material_name)):
			return texture_path
	return ""

## Every distinct material on a mesh surface that has no albedo texture.
static func _untextured_materials(root: Node) -> Array[BaseMaterial3D]:
	var seen: Dictionary = {}
	var result: Array[BaseMaterial3D] = []
	var stack: Array[Node] = [root]
	while not stack.is_empty():
		var node: Node = stack.pop_back()
		stack.append_array(node.get_children())
		var mesh_instance := node as MeshInstance3D
		if mesh_instance == null or mesh_instance.mesh == null:
			continue
		for surface in mesh_instance.mesh.get_surface_count():
			var material := mesh_instance.mesh.surface_get_material(surface) as BaseMaterial3D
			if material == null or material.albedo_texture != null or seen.has(material):
				continue
			seen[material] = true
			result.append(material)
	return result

static func _load_texture(texture_path: String) -> Texture2D:
	var image := Image.load_from_file(texture_path)
	if image == null or image.is_empty():
		push_warning("AssetManager: could not read texture ", texture_path)
		return null
	image.generate_mipmaps()
	return ImageTexture.create_from_image(image)

## Swaps each ImporterMeshInstance3D for a MeshInstance3D carrying the same
## mesh, skin and skeleton. The tree is not in the scene yet, so replaced
## nodes are freed at once.
static func _convert_importer_meshes(node: Node) -> Node:
	if not is_instance_valid(node):
		return node

	var result: Node = node
	if node.get_class() == "ImporterMeshInstance3D":
		var mesh_instance := MeshInstance3D.new()
		mesh_instance.name = node.name
		if node is Node3D:
			mesh_instance.transform = (node as Node3D).transform

		var importer_mesh: Variant = node.get("mesh")
		if importer_mesh != null and importer_mesh.has_method("get_mesh"):
			mesh_instance.mesh = importer_mesh.get_mesh()
		var skin: Variant = node.get("skin")
		if skin != null:
			mesh_instance.skin = skin
		var skeleton_path: Variant = node.get("skeleton_path")
		if skeleton_path != null:
			mesh_instance.skeleton = skeleton_path

		for child in node.get_children():
			node.remove_child(child)
			mesh_instance.add_child(child)

		var parent := node.get_parent()
		if parent != null:
			var index := node.get_index()
			parent.remove_child(node)
			parent.add_child(mesh_instance)
			parent.move_child(mesh_instance, index)
		node.free()
		result = mesh_instance

	for child in result.get_children():
		_convert_importer_meshes(child)
	return result
