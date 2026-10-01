# Recipe: custom item

Add a custom item to the mod. This is usually the first feature a new modder adds — it touches every layer (registry, model, texture, translation) without being too complex.

## What you'll create

- A registered `Item` instance with a chosen ID.
- A model JSON pointing at a texture.
- A texture PNG (16×16).
- An English translation entry for the in-game name.
- (Optional) Addition to a creative tab so the player can find it.

## Step 1: Ask the user

- **Item ID** (snake_case, e.g. `ruby`, `dragon_scale`).
- **Display name** (the human-readable name shown in tooltips and inventories).
- **Stack size** (default 64; ask if they want non-stackable for tools/weapons).
- **Creative tab placement** — easiest default is "Ingredients" or "Miscellaneous"; if they want a custom tab, do `creative-tab.md` first.

## Step 2: Create the Items class (if it doesn't exist)

Place at `src/main/java/<package>/item/ModItems.java`.

### Fabric

> **Important: the exact API names depend on BOTH your mapping choice AND your MC version.** Translate the example below as needed using this table:
>
> | What you need              | Yarn (1.20.x)               | Yarn (1.21+)                          | Mojang (1.20.x)             | Mojang (1.21+)                          |
> |----------------------------|-----------------------------|----------------------------------------|-----------------------------|------------------------------------------|
> | Item settings/properties   | `new Item.Settings()`       | `new Item.Settings()`                  | `new Item.Properties()`     | `new Item.Properties()`                  |
> | Item registry              | `Registries.ITEM`           | `Registries.ITEM`                      | `BuiltInRegistries.ITEM`    | `BuiltInRegistries.ITEM`                 |
> | Identifier/ResourceLocation construction | `new Identifier(MOD_ID, name)` | `Identifier.of(MOD_ID, name)` | `new ResourceLocation(MOD_ID, name)` | `ResourceLocation.fromNamespaceAndPath(MOD_ID, name)` |
> | Import for ID class        | `net.minecraft.util.Identifier` | same                              | `net.minecraft.resources.ResourceLocation` | same                  |
>
> The skeleton below uses Yarn names targeting 1.20.x. If your project uses Mojang mappings or 1.21+, swap the highlighted parts using the table.

```java
package <package>.item;

import net.minecraft.item.Item;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.minecraft.util.Identifier;
import <package>.ExampleMod;

public class ModItems {
    public static final Item RUBY = register("ruby", new Item(new Item.Settings()));

    private static Item register(String name, Item item) {
        return Registry.register(Registries.ITEM, new Identifier(ExampleMod.MOD_ID, name), item);
    }

    public static void registerModItems() {
        // Triggers class loading. Call from ModInitializer.onInitialize().
    }
}
```

Then in the main `ModInitializer`:

```java
@Override
public void onInitialize() {
    ModItems.registerModItems();
}
```

### NeoForge

```java
package <package>.item;

import net.minecraft.world.item.Item;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredItem;
import net.neoforged.neoforge.registries.DeferredRegister;
import <package>.ExampleMod;

public class ModItems {
    public static final DeferredRegister.Items ITEMS =
        DeferredRegister.createItems(ExampleMod.MODID);

    public static final DeferredItem<Item> RUBY =
        ITEMS.register("ruby", () -> new Item(new Item.Properties()));

    public static void register(IEventBus bus) {
        ITEMS.register(bus);
    }
}
```

Then in the main `@Mod` class constructor:

```java
public ExampleMod(IEventBus modEventBus, ModContainer modContainer) {
    ModItems.register(modEventBus);
}
```

## Step 3: Model JSON

Place at `src/main/resources/assets/<mod_id>/models/item/<item_id>.json`.

```json
{
  "parent": "minecraft:item/generated",
  "textures": {
    "layer0": "<mod_id>:item/<item_id>"
  }
}
```

## Step 4: Texture

**Generate a 16×16 PNG texture for the item by default** — don't skip this step expecting the user to fill it in later. Save it at `src/main/resources/assets/<mod_id>/textures/item/<item_id>.png`. Make the sprite recognizably represent what the item is supposed to be (a ruby looks like a red faceted gem; a "frozen heart" looks like a pale-blue heart; etc.) on a transparent background. Tell the user one short sentence about what you drew so they can ask for changes if it doesn't match their mental image.

Only fall back to a placeholder (`assets/textures/placeholder_item.png` in the skill bundle) if the user has explicitly said they'll handle textures themselves, or if generation is failing. A missing texture file shows up in-game as the purple-and-black "missing texture" checker, which most users mistake for a broken mod.

## Step 5: Translation

Place at `src/main/resources/assets/<mod_id>/lang/en_us.json` (create if missing, append if it exists):

```json
{
  "item.<mod_id>.<item_id>": "<Display Name>"
}
```

The key format is `item.<mod_id>.<item_id>` — both loaders use the same translation key convention.

## Step 6: Adding to a creative tab

### Fabric

In your `ModInitializer.onInitialize()`:

```java
ItemGroupEvents.modifyEntriesEvent(ItemGroups.INGREDIENTS).register(entries -> {
    entries.add(ModItems.RUBY);
});
```

Note for **Mojang mappings**: replace `ItemGroups` with `CreativeModeTabs` (and the import path `net.minecraft.item.ItemGroups` → `net.minecraft.world.item.CreativeModeTabs`). The `entries.add(...)` method name and the `ItemGroupEvents` class itself stay the same regardless of mapping — it's a Fabric API class, not a Minecraft class.

As of Minecraft 1.20, `modifyEntriesEvent` takes a `RegistryKey<ItemGroup>` (Yarn) / `ResourceKey<CreativeModeTab>` (Mojang), not the item-group object itself. `ItemGroups.INGREDIENTS` / `CreativeModeTabs.INGREDIENTS` already **is** that key in modern versions, which is why passing it directly works. If you ever target a third-party mod's creative tab via its `Identifier`/`ResourceLocation`, you'll need to wrap it: `RegistryKey.of(RegistryKeys.ITEM_GROUP, identifier)`.

The `ItemGroupEvents` class lives in the `fabric-item-group-api-v1` module, which is part of the umbrella `fabric-api` dependency that the template already pulls in. If you ever split out only specific Fabric API modules in `build.gradle`, make sure this one is included.

### NeoForge

In a class subscribed to the mod event bus:

```java
@SubscribeEvent
public static void addCreative(BuildCreativeModeTabContentsEvent event) {
    if (event.getTabKey() == CreativeModeTabs.INGREDIENTS) {
        event.accept(ModItems.RUBY);
    }
}
```

If the user wants a fully custom tab, switch to the `creative-tab.md` recipe before continuing.

## Step 7: Test

Run `./gradlew runClient`. In-game:

1. Open the creative inventory.
2. Find the chosen creative tab.
3. The new item should appear with the correct texture and display name.

In-game testing command (works in both loaders, creative or with cheats enabled):

```
/give @s <mod_id>:<item_id>
```

## Common failures

- **Purple-and-black checker texture**: the model JSON's `layer0` path doesn't match the texture file's actual location. Both must use `<mod_id>:item/<item_id>` and the file must be at `assets/<mod_id>/textures/item/<item_id>.png`.
- **"item.<mod_id>.<item_id>" shown as the name** instead of the display name: the lang file is missing the translation key or has a typo in it.
- **Item doesn't appear in creative menu**: the creative tab hook wasn't registered. On Fabric, confirm the `ItemGroupEvents` line is inside `onInitialize`. On NeoForge, confirm the event class is registered on the mod event bus (not the game event bus).
