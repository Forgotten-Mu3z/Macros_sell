# Fabric reference

Loader-specific guidance for the Fabric ecosystem. Read this when the user has chosen Fabric in step 1 of the main skill.

## Key facts

- **Build tool**: Gradle, with one of the `fabric-loom` plugin variants. **Don't assume the plugin ID** — modern templates (Loom 1.10+) split into `net.fabricmc.fabric-loom-remap` (for obfuscated MC versions, i.e. anything before 26.1) and `net.fabricmc.fabric-loom` (for unobfuscated 26.1+). Both are valid; check the template's `build.gradle` before "correcting" anything.
- **Mappings**: Check `build.gradle` — look for the `mappings` line in the `dependencies` block. Current example-mod branches use `loom.officialMojangMappings()` (Mojang mappings). Older branches and some third-party templates may use Yarn (`net.fabricmc:yarn:...`). As of Fabric for Minecraft 26.1, Yarn is no longer officially supported, so default to Mojang for new mods unless the user explicitly asks for Yarn.
- **Required dependency**: Fabric API is technically optional but practically required — almost every tutorial and API in the ecosystem assumes it's present.
- **Mod metadata file**: `src/main/resources/fabric.mod.json`
- **Entrypoint**: A class implementing `ModInitializer` (server+client) and/or `ClientModInitializer` (client-only).
- **Template generator**: `https://fabricmc.net/develop/template/` — fastest way to get a project tailored to a specific MC version.

## Scaffolding (Fabric)

**Preferred path**: use the online template generator.

1. Open `https://fabricmc.net/develop/template/` and have the user fill in:
   - Mod name (display name)
   - Package name (e.g. `com.example.coolmod`)
   - Mod ID (matches the package's final segment if possible)
   - Minecraft version (from the dropdown)
   - Whether to include Kotlin support (default no, unless they asked for Kotlin)
2. Download the generated `.zip` and extract into the project location from step 1.

**Fallback path**: clone the example mod directly.

```bash
git clone --branch <minecraft-version-branch> https://github.com/FabricMC/fabric-example-mod.git <project-path>
cd <project-path>
rm -rf .git LICENSE README.md
```

The example-mod repo has branches per MC version (e.g. `1.21`, `1.20.4`). Check `https://github.com/FabricMC/fabric-example-mod/branches` if you're unsure which branch to use.

## Renaming a Fabric template

**Important first note**: the template uses the literal string `modid` (not a placeholder substitution) in several files — `assets/modid/`, `modid.mixins.json`, `modid.client.mixins.json`, and references inside `build.gradle` and `fabric.mod.json`. Treat `modid` like any other string to find-and-replace; it is not magically substituted by Gradle.

Files that need editing:

1. **`gradle.properties`** — set `maven_group`, `mod_version`, and `archives_base_name`. Also confirm `minecraft_version`, `loader_version`, `loom_version`, and `fabric_api_version` (names vary by template — `yarn_mappings` only exists on Yarn-based templates) match what the template emitted. **When appending lines from a script, make sure to include a trailing newline before `>>` or you'll mash two properties onto one line and Gradle will fail to parse it.**
2. **`settings.gradle`** — update `rootProject.name = 'modid'` to use your mod ID.
3. **`build.gradle`** — inside the `loom { mods { "modid" { ... } } }` block, rename the literal `"modid"` string to your mod ID. Also check whether `base { archivesName = ... }` references anything that needs updating.
4. **`src/main/resources/fabric.mod.json`** — set `id`, `name`, `description`, `authors`, `contact`, and `entrypoints.main` (which points at the main class). The `id` here MUST match `archives_base_name` from `gradle.properties`. Also update the `icon` path (e.g. `assets/<mod_id>/icon.png`) and the `mixins` array (which references the mixin config filenames).
5. **Package directories** — move `src/main/java/com/example/examplemod/` (or whatever the template uses) to the chosen package path. Same for `src/client/java/` if the template has a split client source set.
6. **Main class** — rename the class file and update the `package` declaration and the `MOD_ID` constant inside the class. Update the entrypoint references in `fabric.mod.json` to match the new fully-qualified class names.
7. **Mixin config files** (`modid.mixins.json` and `modid.client.mixins.json` if present) — rename the **files themselves** to `<mod_id>.mixins.json` / `<mod_id>.client.mixins.json`, and inside each file update the `package` field to the new mixin package. Also update the references to these filenames in `fabric.mod.json`.
8. **Assets and data namespaces** — rename `src/main/resources/assets/modid/` to `assets/<mod_id>/`. Same for `data/modid/` if present.

After all renames, sweep for leftover references before building:

```bash
grep -rn "modid\|com\.example\|ExampleMod" src/ build.gradle settings.gradle gradle.properties
```

The only remaining hit should be a comment in `settings.gradle` that says "Should match your modid" — that's fine, it's documentation.

Then run:

```bash
./gradlew build
```

**The first build takes 5–15 minutes** because Gradle downloads its own distribution, then Loom downloads Minecraft, decompiles it, and applies mappings. The output looks frozen for long stretches — this is normal. Warn the user before running it.

If it fails, the most common culprits are: forgot to rename the mixin config's `package` field; the mod ID in `fabric.mod.json` doesn't match `gradle.properties`; the entrypoint class path in `fabric.mod.json` doesn't match the actual package + class name; or `build.gradle` / `settings.gradle` still references the old `"modid"` string.

## Running the dev client (Fabric)

```bash
./gradlew runClient
```

For a dedicated server test: `./gradlew runServer` (then accept the EULA in `run/server/eula.txt` and re-run).

## Registry pattern (Fabric)

Fabric uses `Registry.register(Registries.ITEM, ...)` directly in the `ModInitializer.onInitialize()` method (or a static initializer block called from there). There's no deferred registry pattern like NeoForge — registration is immediate at mod load.

### Class and method names vary by MC version and mapping choice

Before writing any Minecraft-API-touching code, **be aware that class and method names differ between Yarn and Mojang mappings**, and method names change between MC versions even within the same mapping set. The example mod's own mixin classes illustrate this — the template's stub mixin may reference a method name that doesn't exist on the chosen MC version. A few examples to watch for:

- Client class: `MinecraftClient` (Yarn) vs `Minecraft` (Mojang).
- Server world-load method: `loadWorld` (1.20.1 Yarn) vs `loadLevel` (Mojang) vs other names in newer versions.
- `Identifier` (Yarn) vs `ResourceLocation` (Mojang).
- `Item.Settings` (Yarn) vs `Item.Properties` (Mojang).

When you encounter compile errors, the first thing to check is "is this name correct for this version + mapping combination?" Search the Fabric docs or the example-mod's own branch for the target MC version to confirm.

Example structure for an item (assumes Yarn mapping names — translate to Mojang if needed):

```java
public class ModItems {
    public static final Item EXAMPLE_ITEM = Registry.register(
        Registries.ITEM,
        Identifier.of(ExampleMod.MOD_ID, "example_item"),
        new Item(new Item.Settings())
    );

    public static void register() {
        // Called from ModInitializer.onInitialize() to trigger class loading.
    }
}
```

## Useful Fabric docs

- Official docs: `https://docs.fabricmc.net/`
- Wiki (more tutorials): `https://wiki.fabricmc.net/`
- Versions/dependencies dashboard: `https://fabricmc.net/develop/`
