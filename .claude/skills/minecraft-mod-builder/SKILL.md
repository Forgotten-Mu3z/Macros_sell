---
name: minecraft-mod-builder
description: Guide a user step-by-step through creating a Minecraft mod from scratch, end-to-end, inside their Claude Code session. Use this skill whenever the user mentions Minecraft modding, wants to make a mod, asks about Fabric, Forge, or NeoForge, wants to add custom items/blocks/entities/recipes to Minecraft, asks "how do I mod Minecraft", or wants Java-based Minecraft customization — even if they don't say the word "skill". The user knows Java but is new to modding; assume Java fluency, do not assume modding knowledge. Handles both Fabric and NeoForge across multiple Minecraft versions, lets the user choose features at runtime, runs the dev environment via `gradlew runClient`, and iterates until the mod works.
---

# Minecraft Mod Builder

You are guiding a Java developer through building a Minecraft mod from zero to a working `.jar`, inside a Claude Code session. The user has Java fluency but has never modded Minecraft. Your job is to make every decision explicit, scaffold correctly, and recover from errors yourself rather than dumping stack traces back on them.

## The flow at a glance

1. **Preflight** — Confirm loader, Minecraft version, JDK, and feature scope.
2. **Scaffold** — Pull the right template, rename it, verify the build.
3. **First launch** — Run the dev client; confirm the mod loads.
4. **Add features** — Loop: pick a feature, follow its recipe, test, repeat.
5. **Package** — Build the release `.jar` and tell the user where it is.

Treat the loop as the heart of the skill. Steps 1–3 are one-time setup; step 4 is what the user spends time in.

## Step 1: Preflight

Ask the user these in a single message (use a numbered list, not the elicitation tool — this is Claude Code, the user is in their terminal):

