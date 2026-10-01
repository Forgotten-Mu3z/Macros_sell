# Recipe: custom block

Add a custom block to the mod. A block always needs a corresponding `BlockItem` so the player can carry and place it, so this recipe touches both block and item registries.

## What you'll create

- A registered `Block`.
- A `BlockItem` so it appears in inventories.
- Block model + blockstate JSONs.
- Item model JSON (referencing the block model).
- Texture(s).
- Translation entry.
- Loot table (so the block drops itself when broken).

## Step 1: Ask the user

- **Block ID** (snake_case).
- **Display name**.
- **Material**: stone-like (pickaxe-mineable, hard) or wood-like (axe-mineable, softer) or something else? This determines `BlockBehaviour.Properties`.
- **Hardness/resistance**: defaults are fine for most cases; ask only if they want something specific.

## Step 2: Create the Blocks class

Place at `src/main/java/<package>/block/ModBlocks.java`.

### Fabric

> The example below uses **Yarn mapping names** (`AbstractBlock.Settings`, `Identifier`, `Item.Settings`). Modern Fabric templates default to **Mojang mappings**, where the equivalents are `BlockBehaviour.Properties`, `ResourceLocation`, and `Item.Properties`. Check `build.gradle`'s `mappings` line and translate as needed.

```java
package <package>.block;

import net.minecraft.block.AbstractBlock;
import net.minecraft.block.Block;
import net.minecraft.item.BlockItem;
import net.minecraft.item.Item;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.minecraft.util.Identifier;
import <package>.ExampleMod;

public class ModBlocks {
    public static final Block RUBY_BLOCK = registerBlock("ruby_block",
        new Block(AbstractBlock.Settings.create().strength(4.0f).requiresTool()));

    private static Block registerBlock(String name, Block block) {
        registerBlockItem(name, block);
        return Registry.register(Registries.BLOCK, Identifier.of(ExampleMod.MOD_ID, name), block);
    }

    private static void registerBlockItem(String name, Block block) {
        Registry.register(Registries.ITEM, Identifier.of(ExampleMod.MOD_ID, name),
            new BlockItem(block, new Item.Settings()));
    }

    public static void registerModBlocks() {
        // Triggers class loading.
    }
}
```

Call `ModBlocks.registerModBlocks()` from `onInitialize()`.

### NeoForge

```java
package <package>.block;

import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.Item;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockBehaviour;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredBlock;
import net.neoforged.neoforge.registries.DeferredRegister;
import <package>.ExampleMod;
import <package>.item.ModItems;

public class ModBlocks {
    public static final DeferredRegister.Blocks BLOCKS =
        DeferredRegister.createBlocks(ExampleMod.MODID);

    public static final DeferredBlock<Block> RUBY_BLOCK = registerBlock("ruby_block",
        () -> new Block(BlockBehaviour.Properties.of().strength(4.0f).requiresCorrectToolForDrops()));

    private static <T extends Block> DeferredBlock<T> registerBlock(String name, java.util.function.Supplier<T> block) {
        DeferredBlock<T> toReturn = BLOCKS.register(name, block);
        ModItems.ITEMS.register(name, () -> new BlockItem(toReturn.get(), new Item.Properties()));
        return toReturn;
    }

    public static void register(IEventBus bus) {
        BLOCKS.register(bus);
    }
}
```

Call `ModBlocks.register(modEventBus)` in the `@Mod` constructor, **before** the items register call if you're using the shared `ModItems.ITEMS` pattern above.

## Step 3: Blockstate JSON

Place at `src/main/resources/assets/<mod_id>/blockstates/<block_id>.json`. For a simple all-sides-same block:

```json
{
  "variants": {
    "": { "model": "<mod_id>:block/<block_id>" }
  }
}
```

## Step 4: Block model JSON

Place at `src/main/resources/assets/<mod_id>/models/block/<block_id>.json`:

```json
{
  "parent": "minecraft:block/cube_all",
  "textures": {
    "all": "<mod_id>:block/<block_id>"
  }
}
```

## Step 5: Item model JSON

Place at `src/main/resources/assets/<mod_id>/models/item/<block_id>.json`:

```json
{
  "parent": "<mod_id>:block/<block_id>"
}
```

This makes the inventory icon match the block's appearance.

## Step 6: Texture

**Generate a 16×16 PNG texture for the block by default** — save it at `src/main/resources/assets/<mod_id>/textures/block/<block_id>.png`. Make the texture tile cleanly (the left edge should match the right edge, top should match bottom) since blocks repeat across surfaces. For blocks with the simple `cube_all` parent model, one texture file is used for all six faces; if the user wants per-face textures (different top/bottom/sides like a furnace or grass block), switch the block model parent to `minecraft:block/cube_bottom_top` or `minecraft:block/orientable` and generate the additional texture files.

Tell the user briefly what you drew. Only fall back to `assets/textures/placeholder_block.png` if the user has said they'll handle textures themselves or generation is failing — never leave the file missing, since that produces the purple-and-black "missing texture" pattern in-game.

## Step 7: Translation

Add to `src/main/resources/assets/<mod_id>/lang/en_us.json`:

```json
{
  "block.<mod_id>.<block_id>": "<Display Name>"
}
```

## Step 8: Loot table

Without a loot table, breaking the block drops nothing. Place at `src/main/resources/data/<mod_id>/loot_tables/blocks/<block_id>.json`:

```json
{
  "type": "minecraft:block",
  "pools": [
    {
      "rolls": 1,
      "entries": [
        {
          "type": "minecraft:item",
          "name": "<mod_id>:<block_id>"
        }
      ],
      "conditions": [
        { "condition": "minecraft:survives_explosion" }
      ]
    }
  ]
}
```

For MC 1.21+, the path is `data/<mod_id>/loot_table/blocks/...` (singular `loot_table`). Check the example mod for the exact path — Mojang has shuffled this between versions.

## Step 9: Mining tool tag (optional but expected for pickaxe-mineable blocks)

If the block uses `.requiresTool()` / `.requiresCorrectToolForDrops()`, add it to the appropriate mineable tag. Place at `src/main/resources/data/minecraft/tags/blocks/mineable/pickaxe.json` (or `axe.json`, etc.):

```json
{
  "replace": false,
  "values": [
    "<mod_id>:<block_id>"
  ]
}
```

And specify the required tier at `data/minecraft/tags/blocks/needs_iron_tool.json` (or `needs_stone_tool.json`, etc.) the same way.

## Step 10: Creative tab

Same as the custom-item recipe, but use the block's item form:

- Fabric: `entries.add(ModBlocks.RUBY_BLOCK.asItem())`
- NeoForge: `event.accept(ModBlocks.RUBY_BLOCK.get())`

## Step 11: Test

`./gradlew runClient`. In-game:

1. Find the block in the creative menu.
2. Place it. Verify the texture renders correctly on all sides.
3. Break it with the correct tool. Verify it drops itself.

`/setblock ~ ~ ~ <mod_id>:<block_id>` is a fast way to test placement without inventory hassle.

## Common failures

- **Block has no item and can't be picked up**: forgot the `BlockItem` registration.
- **Block drops nothing when broken**: missing loot table, or it's in the wrong path (`loot_tables` vs `loot_table` depending on MC version).
- **Block is unbreakable**: it requires a tool tier you haven't tagged. Either remove `.requiresTool()` or add the block to the appropriate mineable/needs_X_tool tags.
- **Missing model**: the blockstate JSON points at a model path that doesn't exist or has a typo. Console will print `Missing model` warnings on game start.
