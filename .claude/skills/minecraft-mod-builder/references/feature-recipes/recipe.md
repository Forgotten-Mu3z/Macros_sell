# Recipe: crafting recipe

Add a crafting recipe so players can craft a custom item or block in survival mode. Recipes are pure data — no Java code involved, no loader differences.

## Step 1: Ask the user

- **Output**: which item/block does this recipe produce, and how many?
- **Type**: shaped (specific pattern) or shapeless (any arrangement)?
- **Ingredients**: what goes in?

## Step 2: Create the JSON

Path: `src/main/resources/data/<mod_id>/recipe/<recipe_id>.json` (1.21+) or `src/main/resources/data/<mod_id>/recipes/<recipe_id>.json` (older versions — check the example mod for your MC version).

### Shaped example (9 rubies → 1 ruby block)

```json
{
  "type": "minecraft:crafting_shaped",
  "pattern": [
    "RRR",
    "RRR",
    "RRR"
  ],
  "key": {
    "R": { "item": "<mod_id>:ruby" }
  },
  "result": {
    "id": "<mod_id>:ruby_block",
    "count": 1
  }
}
```

Pattern symbols can be any single character (letters, digits, `#`, etc.) and `key` maps each to an ingredient. Use spaces in the pattern for empty slots. Patterns can be 1×1, 2×2, 2×3, 3×3, or any rectangle up to 3×3.

### Shapeless example (1 ruby block → 9 rubies)

```json
{
  "type": "minecraft:crafting_shapeless",
  "ingredients": [
    { "item": "<mod_id>:ruby_block" }
  ],
  "result": {
    "id": "<mod_id>:ruby",
    "count": 9
  }
}
```

### Tag-based ingredient

Use `"tag": "c:tools/pickaxe"` instead of `"item": "..."` to accept any item in the tag. The `c:` namespace is the convention shared between mod loaders.

## Step 3: Test

`./gradlew runClient`, open the inventory crafting grid or a crafting table, and try the recipe. The recipe book should also list it if discovery conditions are met.

## Note on result format

The JSON shape changed slightly between MC versions:

- **1.20.x and older**: `"result": { "item": "<id>", "count": N }`
- **1.21+**: `"result": { "id": "<id>", "count": N }`

Check what the example mod uses for your target version.

## Common failures

- **Recipe doesn't work in-game**: usually a JSON syntax error (run the file through a JSON validator) or wrong directory (`recipe` vs `recipes`).
- **"Unknown recipe type" in console**: the `type` field is wrong, usually a typo like `crafting_shape` instead of `crafting_shaped`.
- **Player can craft it but recipe book doesn't show it**: missing `category` field — add `"category": "misc"` (or `"building"`, `"redstone"`, `"equipment"`) at the top level.