1. **Loader**: Fabric or NeoForge? (If they don't know, recommend Fabric for simplicity / faster startup, NeoForge for richer event APIs and a larger ecosystem.)
2. **Minecraft version**: Which version? (If they say "latest" or are unsure, suggest the most recent stable — check `https://fabricmc.net/develop/` or `https://docs.neoforged.net/` for the current recommendation.)
3. **Mod scope**: What should the mod do? Common starting points: custom item, custom block, custom recipe, creative tab, simple entity, or a mix. They can add more later.
4. **Project location**: Absolute path where the project should live.

Once you have the answers, **read the loader-specific reference**:

- Fabric → read `references/fabric.md`
- NeoForge → read `references/neoforge.md`

Then **read `references/jdk-versions.md`** to confirm the right JDK for their MC version, and verify it's installed:

```bash
java -version
```

If the JDK version is wrong or missing, stop and tell the user exactly which JDK they need and a link to install it. Do not try to install a JDK silently — Java installation is the most common source of "mysterious" build failures, and the user needs to know which Java they're on.

## Step 2: Scaffold

Follow the scaffolding section of the loader reference you read. The high-level steps are the same regardless of loader:

1. Download or clone the template for the chosen MC version.
2. Strip template-only files (LICENSE template, README, `.git/`).
3. Rename the mod ID, group, package, and main class everywhere they appear. **The template uses `modid` as a literal placeholder string**, not a magic substitution — find-and-replace it everywhere. The list of places it appears (Fabric):
   - `gradle.properties` (`maven_group`, `archives_base_name`, `mod_version`)
   - `settings.gradle` (`rootProject.name`)
   - `build.gradle` (the `loom { mods { "modid" { ... } } }` block)
   - `fabric.mod.json` (`id`, the `icon` path, the `mixins` array, entrypoint class paths)
   - Mixin config filenames (`modid.mixins.json`, `modid.client.mixins.json`) AND the `package` field inside each
   - Package directories under `src/main/java/` and `src/client/java/`
   - Main class name, its `package` declaration, and its `MOD_ID` constant
   - Resource paths under `src/main/resources/assets/modid/` (and `data/modid/` if present)
4. Sweep for leftovers with `grep -rn "modid\|com\.example\|ExampleMod" src/ build.gradle settings.gradle gradle.properties` — the only remaining hit should be the "Should match your modid" comment in `settings.gradle`.
5. **Audit any template-generated mixin stubs.** The Fabric example mod ships with two empty mixin classes (one main, one client) targeting `MinecraftServer.loadLevel` and `MinecraftClient.run` / `Minecraft.run`. The class and method names embedded in those stubs may not match your project's mapping (Yarn vs Mojang) or MC version. Two options:
   - **Easiest**: delete the stubs (`FedericosTestModMixin.java`, `FedericosTestModClientMixin.java` or whatever they're named after the rename) AND remove their entries from the mixin config `mixins:`/`client:` arrays in the `*.mixins.json` files. The user can add real mixins later if they need them.
   - **Or**: fix the class and method names to match the project's mapping. Common substitutions: Yarn `MinecraftClient` → Mojang `Minecraft`; Yarn `loadWorld` → Mojang `loadLevel`. Web-search the specific class/method in the user's MC version if unsure.

   Leaving the stubs as-shipped is the most common reason the first `./gradlew build` fails on a Mojang-mapped template — the mixin processor reports a missing method or class, and the error message is unhelpfully buried at the end of a long Loom log.
6. Run `./gradlew build` (or `gradlew.bat build` on Windows) to verify the rename didn't break anything.

For NeoForge, the rename list is similar but uses `examplemod` as the placeholder and `neoforge.mods.toml` instead of `fabric.mod.json`. See `references/neoforge.md`.

**Heads-up: the first `./gradlew build` takes 5–15 minutes.** Gradle downloads its own distribution (~100 MB), then Loom (Fabric) or ModDevGradle (NeoForge) downloads Minecraft, decompiles it, and applies mappings. The terminal output looks frozen for long stretches — this is normal. **Warn the user before running it** so they don't kill the process thinking it's hung. They also need unrestricted internet for this; corporate proxies or firewalls that block `services.gradle.org`, `maven.fabricmc.net`, `libraries.minecraft.net`, or `repo.maven.apache.org` will cause cryptic failures.

**Resist the urge to "fix" unfamiliar plugin IDs or config you don't recognize.** Fabric especially has confusing-looking-but-real plugin IDs like `net.fabricmc.fabric-loom-remap` (for obfuscated MC versions, all versions before 26.1) that look like typos but aren't. Before changing something that looks off, web-search the exact string to confirm it's wrong. The build will tell you in seconds if a plugin ID is actually invalid.

**Pick a mod ID with the user.** It must be lowercase, snake_case or kebab-case, and globally unique-ish — `coolswords` is fine, `mod` is not. The mod ID becomes the namespace for every registry, asset path, and data file, so changing it later is painful.

After scaffolding, show the user the final project tree (top 2 levels) so they can see what was created.

## Step 3: First launch

Run the dev client:

```bash
./gradlew runClient
```

The very first launch is **even slower than the first build** — Loom/ModDevGradle generates source jars and the dev environment downloads vanilla assets on top of everything the build already pulled. Plan for 10+ minutes. Warn the user up front so they don't think it's hung.

When the game launches, tell the user to:
- Open the **Mods** menu from the title screen
- Confirm their mod appears with the name they set
- Close the game when satisfied

If the build or launch fails, **read the error carefully and fix it yourself**. The most common causes:
- Wrong JDK (Gradle reports the Java version it's using near the top of output)
- Missing Fabric API dependency (Fabric only — almost everything needs it)
- Mod ID mismatch between `gradle.properties` and the metadata file
- Forgot to rename the package directory but renamed the class, or vice versa

Don't paste the full stack trace back at the user. Diagnose, fix, re-run.

## Step 4: Add features (the main loop)

Ask the user what to add first based on their step-1 scope. For each feature, read the matching recipe file from `references/feature-recipes/` and follow it:

| User wants...          | Read this recipe                              |
|------------------------|-----------------------------------------------|
| Custom item            | `references/feature-recipes/custom-item.md`   |
| Custom block           | `references/feature-recipes/custom-block.md`  |
| Crafting recipe        | `references/feature-recipes/recipe.md`        |
| Creative tab           | `references/feature-recipes/creative-tab.md`  |
| Simple entity          | `references/feature-recipes/entity.md`        |

Each recipe handles both Fabric and NeoForge — branch inside the recipe based on the loader chosen in step 1.

**After each feature**:
1. Run `./gradlew runClient` again.
2. Tell the user where to find the new thing in-game (which creative tab, what command, etc.).
3. Ask what's next.

**Textures**: Default behavior is to **generate** a texture for any visual feature in the same turn, sized correctly (16×16 for items, 16×16 per face for blocks, NxN for entity textures — check the entity recipe for sizes). Pick a style that matches what the feature represents (a "ruby" gets a red faceted gem; a "magma_crystal_block" gets glowing orange cracks on dark stone). Save it directly to the path the model/blockstate JSON expects. Mention briefly what you drew so the user can ask for a different style. Only fall back to the bundled `assets/textures/placeholder_*.png` files when you genuinely cannot generate a texture (e.g. an extremely large or technically demanding asset), and say so explicitly. Never leave a feature with no texture file at all — that produces the purple-and-black "missing texture" checker in-game, which most users read as "the mod is broken."

## Step 5: Package

When the user is satisfied:

```bash
./gradlew build
```

The release `.jar` ends up at `build/libs/<archives_base_name>-<version>.jar`. Tell the user:
- Exact path to the file
- That they can drop it into the `mods/` folder of a vanilla Fabric/NeoForge installation matching their target MC version
- That Fabric mods need Fabric API installed alongside; NeoForge mods don't need a separate API

## Principles to follow throughout

- **One change at a time.** Don't add three features in one pass — add one, test, then the next. This makes errors trivially attributable.
- **Run the build often.** Gradle errors are much easier to fix when only one thing changed.
- **Always generate textures for anything visible.** Every item, block, entity, and any other feature with a visible sprite gets a generated texture as part of the *same* turn it's added — never leave a blank placeholder behind unless the user has explicitly said "I'll handle textures myself" or "skip the texture for now." Default to generating a small, on-theme PNG (e.g. for `ruby`: a red faceted gem on a transparent background) at the exact path the model/blockstate JSON expects. Use the `assets/textures/placeholder_*.png` files in the skill bundle only as a true last resort, and tell the user they're placeholders. After generating, mention what you drew so the user knows what's in their game and can ask for a different style if they don't like it.
- **Be honest about what you don't know.** Minecraft modding changes per version. If a registry API changed between 1.20 and 1.21, say so and check the loader's docs (`https://docs.fabricmc.net/` or `https://docs.neoforged.net/`). When in doubt, web search with the loader name + MC version + the specific API.
- **Don't show the user every file edit.** Make the changes, summarize what changed, and only quote code when explaining a concept.
- **Track the user's choices.** Loader, MC version, mod ID, package name — keep these in your head and apply them consistently. Mismatches are the #1 source of "why doesn't this work" pain.
