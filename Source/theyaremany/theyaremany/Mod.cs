using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheyAreMany;

// Harmony binds injection parameters by name (__instance, __result, ___pawn),
// so they cannot follow the parameter naming rule.
// ReSharper disable InconsistentNaming

[StaticConstructorOnStartup]
public static class TheyAreManyMod
{
    private const string GuildDefName = "TheyAreMany";
    private const string TheMasterKindDefName = "TheyAreMany_TheMaster";
    private const string AncientCloningVatDefName = "TheyAreMany_AncientCloningVat";
    private const string AncientTurretDefName = "TheyAreMany_AncientTurret";
    private const string TheMastersRifleDefName = "TheyAreMany_TheMastersRifle";
    private const string VatGrownChildhoodDefName = "TheyAreMany_VatGrown";
    private const string TheWrongedDefName = "TheyAreMany_TheWronged";
    private const string TheVatMasterDefName = "TheyAreMany_TheVatMaster";
    private const string VatSiblingDefName = "TheyAreMany_VatSibling";

    // Clone floating title; the map label stays bare (see StripCloneTitleSuffix).
    private const string CloneTitle = "vat-grown clone";

    private static TheyAreManySettings Settings => TheyAreManyConfig.Loaded;

    private const string IntroLetterLabel = "Marked from above";

    private const string IntroLetterText = "Word has been traveling ahead of you lately, and it speaks your name.\n\n"
        + "Traders in orbit tell of a guild that nobody remembers founding, and nobody can - the thing is only a few years old. They work from an old station that hangs over this world like a held breath - a place on no chart, built by no hand anyone can name, where machines built as cradles have only lately started rocking again.\n\n"
        + "Their lord is only ever called {0}. The stories agree on little about that name, except this: its bearer knows you exist, hates you, and is patient.\n\n"
        + "There are fainter stories, too. Of decks that glow at night on a hull the charts say is empty. Of cargo pods that come down faster and angrier than any trade ship ever did. Of a voice on guild frequencies that counts, unhurried, higher every year.\n\n"
        + "Grudges like this one do not die of old age, and neither does the one who holds it. If you want it over, you will have to fly to his station, take it apart, and put him down where he stands. Whatever he is growing up there, it will not stay up there.";

    private const string FinalTransmissionText = "A radio on the dead {0}'s harness crackles one last time. The voice is calm, and it is his.\n\n'The list was long, and you were at the end of it. I made too many of me to stop counting. The machine will keep going without me - or maybe it's done. Either way, the list is empty now. Take the rifle. It belongs to whoever is still standing.'";

    private class TheyAreManySettings : ModSettings
    {
        public const string DefaultPersonaIdent = "bio:Exorus 'Huntsman' Hawke";

        // Lodge device counts; internal, no sliders in the settings window.
        public const int DefaultVatCount = 5;

        public const int DefaultTurretCount = 6;

        public int VatCount = DefaultVatCount;

        public int TurretCount = DefaultTurretCount;

        public float GuildThreatScale = 1f;

        public const float DefaultGuildRaidLikelihood = 2f;

        public float GuildRaidLikelihood = DefaultGuildRaidLikelihood;

        // Internal: move the guild lodge over the player's landing site at start.
        public bool LodgeNearStart = true;

