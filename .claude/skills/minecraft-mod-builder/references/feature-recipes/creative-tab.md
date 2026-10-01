# Recipe: custom creative tab

Add a dedicated creative-mode tab for the mod's items. Recommended once the mod has more than a handful of items, so they're not scattered across vanilla tabs.

## Step 1: Ask the user

- **Tab ID** (snake_case, often the mod ID itself).
- **Icon item**: which registered item should appear on the tab button? Pick a signature item.
- **Display name**: what the tab is called when hovered.

## Step 2: Register the tab

### Fabric

> The example below uses **Yarn names targeting 1.20.x**. If your project uses Mojang mappings or 1.21+, apply the same translations as the item recipe (see `custom-item.md` step 2 for the full table). The main name swaps you'll need here:
>
> - `ItemGroup` → `CreativeModeTab` (Mojang)
> - `ItemStack` import: `net.minecraft.item.ItemStack` → `net.minecraft.world.item.ItemStack` (Mojang)
> - `RegistryKey<ItemGroup>` → `ResourceKey<CreativeModeTab>` (Mojang)
> - `RegistryKeys.ITEM_GROUP` → `Registries.CREATIVE_MODE_TAB` (Mojang)
> - `Identifier.of(...)` (Yarn 1.21+) → `new Identifier(...)` (Yarn 1.20.x), `ResourceLocation.fromNamespaceAndPath(...)` (Mojang 1.21+), or `new ResourceLocation(...)` (Mojang 1.20.x)
> - `Text.translatable(...)` → `Component.translatable(...)` (Mojang)

In a `ModItemGroups` class:

```java
package <package>.item;

import net.fabricmc.fabric.api.itemgroup.v1.FabricItemGroup;
import net.minecraft.item.ItemGroup;
import net.minecraft.item.ItemStack;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.minecraft.registry.RegistryKey;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import <package>.ExampleMod;

public class ModItemGroups {
    public static final RegistryKey<ItemGroup> EXAMPLE_GROUP = RegistryKey.of(
        RegistryKeys.ITEM_GROUP,
        new Identifier(ExampleMod.MOD_ID, "example_group")
    );

    public static void register() {
        Registry.register(Registries.ITEM_GROUP, EXAMPLE_GROUP, FabricItemGroup.builder()
            .icon(() -> new ItemStack(ModItems.RUBY))
            .displayName(Text.translatable("itemgroup.<mod_id>.example_group"))
            .entries((displayContext, entries) -> {
                entries.add(ModItems.RUBY);
                // entries.add(ModBlocks.RUBY_BLOCK.asItem());
                // ...
            })
            .build());
    }
}
```

Call `ModItemGroups.register()` from `onInitialize()`, **before** `ModItems.registerModItems()` (the tab needs to exist before items reference it).

### NeoForge

```java
package <package>.item;

import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.CreativeModeTab;
import net.minecraft.world.item.ItemStack;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.registries.DeferredHolder;
import net.neoforged.neoforge.registries.DeferredRegister;
import <package>.ExampleMod;

public class ModItemGroups {
    public static final DeferredRegister<CreativeModeTab> CREATIVE_MODE_TABS =
        DeferredRegister.create(Registries.CREATIVE_MODE_TAB, ExampleMod.MODID);

    public static final DeferredHolder<CreativeModeTab, CreativeModeTab> EXAMPLE_GROUP =
        CREATIVE_MODE_TABS.register("example_group", () -> CreativeModeTab.builder()
            .icon(() -> new ItemStack(ModItems.RUBY.get()))
            .title(Component.translatable("itemgroup.<mod_id>.example_group"))
            .displayItems((parameters, output) -> {
                output.accept(ModItems.RUBY.get());
                // output.accept(ModBlocks.RUBY_BLOCK.get());
            })
            .build()
        );

    public static void register(IEventBus eventBus) {
        CREATIVE_MODE_TABS.register(eventBus);
    }
}
```

Call `ModItemGroups.register(modEventBus)` in the `@Mod` constructor.

## Step 3: Translation

Add to `assets/<mod_id>/lang/en_us.json`:

```json
{
  "itemgroup.<mod_id>.example_group": "Example Mod"
}
```

## Step 4: Test

`./gradlew runClient`. The new tab should appear in the creative menu with the chosen icon. Click it; the items added in `entries(...)` / `displayItems(...)` should be visible.

## Maintenance note

Every new item the user adds going forward needs an `entries.add(...)` / `output.accept(...)` line in this class to appear in the custom tab. Mention this to the user so they know where to add new items.