        // Guild persona ident: "bio:<First 'Nick' Last>" for a vanilla character.
        public string PersonaIdent = DefaultPersonaIdent;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref VatCount, "vatCount", DefaultVatCount);
            Scribe_Values.Look(ref TurretCount, "turretCount", DefaultTurretCount);
            Scribe_Values.Look(ref GuildThreatScale, "guildThreatScale", 1f);
            Scribe_Values.Look(ref GuildRaidLikelihood, "guildRaidLikelihood", DefaultGuildRaidLikelihood);
            Scribe_Values.Look(ref LodgeNearStart, "lodgeNearStart", true);
            Scribe_Values.Look(ref PersonaIdent, "personaIdent", DefaultPersonaIdent);
            base.ExposeData();
        }
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private class TheyAreManyConfig : Mod
    {
        public static TheyAreManySettings Loaded { get; private set; } = new();

        public TheyAreManyConfig(ModContentPack content) : base(content)
        {
            Loaded = GetSettings<TheyAreManySettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            Text.Font = GameFont.Small;
            listing.Gap();
            listing.Label("Guild raid threat scale: x" + Settings.GuildThreatScale.ToString("0.##", CultureInfo.InvariantCulture));
            Settings.GuildThreatScale = Mathf.Round(listing.Slider(Settings.GuildThreatScale, 0.8f, 1.2f) * 100f) / 100f;
            listing.Gap();
            listing.Label("Guild raid likelihood: x" + Settings.GuildRaidLikelihood.ToString("0.##", CultureInfo.InvariantCulture));
            Settings.GuildRaidLikelihood = Mathf.Round(listing.Slider(Settings.GuildRaidLikelihood, 0.5f, 4f) * 100f) / 100f;
            listing.Gap();
            if (listing.ButtonText("Guild persona: " + PersonaButtonLabel(Settings.PersonaIdent)))
            {
                Find.WindowStack.Add(BuildPersonaMenu());
            }
            listing.Gap();
            if (listing.ButtonText("Reset to defaults"))
            {
                Settings.GuildThreatScale = 1f;
                Settings.GuildRaidLikelihood = TheyAreManySettings.DefaultGuildRaidLikelihood;
                Settings.VatCount = TheyAreManySettings.DefaultVatCount;
                Settings.TurretCount = TheyAreManySettings.DefaultTurretCount;
                Settings.PersonaIdent = TheyAreManySettings.DefaultPersonaIdent;
            }
            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        private static string PersonaButtonLabel(string ident)
        {
            Persona? persona = ResolvePersona(ident);
            if (persona == null)
            {
                return ident.NullOrEmpty() ? TheyAreManySettings.DefaultPersonaIdent : ident;
            }
            return persona switch
            {
                // The button reads the floating name the guild would use.
                { Name.Nick: { } nick } => nick,
                _ => ident
            };
        }

        // Persona picker: vanilla solid bios only, keyed by their floating names.
        private static FloatMenu BuildPersonaMenu()
        {
            string current = Settings.PersonaIdent;
            var options = new List<FloatMenuOption>
            {
                new("-- named characters --", null)
            };
            foreach (PawnBio bio in SolidBioDatabase.allBios
                .Where(bio => bio?.name != null)
                .OrderBy(bio => bio.name.Nick, StringComparer.CurrentCulture)
                .ThenBy(bio => bio.name.ToString(), StringComparer.CurrentCulture))
            {
                string ident = "bio:" + bio.name;
                string label = (ident == current ? "* " : "") + bio.name.Nick;
                options.Add(new FloatMenuOption(label, () => Settings.PersonaIdent = ident)
                {
                    tooltip = bio.name.ToStringFull
                });
            }
            return new FloatMenu(options);
        }

        public override string SettingsCategory()
        {
            return "theyaremany";
        }
    }

    // Raid counter for batch numbers; vanilla registers GameComponents itself.
    // Instantiated by vanilla via Activator.CreateInstance (Game.FillComponents).
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class TheyAreManyGameComponent : GameComponent
    {
        public int GuildRaidCount;

        // Vanilla instantiates game components via Activator.CreateInstance(type, game).
        public TheyAreManyGameComponent(Game game)
        {
            _ = game;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref GuildRaidCount, "guildRaidCount");
        }
    }

    private static readonly Harmony Patcher = new("anonymous.theyaremany");

    static TheyAreManyMod()
    {
        SafePatch(
            AccessTools.Method(typeof(FactionGenerator), nameof(FactionGenerator.GenerateFactionsIntoWorldLayer)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(EnforceSingleSettlement)));
        SafePatch(
            AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), [typeof(PawnGenerationRequest)]),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(RenameGuildPawn)));
        SafePatch(
            AccessTools.Method(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo)),
            prefix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(InjectPersonaBioAndName)));
        SafePatch(
            AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.LabelNoCount)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(StripCloneTitleSuffix)));
        SafePatch(
            AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PreApplyDamage)),
            prefix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(BlockTheMasterDamage)));
        SafePatch(
            AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)),
            prefix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(BlockTheMasterDeath)));
        SafePatch(
            AccessTools.Method(typeof(Faction), nameof(Faction.TryGenerateNewLeader)),
            prefix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(NoGuildLeaderRegeneration)));
        SafePatch(
            AccessTools.PropertyGetter(typeof(Faction), "ShouldHaveLeader"),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(NoLeaderExpectedForGuild)));
        SafePatch(
            AccessTools.Method(typeof(MapGenerator), nameof(MapGenerator.GenerateMap)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(SpawnTheMasterOnLodgeMap)));
        SafePatch(
            AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.RequiresSignalJammerToReach)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(NoJammerRequiredForGuildLodge)));
        SafePatch(
            AccessTools.PropertyGetter(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(AncientGuildTechAlwaysPowered)));
        SafePatch(
            AccessTools.Method(typeof(Building_Door), nameof(Building_Door.PawnCanOpen)),
            prefix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(GuildLodgeDoorsOpenToPlayer)));
        SafePatch(
            AccessTools.Method(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.AdjustedRaidPoints)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(ScaleGuildRaidPoints)));
        SafePatch(
            AccessTools.Method(typeof(FactionDef), nameof(FactionDef.RaidCommonalityFromPoints)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(ScaleGuildRaidLikelihood)));
        SafePatch(
            AccessTools.Method(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo)),
            prefix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(CountGuildRaidBatch)));
        SafePatch(
            AccessTools.Method(typeof(PawnGenerator), "GeneratePawnRelations"),
            prefix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(SkipGuildRandomRelations)));
        SafePatch(
            AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(SendFinalTransmission)));
        SafePatch(
            AccessTools.Method(typeof(Game), nameof(Game.InitNewGame)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(EnsureTheMasterExistsAtGameStart)));
        SafePatch(
            AccessTools.Method(typeof(Game), nameof(Game.InitNewGame)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(RelocateLodgeNearStart)));
        SafePatch(
            AccessTools.Method(typeof(Game), nameof(Game.InitNewGame)),
            postfix: new HarmonyMethod(typeof(TheyAreManyMod), nameof(SendIntroLetter)));
        Log.Message(_failedPatches == 0
            ? "[theyaremany] guild patches applied"
            : $"[theyaremany] {_failedPatches} of 20 patches failed to apply");
    }

    private static int _failedPatches;

    // Fail a single patch without losing the rest.
    private static void SafePatch(MethodBase original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        try
        {
            Patcher.Patch(original, prefix, postfix);
        }
        catch (Exception ex)
        {
            _failedPatches++;
            Log.Error("[theyaremany] failed to patch " + original.FullDescription() + ": " + ex);
        }
    }

    private sealed class Persona
    {
        public BackstoryDef? Childhood;

        public BackstoryDef? Adulthood;

        public NameTriple? Name;
    }

    // Ident to persona; unresolvable idents fall back to the default persona.
    private static Persona? ResolvePersona(string? ident)
    {
        Persona? persona = ResolveIdent(ident);
        return persona ?? ResolveIdent(TheyAreManySettings.DefaultPersonaIdent);
    }

    private static Persona? ResolveIdent(string? ident)
    {
        if (ident.NullOrEmpty())
        {
            return null;
        }
        int split = ident!.IndexOf(':');
        if (split < 0)
        {
            return null;
        }
        string kind = ident[..split];
        string payload = ident[(split + 1)..];
        switch (kind)
        {
            case "bio":
            {
                PawnBio bio = SolidBioDatabase.allBios.FirstOrDefault(b => b?.name != null && b.name.ToString() == payload);
                if (bio == null)
                {
                    return null;
                }
                return new Persona
                {
                    Childhood = bio.childhood,
                    Adulthood = bio.adulthood,
                    Name = bio.name
                };
            }
            default:
                return null;
        }
    }

    // Swaps the generated backstories for the persona's own, when it has them.
    private static void ApplyPersonaStories(Pawn pawn, Persona? persona)
    {
        if (persona == null || pawn.story == null)
        {
            return;
        }
        if (persona.Childhood != null)
        {
            pawn.story.Childhood = persona.Childhood;
        }
        if (persona.Adulthood != null)
        {
            pawn.story.Adulthood = persona.Adulthood;
        }
    }

    // The villain's floating name from the persona; "the master" as fallback.
    private static string VillainFloatName()
    {
        Persona? persona = ResolvePersona(TheyAreManyConfig.Loaded.PersonaIdent);
        return persona?.Name != null && !persona.Name.Nick.NullOrEmpty()
            ? persona.Name.Nick
            : "the master";
    }

    private static bool IsTheMaster(Pawn pawn)
    {
        return pawn is { RaceProps.Humanlike: true, kindDef.defName: TheMasterKindDefName, Faction.def.defName: GuildDefName };
    }

    // Apply the persona before vanilla rolls; RenameGuildPawn names afterward.
    private static bool InjectPersonaBioAndName(Pawn pawn)
    {
        try
        {
            if (pawn.story == null
                || pawn.kindDef == null
                || !pawn.kindDef.defName.StartsWith("TheyAreMany_", StringComparison.Ordinal))
            {
                return true;
            }
            Persona? persona = ResolvePersona(TheyAreManyConfig.Loaded.PersonaIdent);
            ApplyPersonaStories(pawn, persona);
            if (pawn.kindDef.defName == TheMasterKindDefName)
            {
                // Story-less default: fall back to his grudge-and-facility stories.
                BackstoryDef firstGrown = DefDatabase<BackstoryDef>.GetNamedSilentFail(TheWrongedDefName);
                if (pawn.story.Childhood == null && firstGrown != null)
                {
                    pawn.story.Childhood = firstGrown;
                }
                BackstoryDef vatMaster = DefDatabase<BackstoryDef>.GetNamedSilentFail(TheVatMasterDefName);
                if (pawn.story.Adulthood == null && vatMaster != null)
                {
                    pawn.story.Adulthood = vatMaster;
                }
            }
            else
            {
                // Vat childhood; their profile comes from the master anyway.
                BackstoryDef vatGrown = DefDatabase<BackstoryDef>.GetNamedSilentFail(VatGrownChildhoodDefName);
                if (vatGrown != null)
                {
                    pawn.story.Childhood = vatGrown;
                }
            }
            // Placeholder only; RenameGuildPawn pins the real name.
            if (persona?.Name != null)
            {
                pawn.Name = persona.Name;
            }
            else
            {
                string fallback = VillainFloatName();
                pawn.Name = new NameTriple(fallback, fallback, fallback);
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to apply persona to guild pawn: " + ex);
            return true;
        }
    }

    // Clone inherits the master's stories, genes, traits and skills wholesale.
    private static void CopyTheMastersProfile(Pawn clone, Pawn master)
    {
        if (clone.story == null || master.story == null)
        {
            return;
        }
        if (master.story.Adulthood != null)
        {
            clone.story.Adulthood = master.story.Adulthood;
        }
        clone.story.Title = CloneTitle;
        CopyTheMastersGenes(clone, master);
        CopyTheMastersTraits(clone, master);
        if (master.skills != null && clone.skills != null)
        {
            foreach (SkillRecord skill in clone.skills.skills)
            {
                SkillRecord source = master.skills.GetSkill(skill.def);
                if (source == null)
                {
                    continue;
                }
                skill.passion = source.passion;
                skill.Level = source.Level;
                skill.xpSinceLastLevel = source.xpSinceLastLevel;
                if (source.TotallyDisabled)
                {
                    skill.Level = 0;
                }
            }
        }
    }

    // Clone the xenotype and both gene sets; rebuild via AddGene/RemoveGene.
    private static void CopyTheMastersGenes(Pawn clone, Pawn master)
    {
        if (!ModLister.BiotechInstalled || clone.genes == null || master.genes == null)
        {
            return;
        }
        clone.genes.SetXenotypeDirect(master.genes.Xenotype);
        // A unique xenotype (built in-game, no def) keeps its name and icon.
        clone.genes.xenotypeName = master.genes.xenotypeName;
        clone.genes.iconDef = master.genes.iconDef;
        foreach (Gene gene in clone.genes.Endogenes.ToList())
        {
            clone.genes.RemoveGene(gene);
        }
        foreach (Gene gene in clone.genes.Xenogenes.ToList())
        {
            clone.genes.RemoveGene(gene);
        }
        foreach (Gene gene in master.genes.Endogenes)
        {
            clone.genes.AddGene(gene.def, xenogene: false);
        }
        foreach (Gene gene in master.genes.Xenogenes)
        {
            clone.genes.AddGene(gene.def, xenogene: true);
        }
    }

    // Only non-gene traits move; gene traits arrive with the copied genes.
    private static void CopyTheMastersTraits(Pawn clone, Pawn master)
    {
        TraitSet traits = clone.story.traits;
        if (traits == null || master.story.traits == null)
        {
            return;
        }
        foreach (Trait trait in traits.allTraits.ToList())
        {
            if (trait.sourceGene == null)
            {
                traits.RemoveTrait(trait, unsuppressConflicts: true);
            }
        }
        foreach (Trait trait in master.story.traits.allTraits.ToList())
        {
            if (trait.sourceGene == null && !traits.HasTrait(trait.def))
            {
                traits.GainTrait(new Trait(trait.def, trait.Degree, forced: true), suppressConflicts: true);
            }
        }
    }

    private static bool IsGuildPawn(Pawn pawn)
    {
        return pawn is { RaceProps.Humanlike: true, Faction.def.defName: GuildDefName };
    }

    // Clones' labels drop the title suffix; string result suits Harmony 3.
    private static void StripCloneTitleSuffix(Pawn __instance, ref string __result)
    {
        if (IsGuildPawn(__instance)
            && !IsTheMaster(__instance)
            && __instance.Name is NameTriple name)
        {
            __result = name.ToStringShort;
        }
    }

    // No random relations; the vat-sibling link is added in RenameGuildPawn.
    private static bool SkipGuildRandomRelations(Pawn pawn)
    {
        return !IsGuildPawn(pawn);
    }

    // The master is only touchable on his own settlement map.
    private static bool AtTheGuildLodge(Pawn pawn)
    {
        return pawn.Map?.Parent is Settlement lodge && lodge.Faction == pawn.Faction;
    }

    // The guild lodge is a plain raid target: no signal-jammer requirement.
    private static void NoJammerRequiredForGuildLodge(WorldObject __instance, ref bool __result)
    {
        if (__result
            && __instance is Settlement { Faction.def.defName: GuildDefName })
        {
            __result = false;
        }
    }

    private static bool BlockTheMasterDamage(ref bool absorbed, Pawn ___pawn)
    {
        // Absorb, so callers get a valid empty result instead of a hard skip.
        if (IsTheMaster(___pawn) && !AtTheGuildLodge(___pawn))
        {
            absorbed = true;
            return false;
        }
        return true;
    }

    // Never regenerate the guild leader in-game; world gen still mints the first.
    private static bool NoGuildLeaderRegeneration(Faction __instance)
    {
        return __instance is not { def.defName: GuildDefName }
            || __instance.temporary
            || Current.Game == null;
    }

    // A leaderless guild after his death is intended, not a load-time error.
    private static void NoLeaderExpectedForGuild(Faction __instance, ref bool __result)
    {
        if (__result
            && __instance is { def.defName: GuildDefName })
        {
            __result = false;
        }
    }

    private static bool BlockTheMasterDeath(Pawn __instance)
    {
        return !IsTheMaster(__instance) || AtTheGuildLodge(__instance);
    }

    // Clamp world gen to exactly one guild settlement per layer.
    private static void EnforceSingleSettlement(PlanetLayer layer)
    {
        try
        {
            if (Find.FactionManager == null || Find.WorldObjects == null)
            {
                return;
            }
            FactionDef guildDef = DefDatabase<FactionDef>.GetNamedSilentFail(GuildDefName);
            if (guildDef == null)
            {
                return;
            }
            Faction guild = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def == guildDef);
            if (guild == null || guild.temporary)
            {
                return;
            }
            List<Settlement> settlements =
            [
                .. Find.WorldObjects.AllWorldObjects
                    .OfType<Settlement>()
                    .Where(s => s.Faction == guild)
            ];
            for (int i = 1; i < settlements.Count; i++)
            {
                Find.WorldObjects.Remove(settlements[i]);
            }
            if (settlements.Count == 0 && CanExistOnLayer(layer, guildDef))
            {
                WorldObject lodge = WorldObjectMaker.MakeWorldObject(layer.Def.SettlementWorldObjectDef);
                lodge.SetFaction(guild);
                lodge.Tile = TileFinder.RandomSettlementTileFor(layer, guild);
                if (lodge is INameableWorldObject nameable)
                {
                    nameable.Name = SettlementNameGenerator.GenerateSettlementName(lodge);
                }
                Find.WorldObjects.Add(lodge);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to enforce single guild settlement: " + ex);
        }
    }

    // Mirrors FactionGenerator.CanExistOnLayer, which is private in vanilla.
    private static bool CanExistOnLayer(PlanetLayer layer, FactionDef def)
    {
        if (!def.layerBlacklist.NullOrEmpty() && def.layerBlacklist.Contains(layer.Def))
        {
            return false;
        }
        if (!def.layerWhitelist.NullOrEmpty() || !layer.IsRootSurface)
        {
            return def.layerWhitelist.Contains(layer.Def);
        }
        return true;
    }

    // Pin names and ages; the vat age profile is set after generation.
    private static void RenameGuildPawn(Pawn __result)
    {
        try
        {
            if (__result is not { Name: NameTriple, RaceProps.Humanlike: true })
            {
                return;
            }
            var faction = __result.Faction;
            if (faction == null || faction.def.defName != GuildDefName)
            {
                return;
            }
            // Vat births land in the guild as newborns; they are not batch members.
            if (__result.DevelopmentalStage != DevelopmentalStage.Adult)
            {
                return;
            }
            Persona? persona = ResolvePersona(TheyAreManyConfig.Loaded.PersonaIdent);
            if (IsTheMaster(__result))
            {
                // The persona's full solid bio carries its own name.
                if (persona?.Name != null)
                {
                    __result.Name = persona.Name;
                }
                // Uniquely armed and loyal: capture him, never recruit him.
                __result.guest?.Recruitable = false;
                ThingDef rifleDef = DefDatabase<ThingDef>.GetNamedSilentFail(TheMastersRifleDefName);
                if (rifleDef != null)
                {
                    __result.equipment?.DestroyAllEquipment();
                    var rifle = (ThingWithComps)ThingMaker.MakeThing(rifleDef);
                    // Legendary; the def's unique-weapon comp names and traits it.
                    // No art source: keep the name the unique comp already minted.
                    rifle.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, null);
                    rifle.TryGetComp<CompBiocodable>()?.UnCode();
                    __result.equipment?.AddEquipment(rifle);
                }
                return;
            }
            // One batch number per raid, saved in the game component.
            int batch = RaidCounter()?.GuildRaidCount ?? 0;
            // Persona nickname floats; the batch tag lives in the last name.
            string batchTag = $"Batch {batch:000}";
            if (persona?.Name != null)
            {
                string last = persona.Name.Last.NullOrEmpty() ? "" : persona.Name.Last + " ";
                __result.Name = new NameTriple(persona.Name.First, persona.Name.Nick, last + batchTag);
            }
            else
            {
                string fallback = VillainFloatName();
                __result.Name = new NameTriple(fallback, fallback, batchTag);
            }
            __result.ageTracker.AgeBiologicalTicks = (long)(new FloatRange(20f, 26f).RandomInRange * 3600000f);
            // Gestation plus a few days: adults in days, not years.
            __result.ageTracker.AgeChronologicalTicks = (long)(new FloatRange(9f, 16f).RandomInRange * 60000f);
            // Vat sibling of the master; vanilla's implied kin cannot be added.
            Pawn master = faction.leader;
            if (master is { Dead: false }
                && master != __result)
            {
                PawnRelationDef vatSibling = DefDatabase<PawnRelationDef>.GetNamedSilentFail(VatSiblingDefName);
                if (vatSibling != null && !__result.relations.DirectRelationExists(vatSibling, master))
                {
                    __result.relations.AddDirectRelation(vatSibling, master);
                }
                CopyTheMastersProfile(__result, master);
            }
            // Capturable, never recruitable, never turned against the guild.
            __result.guest?.Recruitable = false;
            // Clone guns are bio coded to their twin; only the rifle pays out.
            if (__result.equipment?.Primary is { } primary)
            {
                primary.TryGetComp<CompBiocodable>()?.CodeFor(__result);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to finalize guild pawn: " + ex);
        }
    }

    // The master only ever spawns on his own settlement map.
    private static void SpawnTheMasterOnLodgeMap(Map __result, MapParent parent)
    {
        try
        {
            if (parent is not Settlement lodge)
            {
                return;
            }
            Faction faction = lodge.Faction;
            if (faction == null || faction.def.defName != GuildDefName)
            {
                return;
            }
            EnsureTheMasterExists(faction);
            if (!TheMasterAlive())
            {
                // A dead master leaves a dark ruin; no restock on revisit.
                return;
            }
            List<Building_GrowthVat> vats = SetupLodgeMapDevices(__result, faction);
            Pawn leader = faction.leader;
            if (leader == null || leader.Dead || !IsTheMaster(leader) || leader.Spawned || leader.Map != null)
            {
                return;
            }
            if (!TryFindTheMastersCell(__result, vats, out IntVec3 cell))
            {
                Log.Warning("[theyaremany] found no spawn cell for the master on his lodge map");
                return;
            }
            GenSpawn.Spawn(leader, cell, __result);
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to spawn the master on his lodge map: " + ex);
        }
    }

    // He guards the vats; fallback is a walkable cell near the map center.
    private static bool TryFindTheMastersCell(Map map, List<Building_GrowthVat> vats, out IntVec3 cell)
    {
        foreach (Building_GrowthVat vat in vats)
        {
            if (vat.Spawned && CellFinder.TryRandomClosewalkCellNear(vat.Position, map, 8, out cell))
            {
                return true;
            }
        }
        if (CellFinder.TryRandomClosewalkCellNear(map.Center, map, 30, out cell))
        {
            return true;
        }
        cell = IntVec3.Invalid;
        return false;
    }

    // The master is the reactor: guild tech runs while he lives, dark after.
    private static void AncientGuildTechAlwaysPowered(CompPowerTrader __instance, ref bool __result)
    {
        if (__result
            || __instance.parent == null
            || (__instance.parent.def.defName != AncientCloningVatDefName
                && __instance.parent.def.defName != AncientTurretDefName))
        {
            return;
        }
        // Only the lodge facility runs on him; elsewhere it needs a real grid.
        Map map = __instance.parent.MapHeld;
        bool onGuildLodge = map?.Parent is Settlement { Faction.def.defName: GuildDefName };
        if (onGuildLodge && TheMasterAlive())
        {
            __result = true;
        }
    }

    private static bool TheMasterAlive()
    {
        if (Find.FactionManager == null)
        {
            return false;
        }
        Faction guild = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def.defName == GuildDefName);
        return guild?.leader is { Dead: false } leader && IsTheMaster(leader);
    }

    // Hostile-faction doors would seal; open them per door for the player.
    private static bool GuildLodgeDoorsOpenToPlayer(Building_Door __instance, Pawn p, ref bool __result)
    {
        if (p.Faction == Faction.OfPlayer
            && __instance.Map?.Parent is Settlement { Faction.def.defName: GuildDefName })
        {
            __result = true;
            return false;
        }
        return true;
    }

    // Slider scales the final guild raid size only; other factions pass through.
    private static void ScaleGuildRaidPoints(Faction faction, ref float __result)
    {
        if (faction.def.defName == GuildDefName)
        {
            __result *= Settings.GuildThreatScale;
        }
    }

    // Slider shifts the raid share toward the guild; total frequency is unchanged.
    private static void ScaleGuildRaidLikelihood(FactionDef __instance, ref float __result)
    {
        if (__instance.defName == GuildDefName)
        {
            __result *= Settings.GuildRaidLikelihood;
        }
    }

    // Prefix runs before pawn generation, so the whole raid shares one batch.
    private static void CountGuildRaidBatch(IncidentParms parms)
    {
        if (parms.faction != null && parms.faction.def.defName == GuildDefName)
        {
            TheyAreManyGameComponent? counter = RaidCounter();
            if (counter != null)
            {
                counter.GuildRaidCount++;
            }
        }
    }

    private static TheyAreManyGameComponent? RaidCounter()
    {
        return Current.Game?.GetComponent<TheyAreManyGameComponent>();
    }

    // Last transmission over his corpse; only confirm an actual death.
    private static void SendFinalTransmission(Pawn __instance)
    {
        try
        {
            if (!IsTheMaster(__instance) || !__instance.Dead)
            {
                return;
            }
            Find.LetterStack.ReceiveLetter(
                "The last transmission",
                string.Format(FinalTransmissionText, VillainFloatName()),
                LetterDefOf.NeutralEvent);
            KillAllLivingClones();
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to send the final transmission: " + ex);
        }
    }

    // When the master dies, every living clone dies with him.
    private static void KillAllLivingClones()
    {
        try
        {
            List<Pawn> clones = [];
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (IsGuildPawn(pawn) && !IsTheMaster(pawn))
                    {
                        clones.Add(pawn);
                    }
                }
            }
            foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
            {
                if (IsGuildPawn(pawn) && !IsTheMaster(pawn))
                {
                    clones.Add(pawn);
                }
            }
            foreach (Pawn pawn in clones)
            {
                try
                {
                    if (pawn is { Dead: false, Destroyed: false })
                    {
                        pawn.Kill(null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[theyaremany] failed to kill a surviving clone: " + ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to end the batch: " + ex);
        }
    }

    // Lodge map devices: defense emitters first, so vat placement avoids them.
    private static List<Building_GrowthVat> SetupLodgeMapDevices(Map map, Faction guild)
    {
        PlaceAncientTurrets(map, guild);
        return SetupCloningVats(map, guild);
    }

    // Everything hangs off the master; mint one if the guild starts leaderless.
    private static void EnsureTheMasterExistsAtGameStart()
    {
        try
        {
            if (Find.FactionManager == null)
            {
                return;
            }
            Faction? guild = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => f.def.defName == GuildDefName);
            if (guild != null)
            {
                EnsureTheMasterExists(guild);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to check the guild's leader: " + ex);
        }
    }

    private static void EnsureTheMasterExists(Faction guild)
    {
        if (guild.leader is { Dead: false } leader && IsTheMaster(leader))
        {
            return;
        }
        PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(TheMasterKindDefName);
        if (kind == null)
        {
            Log.Error("[theyaremany] the master pawn kind is missing; the guild stays leaderless");
            return;
        }
        Pawn master = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            kind, guild, forceGenerateNewPawn: true));
        if (master == null)
        {
            Log.Error("[theyaremany] failed to generate the master; the guild stays leaderless");
            return;
        }
        guild.leader = master;
        if (!Find.WorldPawns.Contains(master))
        {
            Find.WorldPawns.PassToWorld(master, PawnDiscardDecideMode.KeepForever);
        }
        Log.Message("[theyaremany] the guild was leaderless; generated the master anew");
    }

    // Move the lodge over the landing site before the intro letter goes out.
    private static void RelocateLodgeNearStart(Game __instance)
    {
        try
        {
            if (!Settings.LodgeNearStart || Find.WorldGrid == null)
            {
                return;
            }
            PlanetTile start = __instance.Info.startingTile;
            if (!start.Valid)
            {
                return;
            }
            Faction? guild = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => f.def.defName == GuildDefName);
            Settlement? lodge = Find.WorldObjects.AllWorldObjects
                .OfType<Settlement>()
                .FirstOrDefault(s => s.Faction == guild);
            if (guild == null || lodge == null)
            {
                return;
            }
            WorldGrid grid = Find.WorldGrid;
            PlanetLayer orbit = grid.Orbit;
            if (orbit == null)
            {
                return;
            }
            Vector3 anchor = grid.GetTileCenter(start);
            // Nearest valid orbit tile to the landing site wins.
            PlanetTile? chosen = null;
            float best = float.MaxValue;
            for (int i = 0; i < orbit.TilesCount; i++)
            {
                PlanetTile tile = new PlanetTile(i, orbit);
                float distance = (grid.GetTileCenter(tile) - anchor).sqrMagnitude;
                if (distance < best && TileFinder.IsValidTileForNewSettlement(tile))
                {
                    best = distance;
                    chosen = tile;
                }
            }
            if (chosen == null)
            {
                Log.Warning("[theyaremany] found no free orbit tile near the landing site; the lodge stays put");
                return;
            }
            lodge.Tile = chosen.Value;
            Log.Message("[theyaremany] moved the guild lodge to an orbit tile near the landing site");
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to move the lodge near the landing site: " + ex);
        }
    }

    // The opening rumor: sent once per new game, before anything is on the map.
    private static void SendIntroLetter()
    {
        try
        {
            FactionDef guildDef = DefDatabase<FactionDef>.GetNamedSilentFail(GuildDefName);
            if (guildDef == null)
            {
                return;
            }
            Faction guild = Find.FactionManager.FirstFactionOfDef(guildDef);
            if (guild == null)
            {
                return;
            }
            Find.LetterStack.ReceiveLetter(
                IntroLetterLabel,
                string.Format(IntroLetterText, VillainFloatName()),
                LetterDefOf.NeutralEvent);
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to send the intro letter: " + ex);
        }
    }

    // Ancient emitters guard the sealed rooms of the facility itself.
    private static void PlaceAncientTurrets(Map map, Faction guild)
    {
        try
        {
            ThingDef turretDef = DefDatabase<ThingDef>.GetNamedSilentFail(AncientTurretDefName);
            if (turretDef == null || map.regionGrid == null)
            {
                return;
            }
            List<Room> rooms =
            [
                .. map.regionGrid.AllRooms
                    .Where(r => r is { PsychologicallyOutdoors: false, Vacuum: <= 0f, CellCount: >= 25 })
                    .OrderBy(r => RoomDistanceSquared(r, map.Center))
            ];
            int placed = 0;
            foreach (Room room in rooms)
            {
                if (placed >= Settings.TurretCount)
                {
                    break;
                }
                if (TryPlaceTurret(map, turretDef, room, guild))
                {
                    placed++;
                }
            }
            Log.Message($"[theyaremany] placed {placed}/{Settings.TurretCount} ancient defense emitters in lodge rooms");
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to place ancient defense emitters: " + ex);
        }
    }

    private static bool TryPlaceTurret(Map map, ThingDef turretDef, Room room, Faction guild)
    {
        foreach (IntVec3 cell in room.Cells)
        {
            if (!CellInRoom(cell, room, map) || CellOccupied(map, cell) || !HasFoundation(map, cell))
            {
                continue;
            }
            Building turret = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(turretDef), cell, map);
            turret?.SetFaction(guild);
            return true;
        }
        return false;
    }

    // One vat per existing room with a free 1x2 footprint.
    private static List<Building_GrowthVat> SetupCloningVats(Map map, Faction guild)
    {
        var vats = new List<Building_GrowthVat>();
        try
        {
            ThingDef vatDef = DefDatabase<ThingDef>.GetNamedSilentFail(AncientCloningVatDefName);
            if (vatDef == null || map.regionGrid == null)
            {
                return vats;
            }
            TerrainDef floorDef = DefDatabase<TerrainDef>.GetNamedSilentFail("AncientTile");
            // Sealed foundation rooms only; vats hold gestating life.
            List<Room> rooms =
            [
                .. map.regionGrid.AllRooms
                    .Where(r => r is { PsychologicallyOutdoors: false, Vacuum: <= 0f, CellCount: >= 10 })
                    .OrderBy(r => RoomDistanceSquared(r, map.Center))
            ];
            foreach (Room room in rooms)
            {
                if (vats.Count >= Settings.VatCount)
                {
                    break;
                }
                if (TryPlaceVat(map, vatDef, floorDef, room, guild, out Building_GrowthVat? vat) && vat != null)
                {
                    vats.Add(vat);
                }
            }
            Log.Message($"[theyaremany] placed {vats.Count}/{Settings.VatCount} ancient cloning vats in lodge rooms");
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to place cloning vats: " + ex);
        }
        return vats;
    }

    // Approximate distance from the room's first cell; only used for ordering.
    private static int RoomDistanceSquared(Room room, IntVec3 center)
    {
        foreach (IntVec3 cell in room.Cells)
        {
            int dx = cell.x - center.x;
            int dz = cell.z - center.z;
            return dx * dx + dz * dz;
        }
        return int.MaxValue;
    }

    private static bool TryPlaceVat(Map map, ThingDef vatDef, TerrainDef floorDef, Room room, Faction guild, out Building_GrowthVat? vat)
    {
        vat = null;
        Rot4 rot = Rot4.North;
        foreach (IntVec3 cell in room.Cells)
        {
            if (!CanPlaceVatAt(map, vatDef, room, cell, rot))
            {
                continue;
            }
            foreach (IntVec3 occupied in GenAdj.CellsOccupiedBy(cell, rot, vatDef.size))
            {
                if (floorDef != null)
                {
                    map.terrainGrid.SetTerrain(occupied, floorDef);
                }
            }
            vat = (Building_GrowthVat)GenSpawn.Spawn(ThingMaker.MakeThing(vatDef), cell, map, rot);
            if (vat != null)
            {
                // The vat (and thus its eventual newborn) belongs to the guild.
                vat.SetFaction(guild);
                StockVatWithEmbryo(vat);
            }
            return true;
        }
        return false;
    }

    // Footprint and interaction cell clear, in-room, on real foundation.
    private static bool CanPlaceVatAt(Map map, ThingDef vatDef, Room room, IntVec3 cell, Rot4 rot)
    {
        foreach (IntVec3 occupied in GenAdj.CellsOccupiedBy(cell, rot, vatDef.size))
        {
            if (!CellInRoom(occupied, room, map) || CellOccupied(map, occupied) || !HasFoundation(map, occupied))
            {
                return false;
            }
        }
        IntVec3 interactionCell = ThingUtility.InteractionCell(vatDef.interactionCellOffset, cell, rot);
        return CellInRoom(interactionCell, room, map)
            && !CellOccupied(map, interactionCell)
            && HasFoundation(map, interactionCell);
    }

    private static bool CellInRoom(IntVec3 cell, Room room, Map map)
    {
        return cell.InBounds(map) && map.regionGrid.GetValidRegionAt(cell)?.Room == room;
    }

    // Every vat holds a gestating embryo; the vat's own path births it.
    private static void StockVatWithEmbryo(Building_GrowthVat vat)
    {
        try
        {
            if (vat.selectedEmbryo != null)
            {
                return;
            }
            HumanEmbryo embryo = (HumanEmbryo)ThingMaker.MakeThing(ThingDefOf.HumanEmbryo);
            if (!vat.innerContainer.TryAddOrTransfer(embryo))
            {
                Log.Error("[theyaremany] failed to seal an embryo into a growth vat");
                return;
            }
            vat.selectedEmbryo = embryo;
            // startTick is the gestation target; "now" would birth on tick one.
            AccessTools.Field(typeof(Building_Enterable), "startTick")
                .SetValue(vat, Find.TickManager.TicksGame + Building_GrowthVat.EmbryoGestationTicks);
            // 60 units feeds the entire 9-day gestation (6/day) even across visits.
            AccessTools.Field(typeof(Building_GrowthVat), "containedNutrition").SetValue(vat, 60f);
        }
        catch (Exception ex)
        {
            Log.Error("[theyaremany] failed to stock a growth vat with an embryo: " + ex);
        }
    }

    // Orbital maps mark off-hull cells as "Space"; never build on those.
    private static bool HasFoundation(Map map, IntVec3 cell)
    {
        TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
        return terrain != null
            && terrain.defName != "Space"
            && !terrain.exposesToVacuum
            && terrain.passability != Traversability.Impassable;
    }

    private static bool CellOccupied(Map map, IntVec3 cell)
    {
        if (!cell.InBounds(map))
        {
            return true;
        }
        foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
        {
            if (thing is Pawn || thing is Building || thing.def.passability == Traversability.Impassable)
            {
                return true;
            }
        }
        return false;
    }
}
