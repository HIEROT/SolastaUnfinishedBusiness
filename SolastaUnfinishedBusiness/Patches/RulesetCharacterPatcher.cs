﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using SolastaUnfinishedBusiness.Api.GameExtensions;
using SolastaUnfinishedBusiness.Api.Helpers;
using SolastaUnfinishedBusiness.Api.LanguageExtensions;
using SolastaUnfinishedBusiness.Behaviors;
using SolastaUnfinishedBusiness.Behaviors.Specific;
using SolastaUnfinishedBusiness.Builders;
using SolastaUnfinishedBusiness.Interfaces;
using SolastaUnfinishedBusiness.Models;
using SolastaUnfinishedBusiness.Subclasses;
using SolastaUnfinishedBusiness.Validators;
using UnityEngine;
using static RuleDefinitions;
using static FeatureDefinitionAttributeModifier;
using static ActionDefinitions;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.CharacterClassDefinitions;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.FeatureDefinitionMagicAffinitys;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.SpellDefinitions;
using MirrorImage = SolastaUnfinishedBusiness.Behaviors.Specific.MirrorImage;

namespace SolastaUnfinishedBusiness.Patches;

[UsedImplicitly]
public static class RulesetCharacterPatcher
{
    // helper to get infusions modifiers from items
    private static void EnumerateFeaturesFromItems<T>(
        RulesetCharacter __instance,
        List<FeatureDefinition> featuresToBrowse,
        Dictionary<FeatureDefinition, FeatureOrigin> featuresOrigin) where T : class
    {
        __instance.EnumerateFeaturesToBrowse<T>(featuresToBrowse, featuresOrigin);
        if (__instance is not RulesetCharacterHero hero)
        {
            return;
        }

        foreach (var definition in hero.CharacterInventory.InventorySlotsByName
                     .Select(keyValuePair => keyValuePair.Value)
                     .Where(slot => slot.EquipedItem != null && !slot.Disabled && !slot.ConfigSlot)
                     .Select(slot => slot.EquipedItem)
                     .SelectMany(equipedItem => equipedItem.DynamicItemProperties
                         .Select(dynamicItemProperty => dynamicItemProperty.FeatureDefinition)
                         .Where(definition => definition != null && definition is T)))
        {
            featuresToBrowse.Add(definition);

            if (featuresOrigin.ContainsKey(definition))
            {
                continue;
            }

            featuresOrigin.Add(
                definition,
                new FeatureOrigin(
                    FeatureSourceType.CharacterFeature, definition.Name, null, definition.ParseSpecialFeatureTags()));
        }

        //PATCH: allow ISpellCastingAffinityProvider to be validated with IsCharacterValidHandler
        featuresToBrowse.RemoveAll(x =>
            !__instance.IsValid(x.GetAllSubFeaturesOfType<IsCharacterValidHandler>()));
    }

    private static void EnumerateFeatureDefinitionRegeneration(
        RulesetCharacter __instance,
        List<FeatureDefinition> featuresToBrowse,
        Dictionary<FeatureDefinition, FeatureOrigin> featuresOrigin)
    {
        __instance.EnumerateFeaturesToBrowse<FeatureDefinitionRegeneration>(featuresToBrowse, featuresOrigin);
        featuresToBrowse.RemoveAll(x =>
            !__instance.IsValid(x.GetAllSubFeaturesOfType<IsCharacterValidHandler>()));
    }

    //PATCH: supports `AddFighterLevelToIndomitableSavingReroll`
    [HarmonyPatch(typeof(RulesetCharacter),
        nameof(RulesetCharacter.UseIndomitableResistance))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class UseIndomitableResistance_Patch
    {
        [UsedImplicitly]
        public static void Prefix(RulesetCharacter __instance)
        {
            if (!Main.Settings.AddFighterLevelToIndomitableSavingReroll)
            {
                return;
            }

            __instance.InflictCondition(
                CharacterContext.ConditionIndomitableSaving.Name,
                DurationType.Round,
                1,
                TurnOccurenceType.StartOfTurn,
                AttributeDefinitions.TagEffect,
                __instance.Guid,
                __instance.CurrentFaction.Name,
                1,
                CharacterContext.ConditionIndomitableSaving.Name,
                0,
                0,
                0);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.IsWieldingMonkWeapon))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class IsWieldingMonkWeapon_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, ref bool __result)
        {
            //PATCH: count wild-shaped heroes with monk classes as wielding monk weapons
            if (__instance is not RulesetCharacterMonster ||
                __instance.OriginalFormCharacter is not RulesetCharacterHero hero)
            {
                return;
            }

            __result = hero.GetClassLevel(Monk) > 0;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.FindFirstRetargetableEffect))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class FindFirstRetargetableEffect_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, ref RulesetEffect __result)
        {
            //PATCH: allow effects retarget even if they have conditions applied to self
            if (__result != null)
            {
                return;
            }

            var effects = __instance.EnumerateActiveEffectsActivatedByMe();
            foreach (var effect in effects)
            {
                if (!effect.EffectDescription.RetargetAfterDeath)
                {
                    continue;
                }

                if (!effect.SourceDefinition.HasSubFeatureOfType<ForceRetargetAvailability>())
                {
                    continue;
                }

                __result = effect;
                return;
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.OnConditionRemoved))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class OnConditionRemoved_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, RulesetCondition activeCondition)
        {
            ProcessConditionsMatchingInterruptionSourceRageStop(__instance, activeCondition);

            //PATCH: support 'EnableCharactersOnFireToEmitLight'
            SrdAndHouseRulesContext.RemoveLightSourceIfNeeded(__instance, activeCondition);

            //PATCH: notifies custom condition features that condition is removed 
            var definition = activeCondition.ConditionDefinition;

            definition.GetAllSubFeaturesOfType<IOnConditionAddedOrRemoved>()
                .Do(c => c.OnConditionRemoved(__instance, activeCondition));

            definition.Features
                .SelectMany(f => f.GetAllSubFeaturesOfType<IOnConditionAddedOrRemoved>())
                .Do(c => c.OnConditionRemoved(__instance, activeCondition));
        }

        // ReSharper disable once SuggestBaseTypeForParameter
        private static void ProcessConditionsMatchingInterruptionSourceRageStop(
            RulesetCharacter sourceCharacter,
            RulesetCondition activeCondition)
        {
            if (!activeCondition.ConditionDefinition.IsSubtypeOf(ConditionRaging))
            {
                return;
            }

            // var rulesetEffectPower =
            //     sourceCharacter.PowersUsedByMe.FirstOrDefault(x =>
            //         x.trackedConditionGuids.Contains(activeCondition.guid));
            //
            // if (rulesetEffectPower != null)
            // {
            //     sourceCharacter.TerminatePower(rulesetEffectPower);
            // }

            var gameLocationCharacterService = ServiceRepository.GetService<IGameLocationCharacterService>();

            if (gameLocationCharacterService == null)
            {
                return;
            }

            foreach (var targetRulesetCharacter in gameLocationCharacterService.AllValidEntities
                         .Select(x => x.RulesetActor)
                         .OfType<RulesetCharacter>()
                         .ToList())
            {
                foreach (var rulesetCondition in targetRulesetCharacter.AllConditions
                             .Where(x =>
                                 x.ConditionDefinition.SpecialInterruptions.Contains(
                                     (ConditionInterruption)ExtraConditionInterruption.SourceRageStop) &&
                                 x.SourceGuid == sourceCharacter.Guid)
                             .ToList())
                {
                    targetRulesetCharacter.RemoveCondition(rulesetCondition);

                    if (targetRulesetCharacter.Guid != rulesetCondition.SourceGuid)
                    {
                        continue;
                    }

                    foreach (var effect in targetRulesetCharacter.PowersUsedByMe
                                 .Where(x => x.Name == rulesetCondition.EffectDefinitionName)
                                 .ToList())
                    {
                        targetRulesetCharacter.TerminatePower(effect);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.GetMaxUsesOfPower))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class GetMaxUsesOfPower_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, ref int __result, RulesetUsablePower usablePower)
        {
            __result += __instance.GetSubFeaturesByType<IModifyPowerPoolAmount>()
                .Where(m => m.PowerPool == usablePower.PowerDefinition)
                .Sum(m => m.PoolChangeAmount(__instance));
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.AcknowledgeAttackedCharacter))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class AcknowledgeAttackedCharacter_Patch
    {
        [UsedImplicitly]
        public static void Postfix([CanBeNull] RulesetCharacter target)
        {
            //PATCH: Allows condition interruption after target was attacked
            target?.ProcessConditionsMatchingInterruption(
                (ConditionInterruption)ExtraConditionInterruption.AfterWasAttacked);
        }
    }

    //PATCH: correctly syncs powers used during WS back to original hero
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.SetupFromSubstituteCharacter))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class SetupFromSubstituteCharacter_Patch
    {
        [UsedImplicitly]
        public static void Prefix(RulesetCharacter __instance, RulesetActor substituteActor)
        {
            if (substituteActor is not RulesetCharacterMonster monster)
            {
                return;
            }

            foreach (var usablePower in monster.UsablePowers)
            {
                var heroUsablePower =
                    __instance.UsablePowers.FirstOrDefault(x => x.PowerDefinition == usablePower.PowerDefinition);

                if (heroUsablePower != null)
                {
                    heroUsablePower.remainingUses = usablePower.remainingUses;
                }
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.GetAbilityScoreOfPower))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class GetAbilityScoreOfPower_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance,
            ref string __result,
            FeatureDefinitionPower featureDefinitionPower)
        {
            //PATCH: allow powers have magic attack bonus based on spell attack
            if (featureDefinitionPower.AttackHitComputation !=
                (PowerAttackHitComputation)ExtraPowerAttackHitComputation.SpellAttack)
            {
                return;
            }

            var user = __instance;

            // this is required by Artillerist which has powers tied to caster
            var summoner = user.GetMySummoner();

            if (summoner != null)
            {
                user = summoner.RulesetCharacter;
            }

            var repertoire = user.GetClassSpellRepertoire(user.FindClassHoldingFeature(featureDefinitionPower));

            if (repertoire == null)
            {
                return;
            }

            __result = repertoire.SpellCastingAbility;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.GetLowestSlotLevelAndRepertoireToCastSpell))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class GetLowestSlotLevelAndRepertoireToCastSpell_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance,
            SpellDefinition spellDefinitionToCast,
            ref int __result,
            ref RulesetSpellRepertoire matchingRepertoire)
        {
            //PATCH: game doesn't consider cantrips gained from BonusCantrips feature
            //because of this issue Inventor can't use Light cantrip from quick-cast button on UI
            //this patch tries to find requested cantrip in repertoire's ExtraSpellsByTag
            if (spellDefinitionToCast.spellLevel != 0 || matchingRepertoire != null)
            {
                return;
            }

            foreach (var repertoire in __instance.SpellRepertoires
                         .Where(repertoire => repertoire.ExtraSpellsByTag
                             .Any(x => x.Value.Contains(spellDefinitionToCast))))
            {
                matchingRepertoire = repertoire;
                __result = 0;

                break;
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.IsComponentSomaticValid))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class IsComponentSomaticValid_Patch
    {
        [UsedImplicitly]
        public static void Postfix(
            RulesetCharacter __instance, ref bool __result, SpellDefinition spellDefinition, ref string failure)
        {
            if (__result)
            {
                return;
            }

            //PATCH: Allows valid Somatic component if specific material component is held in main hand or off hand slots
            // allows casting somatic spells with full hands if one of the hands holds material component for the spell
            ValidateIfMaterialInHand(__instance, spellDefinition, ref __result, ref failure);

            if (__result)
            {
                return;
            }

            //PATCH: Allows valid Somatic component if Inventor has infused item in main hand or off hand slots
            // allows casting somatic spells with full hands if one of the hands holds item infused by the caster
            ValidateIfInfusedInHand(__instance, ref __result, ref failure);
        }

        //TODO: move to separate file
        private static void ValidateIfMaterialInHand(RulesetCharacter caster, SpellDefinition spellDefinition,
            ref bool result, ref string failure)
        {
            if (spellDefinition.MaterialComponentType != MaterialComponentType.Specific)
            {
                return;
            }

            var materialTag = spellDefinition.SpecificMaterialComponentTag;
            var mainHand = caster.GetItemInSlot(EquipmentDefinitions.SlotTypeMainHand);
            var offHand = caster.GetItemInSlot(EquipmentDefinitions.SlotTypeOffHand);
            var tagsMap = new Dictionary<string, TagsDefinitions.Criticity>();

            mainHand?.FillTags(tagsMap, caster, true);
            offHand?.FillTags(tagsMap, caster, true);

            if (!tagsMap.ContainsKey(materialTag))
            {
                return;
            }

            result = true;
            failure = string.Empty;
        }

        //TODO: move to separate file
        private static void ValidateIfInfusedInHand(
            RulesetCharacter caster,
            ref bool result,
            ref string failure)
        {
            var mainHand = caster.GetItemInSlot(EquipmentDefinitions.SlotTypeMainHand);
            var offHand = caster.GetItemInSlot(EquipmentDefinitions.SlotTypeOffHand);

            if (!caster.HoldsMyInfusion(mainHand) && !caster.HoldsMyInfusion(offHand))
            {
                return;
            }

            result = true;
            failure = string.Empty;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.IsComponentMaterialValid))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class IsComponentMaterialValid_Patch
    {
        [UsedImplicitly]
        public static void Postfix(
            RulesetCharacter __instance,
            ref bool __result,
            SpellDefinition spellDefinition,
            ref string failure)
        {
            //PATCH: Allow spells to satisfy material components by using stack of equal or greater value
            StackedMaterialComponent.IsComponentMaterialValid(__instance, spellDefinition, ref failure, ref __result);

            if (__result)
            {
                return;
            }

            //PATCH: Allows spells to satisfy specific material components by actual active tags on an item that are not directly defined in ItemDefinition (like "Melee")
            //Used mostly for melee cantrips requiring melee weapon to cast
            ValidateSpecificComponentsByTags(__instance, spellDefinition, ref __result, ref failure);

            if (__result)
            {
                return;
            }

            //PATCH: Allows spells to satisfy mundane material components if Inventor has infused item equipped
            //Used mostly for melee cantrips requiring melee weapon to cast
            ValidateInfusedFocus(__instance, spellDefinition, ref __result, ref failure);
        }

        //TODO: move to separate file
        private static void ValidateSpecificComponentsByTags(
            RulesetCharacter caster,
            SpellDefinition spell,
            ref bool result,
            ref string failure)
        {
            if (spell.MaterialComponentType != MaterialComponentType.Specific)
            {
                return;
            }

            var materialTag = spell.SpecificMaterialComponentTag;
            var requiredCost = spell.SpecificMaterialComponentCostGp;
            var items = new List<RulesetItem>();

            caster.CharacterInventory.EnumerateAllItems(items);

            var tagsMap = new Dictionary<string, TagsDefinitions.Criticity>();

            foreach (var rulesetItem in items)
            {
                tagsMap.Clear();
                rulesetItem.FillTags(tagsMap, caster, true);

                var itemItemDefinition = rulesetItem.ItemDefinition;
                var costInGold = EquipmentDefinitions.GetApproximateCostInGold(itemItemDefinition.Costs);

                if (tagsMap.ContainsKey(materialTag) && costInGold >= requiredCost)
                {
                    continue;
                }

                result = true;
                failure = string.Empty;
            }
        }

        //TODO: move to separate file
        private static void ValidateInfusedFocus(
            RulesetCharacter caster,
            SpellDefinition spell,
            ref bool result,
            ref string failure)
        {
            if (spell.MaterialComponentType != MaterialComponentType.Mundane)
            {
                return;
            }

            var items = new List<RulesetItem>();

            caster.CharacterInventory.EnumerateAllItems(items);

            if (!items.Any(caster.HoldsMyInfusion))
            {
                return;
            }

            result = true;
            failure = string.Empty;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.SpendSpellMaterialComponentAsNeeded))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class SpendSpellMaterialComponentAsNeeded_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(RulesetCharacter __instance, RulesetEffectSpell activeSpell)
        {
            //PATCH: Modify original code to spend enough of a stack to meet component cost
            return StackedMaterialComponent.SpendSpellMaterialComponentAsNeeded(__instance, activeSpell);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.IsValidReadyCantrip))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class IsValidReadyCantrip_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, ref bool __result,
            SpellDefinition cantrip)
        {
            //PATCH: Modifies validity of ready cantrip action to include attack cantrips even if they don't have damage forms
            //makes melee cantrips valid for ready action
            if (__result)
            {
                return;
            }

            var effect = PowerBundle.ModifySpellEffect(cantrip, __instance);
            var hasDamage = effect.HasFormOfType(EffectForm.EffectFormType.Damage);
            var hasAttack = cantrip.HasSubFeatureOfType<IAttackAfterMagicEffect>();
            var notGadgets = effect.TargetFilteringMethod != TargetFilteringMethod.GadgetOnly;
            var componentsValid = __instance.AreSpellComponentsValid(cantrip);

            __result = (hasDamage || hasAttack) && notGadgets && componentsValid;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.IsSubjectToAttackOfOpportunity))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class IsSubjectToAttackOfOpportunity_Patch
    {
        // ReSharper disable once RedundantAssignment
        [UsedImplicitly]
        public static void Postfix(
            RulesetCharacter __instance, ref bool __result, RulesetCharacter attacker, float distance)
        {
            //PATCH: allows custom exceptions for attack of opportunity triggering
            //Mostly for Sentinel feat
            __result = AttacksOfOpportunity.IsSubjectToAttackOfOpportunity(__instance, attacker, __result, distance);
        }
    }

    //PATCH: ensures that the wildshape heroes or heroes under rage cannot cast any spells (Multiclass)
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.CanCastSpells))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class CanCastSpells_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, ref bool __result)
        {
            // wildshape
            if (__instance.OriginalFormCharacter is RulesetCharacterHero hero && hero != __instance &&
                hero.classesAndLevels.TryGetValue(Druid, out var level) &&
                level < 18)
            {
                __result = false;
            }

            // raging
            if (__instance.HasConditionOfTypeOrSubType(ConditionRaging))
            {
                __result = false;
            }
        }
    }

    //PATCH: ensures that the wildshape hero has access to spell repertoires for calculating slot related features (Multiclass)
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.SpellRepertoires), MethodType.Getter)]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class SpellRepertoires_Getter_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, ref List<RulesetSpellRepertoire> __result)
        {
            if (__instance.OriginalFormCharacter is RulesetCharacterHero hero && hero != __instance)
            {
                __result = hero.SpellRepertoires;
            }
        }
    }

    //PATCH: ensures that original character sorcery point pool is in sync with substitute (Multiclass)
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.CreateSorceryPoints))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class CreateSorceryPoints_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, int slotLevel, RulesetSpellRepertoire repertoire)
        {
            if (__instance.OriginalFormCharacter is RulesetCharacterHero hero && hero != __instance)
            {
                hero.CreateSorceryPoints(slotLevel, repertoire);
            }
        }
    }

    //PATCH: ensures that original character sorcery point pool is in sync with substitute (Multiclass)
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.GainSorceryPoints))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class GainSorceryPoints_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, int sorceryPointsGain)
        {
            if (__instance.OriginalFormCharacter is RulesetCharacterHero hero && hero != __instance)
            {
                hero.GainSorceryPoints(sorceryPointsGain);
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.UsePower))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class UsePower_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, RulesetUsablePower usablePower)
        {
            if (__instance.OriginalFormCharacter is RulesetCharacterHero hero && hero != __instance)
            {
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (usablePower.PowerDefinition.RechargeRate)
                {
                    //PATCH: ensures that original character rage pool is in sync with substitute (Multiclass)
                    case RechargeRate.RagePoints:
                        hero.SpendRagePoint();

                        break;
                    //PATCH: ensures that original character ki pool is in sync with substitute (Multiclass)
                    case RechargeRate.KiPoints:
                        hero.ForceKiPointConsumption(usablePower.PowerDefinition.CostPerUse);

                        break;
                }
            }

            //PATCH: update usage for power pools 
            __instance.UpdateUsageForPower(usablePower, usablePower.PowerDefinition.CostPerUse);

            //PATCH: support for counting uses of power in the UsedSpecialFeatures dictionary of the GameLocationCharacter
            CountPowerUseInSpecialFeatures.Count(__instance, usablePower);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RefreshAttributeModifiersFromConditions))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RefreshAttributeModifiersFromConditions_Patch
    {
        [NotNull]
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            //PATCH: support for validation of attribute modifications applied through conditions
            //Replaces first `IsInst` operator with custom validator

            var validate = new Func<
                FeatureDefinition,
                RulesetCharacter,
                FeatureDefinition
            >(ValidateFeatureApplication.ValidateAttributeModifier).Method;

            return instructions.ReplaceCode(instruction => instruction.opcode == OpCodes.Isinst,
                -1, "RulesetCharacter.RefreshAttributeModifiersFromConditions",
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, validate));
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollAttack))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollAttack_Patch
    {
#if false
        [NotNull]
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            //PATCH: support for Mirror Image - replaces target's AC with 10 + DEX bonus if we targeting mirror image
            var currentValueMethod = typeof(RulesetAttribute).GetMethod("get_CurrentValue");
            var mirrorImageLogicGetACMethod =
                new Func<RulesetAttribute, RulesetActor, List<TrendInfo>, int>(MirrorImageLogic.GetAC).Method;
            var tryModifyCritThresholdMethod = new Func<
                RulesetAttribute, // attribute, 
                RulesetCharacter, // me, 
                RulesetCharacter, // target, 
                BaseDefinition, // attackMethod,
                int //result
            >(TryModifyCritThreshold).Method;

            return instructions
                .ReplaceCall(currentValueMethod,
                    1, "RulesetCharacter.RollAttack.AC",
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Ldarg, 4),
                    new CodeInstruction(OpCodes.Call, mirrorImageLogicGetACMethod))
                //technically second occurence of this getter, but first one is replaced on previous call
                .ReplaceCall(currentValueMethod, 1, "RulesetCharacter.RollAttack.CritThreshold",
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Ldarg_3),
                    new CodeInstruction(OpCodes.Call, tryModifyCritThresholdMethod));
        }
#endif

        private static int TryModifyCritThreshold(
            RulesetAttribute attribute, RulesetCharacter me, RulesetCharacter target, BaseDefinition attackMethod)
        {
            var current = attribute.CurrentValue;

            me.GetSubFeaturesByType<IModifyAttackCriticalThreshold>().ForEach(m =>
                current = m.GetCriticalThreshold(current, me, target, attackMethod));

            return current;
        }

        [UsedImplicitly]
        public static bool Prefix(
            [NotNull] RulesetCharacter __instance,
            out int __result,
            int toHitBonus,
            RulesetActor target,
            BaseDefinition attackMethod,
            List<TrendInfo> toHitTrends,
            bool ignoreAdvantage,
            List<TrendInfo> advantageTrends,
            bool rangeAttack,
            bool opportunity,
            int rollModifier,
            out RollOutcome outcome,
            out int successDelta,
            int predefinedRoll,
            bool testMode,
            ReactionCounterAttackType reactionCounterAttackType = ReactionCounterAttackType.None)
        {
            if (!testMode & opportunity && __instance.AttackOfOpportunity != null)
            {
                __instance.AttackOfOpportunity(__instance, target);
            }

            var firstRoll = predefinedRoll;
            var secondRoll = predefinedRoll;
            var advantageType = ComputeAdvantage(advantageTrends);

            if (ignoreAdvantage)
            {
                advantageType = AdvantageType.None;
                advantageTrends = [];
            }

            var rawRoll = predefinedRoll <= 0
                ? __instance.RollDie(DieType.D20, RollContext.AttackRoll, false, advantageType,
                    out firstRoll, out secondRoll)
                : predefinedRoll;

            successDelta = 0;

            var service = ServiceRepository.GetService<IGameSettingsService>();
            var disableEnemyCrits = __instance.Side == Side.Enemy && service is { DisableEnemyCrits: true };
            int attackRoll;

            if (rawRoll == DiceMaxValue[8] && !disableEnemyCrits)
            {
                attackRoll = rawRoll;
                outcome = RollOutcome.CriticalSuccess;
            }
            else if (rawRoll == DiceMinValue[8])
            {
                attackRoll = rawRoll;
                outcome = RollOutcome.CriticalFailure;
            }
            else
            {
                attackRoll = rawRoll + toHitBonus + rollModifier;

                //PATCH: support for Mirror Image - replaces target's AC with 10 + DEX bonus if we targeting mirror image
                // successDelta = attackRoll - target.GetAttribute("ArmorClass").CurrentValue;
                successDelta = attackRoll -
                               MirrorImage.GetAC(target.GetAttribute("ArmorClass"), target, toHitTrends);
                // END PATCH

                if (successDelta >= 0)
                {
                    var currentValue = DiceMaxValue[8];

                    if (__instance.TryGetAttribute("CriticalThreshold", out var rulesetAttribute))
                    {
                        //PATCH: support `IModifyAttackCriticalThreshold`
                        // currentValue = rulesetAttribute.CurrentValue;
                        currentValue = TryModifyCritThreshold(
                            rulesetAttribute, __instance, target as RulesetCharacter, attackMethod);
                        // END PATCH
                    }

                    outcome = rawRoll < currentValue || disableEnemyCrits
                        ? RollOutcome.Success
                        : RollOutcome.CriticalSuccess;
                }
                else
                {
                    outcome = RollOutcome.Failure;
                }
            }

            var isCriticalAutomaticOnMe = false;

            if (target is RulesetCharacter character
                && outcome == RollOutcome.Success
                && character.IsCriticalAutomaticOnMe(__instance))
            {
                outcome = RollOutcome.CriticalSuccess;
                isCriticalAutomaticOnMe = true;
            }

            if (outcome == RollOutcome.CriticalSuccess
                && target is RulesetCharacter rulesetCharacter
                && rulesetCharacter.IsImmuneToCriticalHits())
            {
                outcome = RollOutcome.Success;
            }

            switch (testMode)
            {
                case true when __instance.AttackInitiated != null:
                    __instance.AttackInitiated(
                        __instance, firstRoll, secondRoll, toHitBonus + rollModifier, advantageType);
                    break;
                case false when __instance.AttackRolled != null:
                {
                    foreach (var toHitTrend in toHitTrends)
                    {
                        if (toHitTrend.dieFlag == TrendInfoDieFlag.None
                            || toHitTrend.value <= 0
                            || toHitTrend.dieType <= DieType.D1)
                        {
                            continue;
                        }

                        var additionalAttackDieRolled =
                            __instance.AdditionalAttackDieRolled;

                        additionalAttackDieRolled?.Invoke(__instance, toHitTrend);
                    }

                    __instance.AttackRolled(
                        __instance, target, attackMethod, outcome, attackRoll, rawRoll, toHitBonus + rollModifier,
                        toHitTrends, advantageTrends, opportunity, reactionCounterAttackType);
                    __instance.AccountAttack(outcome);

                    if (isCriticalAutomaticOnMe)
                    {
                        var automaticCritical = __instance.AttackAutomaticCritical;

                        automaticCritical?.Invoke(target);
                    }

                    break;
                }
            }

            if (!testMode && target.IncomingAttackRolled != null)
            {
                target.IncomingAttackRolled(
                    __instance, target, attackMethod, rangeAttack, outcome, attackRoll,
                    rawRoll, toHitBonus + rollModifier, toHitTrends, advantageTrends);
            }

            __result = rawRoll;

            return false;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollAttackMode))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollAttackMode_Patch
    {
        [UsedImplicitly]
        public static void Prefix(
            [NotNull] RulesetCharacter __instance,
            RulesetAttackMode attackMode,
            RulesetActor target,
            List<TrendInfo> toHitTrends,
            bool testMode)
        {
            //PATCH: support for Mirror Image - checks if we have Mirror Images, rolls for it and adds proper to hit trend to mark this roll
            MirrorImage.AttackRollPrefix(__instance, target, toHitTrends, testMode);

            //PATCH: support Elven Precision - sets up flag if this physical attack is valid 
            ElvenPrecision.PhysicalAttackRollPrefix(__instance, attackMode);
        }

        [UsedImplicitly]
        public static void Postfix(
            RulesetAttackMode attackMode,
            RulesetActor target,
            List<TrendInfo> toHitTrends,
            ref RollOutcome outcome,
            ref int successDelta,
            bool testMode)
        {
            //PATCH: support for Mirror Image - checks if we have Mirror Images, and makes attack miss target and removes 1 image if it was hit
            MirrorImage.AttackRollPostfix(attackMode, target, toHitTrends,
                ref outcome,
                ref successDelta,
                testMode);

            //PATCH: support for Elven Precision - reset flag after physical attack is finished
            ElvenPrecision.Active = false;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollMagicAttack))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollMagicAttack_Patch
    {
        internal static RulesetEffect CurrentMagicEffect;

        [UsedImplicitly]
        public static void Prefix(
            [NotNull] RulesetCharacter __instance,
            RulesetEffect activeEffect,
            RulesetActor target,
            List<TrendInfo> toHitTrends,
            bool testMode)
        {
            CurrentMagicEffect = activeEffect;

            //PATCH: support for Mirror Image - checks if we have Mirror Images, rolls for it and adds proper to hit trend to mark this roll
            MirrorImage.AttackRollPrefix(__instance, target, toHitTrends, testMode);

            //PATCH: support Elven Precision - sets up flag if this physical attack is valid 
            ElvenPrecision.MagicAttackRollPrefix(__instance, activeEffect);
        }

        [UsedImplicitly]
        public static void Postfix(
            RulesetActor target,
            List<TrendInfo> toHitTrends,
            ref RollOutcome outcome,
            ref int successDelta,
            bool testMode)
        {
            //PATCH: support for Mirror Image - checks if we have Mirror Images, and makes attack miss target and removes 1 image if it was hit
            MirrorImage.AttackRollPostfix(null, target, toHitTrends, ref outcome, ref successDelta, testMode);

            //PATCH: support for Elven Precision - reset flag after magic attack is finished
            ElvenPrecision.Active = false;
            CurrentMagicEffect = null;
        }
    }

    //PATCH: IModifyAbilityCheck
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollAbilityCheck))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollAbilityCheck_Patch
    {
        [UsedImplicitly]
        public static void Prefix(
            [NotNull] RulesetCharacter __instance,
            int baseBonus,
            string abilityScoreName,
            string proficiencyName,
            List<TrendInfo> modifierTrends,
            List<TrendInfo> advantageTrends,
            int rollModifier,
            ref int minRoll)
        {
            var features = __instance.GetSubFeaturesByType<IModifyAbilityCheck>();

            if (features.Count <= 0)
            {
                return;
            }

            var newMinRoll = features
                .Max(x => x.MinRoll(__instance, baseBonus, rollModifier, abilityScoreName, proficiencyName,
                    advantageTrends, modifierTrends));

            if (minRoll < newMinRoll)
            {
                minRoll = newMinRoll;
            }
        }
    }

    //PATCH: IModifyAbilityCheck
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.ResolveContestCheck))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class ResolveContestCheck_Patch
    {
        [UsedImplicitly]
        public static int ExtendedRollDie(
            [NotNull] RulesetCharacter rulesetCharacter,
            DieType dieType,
            RollContext rollContext,
            bool isProficient,
            AdvantageType advantageType,
            out int firstRoll,
            out int secondRoll,
            bool enumerateFeatures,
            bool canRerollDice,
            string skill,
            int baseBonus,
            int rollModifier,
            string abilityScoreName,
            string proficiencyName,
            List<TrendInfo> advantageTrends,
            List<TrendInfo> modifierTrends)
        {
            var features = rulesetCharacter.GetSubFeaturesByType<IModifyAbilityCheck>();
            var result = rulesetCharacter.RollDie(dieType, rollContext, isProficient, advantageType,
                out firstRoll, out secondRoll, enumerateFeatures, canRerollDice, skill);

            if (features.Count <= 0)
            {
                return result;
            }

            var newMinRoll = features
                .Max(x => x.MinRoll(rulesetCharacter, baseBonus, rollModifier, abilityScoreName, proficiencyName,
                    advantageTrends, modifierTrends));

            if (result < newMinRoll)
            {
                result = newMinRoll;
            }

            return result;
        }

        //
        // there are 2 calls to RollDie on this method
        // we replace them to allow us to compare the die result vs. the minRoll value from any IModifyAbilityCheck feature
        //
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            var rollDieMethod = typeof(RulesetActor).GetMethod("RollDie");
            var extendedRollDieMethod = typeof(ResolveContestCheck_Patch).GetMethod("ExtendedRollDie");

            return instructions
                // first call to roll die checks the initiator
                .ReplaceCall(rollDieMethod,
                    1, "RulesetCharacter.ResolveContestCheck.RollDie1",
                    new CodeInstruction(OpCodes.Ldarg, 1), // baseBonus
                    new CodeInstruction(OpCodes.Ldarg, 2), // rollModifier
                    new CodeInstruction(OpCodes.Ldarg, 3), // abilityScoreName
                    new CodeInstruction(OpCodes.Ldarg, 4), // proficiencyName
                    new CodeInstruction(OpCodes.Ldarg, 5), // advantageTrends
                    new CodeInstruction(OpCodes.Ldarg, 6), // modifierTrends
                    new CodeInstruction(OpCodes.Call, extendedRollDieMethod))
                // second call to roll die checks the opponent
                .ReplaceCall(
                    rollDieMethod, // in fact this is 2nd occurence on game code but as we replaced on previous step we set to 1
                    1, "RulesetCharacter.ResolveContestCheck.RollDie2",
                    new CodeInstruction(OpCodes.Ldarg, 7), // opponentBaseBonus
                    new CodeInstruction(OpCodes.Ldarg, 8), // opponentRollModifier
                    new CodeInstruction(OpCodes.Ldarg, 9), // opponentAbilityScoreName
                    new CodeInstruction(OpCodes.Ldarg, 10), // opponentProficiencyName
                    new CodeInstruction(OpCodes.Ldarg, 11), // opponentAdvantageTrends
                    new CodeInstruction(OpCodes.Ldarg, 12), // opponentModifierTrends
                    new CodeInstruction(OpCodes.Call, extendedRollDieMethod));
        }
    }

    //PATCH: logic to correctly calculate spell slots under MC (Multiclass)
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RefreshSpellRepertoires))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RefreshSpellRepertoires_Patch
    {
        [NotNull]
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            //PATCH: make ISpellCastingAffinityProvider from dynamic item properties apply to repertoires
            return instructions.ReplaceEnumerateFeaturesToBrowse<ISpellCastingAffinityProvider>(
                "RulesetCharacter.RefreshSpellRepertoires",
                EnumerateFeaturesFromItems<ISpellCastingAffinityProvider>);
        }

        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance)
        {
            if (__instance is not RulesetCharacterHero hero || !SharedSpellsContext.IsMulticaster(hero))
            {
                return;
            }

            var slots = new Dictionary<int, int>();

            // adds features slots
            foreach (var additionalSlot in hero.FeaturesToBrowse
                         .OfType<FeatureDefinitionMagicAffinity>()
                         // special Warlock case so we should discard it here
                         .Where(x => x != MagicAffinityChitinousBoonAdditionalSpellSlot)
                         .OfType<ISpellCastingAffinityProvider>()
                         .SelectMany(x => x.AdditionalSlots))
            {
                slots.TryAdd(additionalSlot.SlotLevel, 0);
                slots[additionalSlot.SlotLevel] += additionalSlot.SlotsNumber;
            }

            // adds spell slots
            var sharedCasterLevel = SharedSpellsContext.GetSharedCasterLevel(hero);
            var sharedSpellLevel = SharedSpellsContext.GetSharedSpellLevel(hero);

            for (var i = 1; i <= sharedSpellLevel; i++)
            {
                slots.TryAdd(i, 0);
                slots[i] += SharedSpellsContext.FullCastingSlots[sharedCasterLevel - 1].Slots[i - 1];
            }

            var warlockSpellLevel = SharedSpellsContext.GetWarlockSpellLevel(hero);

            for (var i = 1; i <= warlockSpellLevel; i++)
            {
                slots.TryAdd(i, 0);
                slots[i] += SharedSpellsContext.GetWarlockMaxSlots(hero);
            }

            // reassign slots back to repertoires except for race ones
            foreach (var spellRepertoire in hero.SpellRepertoires
                         .Where(x => x.SpellCastingFeature.SpellCastingOrigin
                             is FeatureDefinitionCastSpell.CastingOrigin.Class
                             or FeatureDefinitionCastSpell.CastingOrigin.Subclass))
            {
                spellRepertoire.spellsSlotCapacities = slots.DeepCopy();
                spellRepertoire.RepertoireRefreshed?.Invoke(spellRepertoire);
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.CanCastSpellOfActionType))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class CanCastSpellOfActionType_Patch
    {
        [UsedImplicitly]
        public static void Postfix(
            RulesetCharacter __instance,
            ref bool __result,
            ActionType actionType,
            bool canOnlyUseCantrips)
        {
            if (__result)
            {
                return;
            }

            if (actionType == ActionType.Bonus &&
                (__instance.GetOriginalHero()?.HasAnyFeature(PatronEldritchSurge.FeatureBlastReload) ?? false))
            {
                __result = true;

                return;
            }

            //PATCH: update usage for power pools
            foreach (var invocation in __instance.Invocations)
            {
                var definition = invocation.InvocationDefinition;

                if (definition is not InvocationDefinitionCustom)
                {
                    continue;
                }

                var spell = definition.GrantedSpell;

                if (spell == null)
                {
                    continue;
                }

                if (canOnlyUseCantrips && spell.spellLevel > 0)
                {
                    continue;
                }

                var isValid = definition
                    .GetAllSubFeaturesOfType<IsInvocationValidHandler>()
                    .All(v => v(__instance, definition));

                if (definition.HasSubFeatureOfType<ModifyInvocationVisibility>() || !isValid)
                {
                    continue;
                }

                var battleActionId = spell.BattleActionId;

                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (actionType)
                {
                    case ActionType.Main:
                        if (battleActionId != Id.CastMain)
                        {
                            continue;
                        }

                        break;
                    case ActionType.Bonus:
                        if (battleActionId != Id.CastBonus)
                        {
                            continue;
                        }

                        break;
                    default:
                        continue;
                }

                if (!invocation.IsAvailable(__instance))
                {
                    continue;
                }

                __result = true;

                return;
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RepayPowerUse))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RepayPowerUse_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, RulesetUsablePower usablePower)
        {
            //PATCH: update usage for power pools
            __instance.UpdateUsageForPower(usablePower, -usablePower.PowerDefinition.CostPerUse);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.GrantPowers))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class GrantPowers_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance)
        {
            //PATCH: update usage for power pools
            PowerBundle.RechargeLinkedPowers(__instance, RestType.LongRest);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.ApplyRest))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class ApplyRest_Patch
    {
        [UsedImplicitly]
        public static void Postfix(
            RulesetCharacter __instance, RestType restType, bool simulate)
        {
            //PATCH: update usage for power pools
            if (!simulate)
            {
                PowerBundle.RechargeLinkedPowers(__instance, restType);
            }

            // The player isn't recharging the shared pool features, just the pool.
            // Hide the features that use the pool from the UI.
            foreach (var feature in __instance.RecoveredFeatures.Where(f => f is FeatureDefinitionPowerSharedPool)
                         .ToArray())
            {
                __instance.RecoveredFeatures.Remove(feature);
            }

            //PATCH: support for invocations that recharge on short rest (like Fey Teleportation feat)
            foreach (var invocation in __instance.Invocations
                         .Where(invocation =>
                             invocation.InvocationDefinition.HasSubFeatureOfType<RechargeInvocationOnShortRest>()))
            {
                invocation.Recharge();
            }

            //PATCH: support for Barbarians to regain one rage point at short rests from level 7
            // ReSharper disable once InvertIf
            if (Main.Settings.EnableBarbarianRegainOneRageAtShortRest &&
                restType == RestType.ShortRest &&
                __instance.GetClassLevel(Barbarian) >= 7)
            {
                if (__instance.UsedRagePoints > 0 && !simulate)
                {
                    __instance.UsedRagePoints--;
                }

                __instance.recoveredFeatures.Add(__instance.GetFeaturesByType<FeatureDefinitionAttributeModifier>()
                    .FirstOrDefault(attributeModifier =>
                        attributeModifier.ModifiedAttribute == AttributeDefinitions.RagePoints));
            }
        }

        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            //PATCH: Makes powers that have their max usage extended by pool modifiers show up correctly during rest
            //replace calls to MaxUses getter to custom method that accounts for extended power usage
            var bind = typeof(RulesetUsablePower).GetMethod("get_MaxUses",
                BindingFlags.Public | BindingFlags.Instance);
            var maxUses =
                new Func<RulesetUsablePower, RulesetCharacter, int>(GetMaxUsesOfPower).Method;
            var restoreAllSpellSlotsMethod = typeof(RulesetSpellRepertoire).GetMethod("RestoreAllSpellSlots");
            var myRestoreAllSpellSlotsMethod =
                new Action<RulesetSpellRepertoire, RulesetCharacter, RestType>(RestoreAllSpellSlots).Method;

            return instructions
                .ReplaceCalls(bind,
                    "RulesetCharacter.ApplyRest.MaxUses",
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, maxUses))
                .ReplaceCalls(restoreAllSpellSlotsMethod,
                    "RulesetCharacter.ApplyRest.RestoreAllSpellSlots",
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, myRestoreAllSpellSlotsMethod));
        }

        private static int GetMaxUsesOfPower(
            [NotNull] RulesetUsablePower poolPower,
            [NotNull] RulesetCharacter character)
        {
            return character.GetMaxUsesOfPower(poolPower);
        }

        private static void RestoreAllSpellSlots(
            RulesetSpellRepertoire __instance,
            RulesetCharacter rulesetCharacter,
            RestType restType)
        {
            if (restType == RestType.LongRest
                || rulesetCharacter is not RulesetCharacterHero hero
                || !SharedSpellsContext.IsMulticaster(hero))
            {
                rulesetCharacter.RestoreAllSpellSlots();

                return;
            }

            var warlockSpellLevel = SharedSpellsContext.GetWarlockSpellLevel(hero);
            var warlockUsedSlots = SharedSpellsContext.GetWarlockUsedSlots(hero);

            foreach (var spellRepertoire in hero.SpellRepertoires
                         .Where(x => x.SpellCastingFeature.SpellCastingOrigin
                             is FeatureDefinitionCastSpell.CastingOrigin.Class
                             or FeatureDefinitionCastSpell.CastingOrigin.Subclass))
            {
                for (var i = SharedSpellsContext.PactMagicSlotsTab; i <= warlockSpellLevel; i++)
                {
                    if (spellRepertoire.usedSpellsSlots.ContainsKey(i))
                    {
                        spellRepertoire.usedSpellsSlots[i] -= warlockUsedSlots;
                    }
                }

                spellRepertoire.RepertoireRefreshed?.Invoke(spellRepertoire);
            }
        }
    }

    //PATCH: ensures auto prepared spells from subclass are considered on level up
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.ComputeAutopreparedSpells))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class ComputeAutopreparedSpells_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(
            [NotNull] RulesetCharacter __instance, [NotNull] RulesetSpellRepertoire spellRepertoire)
        {
            //BEGIN PATCH
            var spellcastingClass = spellRepertoire.SpellCastingClass;

            if (spellcastingClass == null && spellRepertoire.SpellCastingSubclass != null)
            {
                spellcastingClass = LevelUpContext.GetClassForSubclass(spellRepertoire.SpellCastingSubclass);
            }
            //END PATCH

            // this includes all the logic for the base function
            spellRepertoire.AutoPreparedSpells.Clear();
            __instance.EnumerateFeaturesToBrowse<FeatureDefinitionAutoPreparedSpells>(__instance.FeaturesToBrowse);

            var features = __instance.FeaturesToBrowse.OfType<FeatureDefinitionAutoPreparedSpells>();

            foreach (var autoPreparedSpells in features)
            {
                var matcher = autoPreparedSpells.GetFirstSubFeatureOfType<RepertoireValidForAutoPreparedFeature>();
                bool matches;

                if (matcher == null)
                {
                    matches = autoPreparedSpells.SpellcastingClass == spellcastingClass;
                }
                else
                {
                    matches = matcher(spellRepertoire, __instance);
                }

                if (!matches)
                {
                    continue;
                }

                var classLevel = __instance.GetSpellcastingLevel(spellRepertoire);

                foreach (var preparedSpellsGroup in autoPreparedSpells.AutoPreparedSpellsGroups
                             .Where(preparedSpellsGroup => preparedSpellsGroup.ClassLevel <= classLevel))
                {
                    spellRepertoire.AutoPreparedSpells.AddRange(preparedSpellsGroup.SpellsList);
                    spellRepertoire.AutoPreparedTag = autoPreparedSpells.AutoPreparedTag;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollInitiative))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollInitiative_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(RulesetCharacter __instance, out int __result, int forcedInitiative)
        {
            //PATCH: allows summons to have forced initiative of a summoner
            if (__instance.HasSubFeatureOfType<ForceInitiativeToSummoner>())
            {
                var summoner = __instance.GetMySummoner();

                if (summoner != null)
                {
                    forcedInitiative = summoner.lastInitiative;
                }
            }
            else if (Main.Settings.EnemiesAlwaysRollInitiative &&
                     __instance.Side == Side.Enemy)
            {
                forcedInitiative = -1;
            }

            __result = RollInitiative(__instance, forcedInitiative);

            return false;
        }

        private static int RollInitiative(RulesetCharacter __instance, int forcedInitiative)
        {
            int resultRoll;

            if (forcedInitiative <= 0)
            {
                var advantageValue = 0;

                //PATCH: supports AddDexModifierToEnemiesInitiativeRoll
                var currentValue =
                    Main.Settings.AddDexModifierToEnemiesInitiativeRoll &&
                    __instance is RulesetCharacterMonster
                        ? AttributeDefinitions.ComputeAbilityScoreModifier(
                            __instance.TryGetAttributeValue(AttributeDefinitions.Dexterity))
                        : __instance.GetAttribute(AttributeDefinitions.Initiative).CurrentValue;

                __instance.RefreshInitiative(ref advantageValue);

                var advantageType = advantageValue switch
                {
                    > 0 => AdvantageType.Advantage,
                    < 0 => AdvantageType.Disadvantage,
                    _ => AdvantageType.None
                };

                var rollDie = __instance.RollDie(
                    DieType.D20, RollContext.InitiativeRoll, false, advantageType,
                    out var firstRoll, out var secondRoll);

                resultRoll = Mathf.Clamp(rollDie + currentValue, 1, int.MaxValue);

                __instance.LastInitiativeRoll = rollDie;
                __instance.LastInitiativeModifier = currentValue;

                var initiativeRolled = __instance.InitiativeRolled;

                initiativeRolled?.Invoke(__instance, resultRoll, advantageType, firstRoll, currentValue, secondRoll);
            }
            else
            {
                resultRoll = forcedInitiative;
            }

            return resultRoll;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RefreshUsablePower))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RefreshUsablePower_Patch
    {
        [UsedImplicitly]
        public static void Prefix(RulesetCharacter __instance,
            RulesetUsablePower usablePower,
            ref RulesetSpellRepertoire classSpellRepertoire)
        {
            //PATCH: MC: try getting proper class repertoire for the power
            var powerOriginClass = usablePower.OriginClass;

            //Only try to get repertoire for powers that have origin class
            if (powerOriginClass == null)
            {
                return;
            }

            var repertoire = __instance.GetClassSpellRepertoire(powerOriginClass);

            if (repertoire != null)
            {
                classSpellRepertoire = repertoire;
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RefreshUsableDeviceFunctions))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RefreshUsableDeviceFunctions_Patch
    {
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            var isFunctionAvailable = typeof(RulesetItemDevice).GetMethod("IsFunctionAvailable");
            var customMethod = typeof(RefreshUsableDeviceFunctions_Patch).GetMethod("IsFunctionAvailable");

            return instructions.ReplaceCalls(isFunctionAvailable,
                "RulesetCharacter.RefreshUsableDeviceFunctions",
                new CodeInstruction(OpCodes.Call, customMethod));
        }

        [UsedImplicitly]
        public static bool IsFunctionAvailable(RulesetItemDevice device,
            RulesetDeviceFunction function,
            RulesetCharacter character,
            bool inCombat,
            bool usedMainSpell,
            bool usedBonusSpell,
            bool ignoreActivationTimeChecks,
            out string failureFlag)
        {
            //PATCH: allow PowerVisibilityModifier to make power device functions visible even if not valid
            //used to make Grenadier's grenade functions not be be hidden when you have not enough charges
            var result = device.IsFunctionAvailable(function, character, inCombat, usedMainSpell, usedBonusSpell,
                ignoreActivationTimeChecks, out failureFlag);

            if (result || function.DeviceFunctionDescription.type != DeviceFunctionDescription.FunctionType.Power)
            {
                return result;
            }

            var power = function.DeviceFunctionDescription.FeatureDefinitionPower;

            if (ModifyPowerVisibility.IsPowerHidden(character, power, ActionType.Main)
                || !character.CanUsePower(power, false))
            {
                return false;
            }

            failureFlag = string.Empty;

            return true;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.ComputeSpeedAddition))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class ComputeSpeedAddition_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance, IMovementAffinityProvider provider,
            ref int __result)
        {
            if (provider is not FeatureDefinition feature)
            {
                return;
            }

            var modifier = feature.GetFirstSubFeatureOfType<IModifyMovementSpeedAddition>();

            if (modifier != null)
            {
                __result += modifier.ModifySpeedAddition(__instance, provider);
            }
        }
    }

    //PATCH: implement IPreventRemoveConcentrationOnPowerUse
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.TerminateSpell))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class TerminateSpell_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(RulesetCharacter __instance)
        {
            return !CharacterActionExtensions
                .ShouldKeepConcentrationOnPowerUseOrSpend(__instance); // abort if should keep
        }
    }

    //PATCH: implement IPreventRemoveConcentrationOnPowerUse
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.TerminatePower))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class TerminatePower_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(RulesetCharacter __instance)
        {
            return !CharacterActionExtensions
                .ShouldKeepConcentrationOnPowerUseOrSpend(__instance); // abort if should keep
        }
    }

    //PATCH: support adding required action affinities to classes that can use toggles
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.PostLoad))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class PostLoad_Patch
    {
        [UsedImplicitly]
        public static void Postfix(RulesetCharacter __instance)
        {
            if (__instance is not RulesetCharacterHero hero)
            {
                return;
            }

            if (hero.ClassesHistory.Contains(Paladin))
            {
                var tag = AttributeDefinitions.GetClassTag(Paladin, 1);

                switch (Main.Settings.AddPaladinSmiteToggle)
                {
                    case true:
                        if (!hero.HasAnyFeature(GameUiContext.ActionAffinityPaladinSmiteToggle))
                        {
                            hero.ActiveFeatures[tag].Add(GameUiContext.ActionAffinityPaladinSmiteToggle);
                            hero.EnableToggle((Id)ExtraActionId.PaladinSmiteToggle);
                        }

                        break;
                    case false:
                        if (hero.HasAnyFeature(GameUiContext.ActionAffinityPaladinSmiteToggle))
                        {
                            hero.ActiveFeatures[tag].Remove(GameUiContext.ActionAffinityPaladinSmiteToggle);
                        }

                        hero.EnableToggle((Id)ExtraActionId.PaladinSmiteToggle);
                        break;
                }
            }

            if (hero.ActiveFeatures
                .SelectMany(k => k.Value)
                .OfType<FeatureDefinitionPower>()
                .Any(power => hero.GetPowerFromDefinition(power) == null))
            {
                Main.Info($"Hero [{hero.Name}] had missing powers, granting them");
                hero.GrantPowers();
            }

            hero.RefreshAll();
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollConcentrationCheck))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollConcentrationCheck_Patch
    {
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            var myComputeBaseSavingThrowBonus =
                new Func<RulesetActor, string, List<TrendInfo>, int>(ComputeBaseSavingThrowBonus)
                    .Method;

            var myComputeSavingThrowModifier =
                new Action<RulesetActor, string, EffectForm.EffectFormType, string, string, string, string, string,
                        ActionModifier, List<ISavingThrowAffinityProvider>, int>(ComputeSavingThrowModifier)
                    .Method;

            var myGetSavingThrowModifier =
                new Func<ActionModifier, string, bool, RulesetActor, int>(GetSavingThrowModifier).Method;

            var computeBaseSavingThrowBonus = typeof(RulesetActor).GetMethod("ComputeBaseSavingThrowBonus");
            var computeSavingThrowModifier = typeof(RulesetActor).GetMethod("ComputeSavingThrowModifier");
            var getSavingThrowModifier = typeof(ActionModifier).GetMethod("GetSavingThrowModifier");

            //PATCH: make ISpellCastingAffinityProvider from dynamic item properties apply to repertoires
            return instructions
                //PATCH: supports changing the concentration attribute score
                .ReplaceCalls(computeBaseSavingThrowBonus,
                    "RulesetCharacter.RollConcentrationCheck.ComputeBaseSavingThrowBonus",
                    new CodeInstruction(OpCodes.Call, myComputeBaseSavingThrowBonus))
                .ReplaceCalls(computeSavingThrowModifier,
                    "RulesetCharacter.RollConcentrationCheck.ComputeSavingThrowModifier",
                    new CodeInstruction(OpCodes.Call, myComputeSavingThrowModifier))
                .ReplaceCalls(getSavingThrowModifier,
                    "RulesetCharacter.RollConcentrationCheck.GetSavingThrowModifier",
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, myGetSavingThrowModifier))
                //PATCH: allow modifiers from items to be considered on concentration checks
                .ReplaceEnumerateFeaturesToBrowse<ISpellCastingAffinityProvider>(
                    "RulesetCharacter.RollConcentrationCheck.EnumerateSpellCastingAffinities",
                    EnumerateFeaturesFromItems<ISpellCastingAffinityProvider>);
        }

        private static void GetBestSavingThrowAbilityScore(RulesetActor rulesetActor, ref string attributeScore)
        {
            var savingThrowBonus =
                AttributeDefinitions.ComputeAbilityScoreModifier(
                    rulesetActor.TryGetAttributeValue(attributeScore)) +
                rulesetActor.ComputeBaseSavingThrowBonus(attributeScore, []);

            foreach (var attribute in rulesetActor
                         .GetSubFeaturesByType<IModifyConcentrationAttribute>()
                         .Where(x => x.IsValid(rulesetActor))
                         .Select(x => x.ConcentrationAttribute(rulesetActor)))
            {
                var newSavingThrowBonus =
                    AttributeDefinitions.ComputeAbilityScoreModifier(rulesetActor.TryGetAttributeValue(attribute)) +
                    rulesetActor.ComputeBaseSavingThrowBonus(attribute, []);

                // get the last one instead unless we start using this with other subs and then need to decide which one is better
                if (newSavingThrowBonus <= savingThrowBonus)
                {
                    continue;
                }

                attributeScore = attribute;
                savingThrowBonus = newSavingThrowBonus;
            }
        }

        private static int ComputeBaseSavingThrowBonus(
            RulesetActor __instance,
            string abilityScoreName,
            List<TrendInfo> savingThrowModifierTrends)
        {
            GetBestSavingThrowAbilityScore(__instance, ref abilityScoreName);

            return __instance.ComputeBaseSavingThrowBonus(abilityScoreName, savingThrowModifierTrends);
        }

        private static void ComputeSavingThrowModifier(
            RulesetActor __instance,
            string abilityType,
            EffectForm.EffectFormType formType,
            string sourceName,
            string schoolOfMagic,
            string damageType,
            string conditionType,
            string sourceFamily,
            ActionModifier effectModifier,
            List<ISavingThrowAffinityProvider> accountedProviders,
            int savingThrowContextField = 0)
        {
            GetBestSavingThrowAbilityScore(__instance, ref abilityType);

            __instance.ComputeSavingThrowModifier(abilityType, formType, sourceName, schoolOfMagic, damageType,
                conditionType, sourceFamily, effectModifier, accountedProviders, savingThrowContextField);
        }

        private static int GetSavingThrowModifier(
            ActionModifier __instance,
            string abilityType,
            bool ignoreCover,
            RulesetActor rulesetActor)
        {
            GetBestSavingThrowAbilityScore(rulesetActor, ref abilityType);

            return __instance.GetSavingThrowModifier(abilityType, ignoreCover);
        }
    }


    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollConcentrationCheckFromDamage))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollConcentrationCheckFromDamage_Patch
    {
        //PATCH: allow modifiers from items to be considered on concentration checks
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            //PATCH: make FeatureDefinitionMagicAffinity from dynamic item properties apply to repertoires
            return instructions.ReplaceEnumerateFeaturesToBrowse<FeatureDefinitionMagicAffinity>(
                "RulesetCharacter.RollConcentrationCheckFromDamage",
                EnumerateFeaturesFromItems<FeatureDefinitionMagicAffinity>);
        }
    }

    //PATCH: allow FeatureDefinitionRegeneration to be validated with IsCharacterValidHandler
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.ComputeActiveRegenerationFeatures))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class FindBestRegenerationFeature_Patch
    {
        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            //PATCH: make ISpellCastingAffinityProvider from dynamic item properties apply to repertoires
            return instructions.ReplaceEnumerateFeaturesToBrowse<FeatureDefinitionRegeneration>(
                "RulesetCharacter.FindBestRegenerationFeature", EnumerateFeatureDefinitionRegeneration);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter),
        nameof(RulesetCharacter.CanAttackOutcomeFromAlterationMagicalEffectFail))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class CanAttackOutcomeFromAlterationMagicalEffectFail_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(
            RulesetCharacter __instance,
            out bool __result,
            List<EffectForm> effectForms,
            int totalAttack)
        {
            __result = CanAttackOutcomeFromAlterationMagicalEffectFail(__instance, effectForms, totalAttack);

            return false;
        }

        //PATCH: allow AddProficiencyBonus to be considered on attribute modifiers used on reaction attacks
        private static bool CanAttackOutcomeFromAlterationMagicalEffectFail(
            RulesetCharacter __instance,
            // ReSharper disable once ParameterTypeCanBeEnumerable.Local
            List<EffectForm> effectForms,
            int totalAttack)
        {
            foreach (var feature in effectForms
                         .Where(effectForm => effectForm.FormType == EffectForm.EffectFormType.Condition &&
                                              effectForm.ConditionForm.Operation ==
                                              ConditionForm.ConditionOperation.Add).SelectMany(effectForm =>
                             effectForm.ConditionForm.ConditionDefinition.Features))
            {
                if (feature is not FeatureDefinitionAttributeModifier
                    {
                        ModifiedAttribute: AttributeDefinitions.ArmorClass
                    } attributeModifier ||
                    (attributeModifier.ModifierOperation != AttributeModifierOperation.Additive &&
                     attributeModifier.ModifierOperation != AttributeModifierOperation.AddProficiencyBonus))
                {
                    continue;
                }

                var currentValue = __instance.RefreshArmorClass(dryRun: true, dryRunFeature: feature).CurrentValue;

                __instance.GetAttribute(AttributeDefinitions.ArmorClass).ReleaseCopy();

                if (currentValue <= totalAttack)
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }

    //PATCH: Support College of Valiance level 6 feature that won't spend a dice on a failure
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.RollBardicInspirationDie))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class RollBardicInspirationDie_Patch
    {
        // this is standard game code except for the BEGIN/END patch area
        [UsedImplicitly]
        public static bool Prefix(
            RulesetCharacter __instance,
            out int __result,
            RulesetCondition sourceCondition,
            int successDelta,
            bool forceMaxValue = false,
            bool advantage = false)
        {
            var secondRoll = -1;
            var firstRoll = DiceMaxValue[(int)sourceCondition.BardicInspirationDie];

            if (!forceMaxValue)
            {
                RollDie(sourceCondition.BardicInspirationDie,
                    advantage ? AdvantageType.Advantage : AdvantageType.None,
                    out firstRoll, out secondRoll);
            }

            __result = Mathf.Max(firstRoll, secondRoll);

            var success = __result >= Mathf.Abs(successDelta);

            __instance.BardicInspirationDieUsed?.Invoke(
                __instance, sourceCondition.BardicInspirationDie, firstRoll, secondRoll, success, advantage);

            __instance.ProcessConditionsMatchingInterruption(ConditionInterruption.BardicInspirationUsed);

            //BEGIN PATCH
            if (!success && CollegeOfValiance.IsValianceLevel6(sourceCondition.SourceGuid))
            {
                return false;
            }
            //END PATCH

            __instance.RemoveCondition(sourceCondition);

            return false;
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.FindClassHoldingFeature))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class FindClassHoldingFeature_Patch
    {
        [UsedImplicitly]
        public static void Postfix(
            RulesetCharacter __instance,
            FeatureDefinition featureDefinition,
            ref CharacterClassDefinition __result)
        {
            var gameLocationCharacter = __instance.GetMySummoner();
            var rulesetCharacter = gameLocationCharacter?.RulesetCharacter ?? __instance;

            //PATCH: replaces feature holding class with one provided by custom interface
            //used for features that are not granted directly through class but need to scale with class levels
            var classHolder = featureDefinition.GetFirstSubFeatureOfType<IModifyAdditionalDamageClassLevel>()?.Class;

            if (classHolder == null)
            {
                return;
            }

            // Only override if the character actually has levels in the class, to prevent errors
            var levels = rulesetCharacter.GetClassLevel(classHolder);

            if (levels > 0)
            {
                __result = classHolder;
            }
        }
    }

    //PATCH: implements a better algorithm on SpendSpellSlot to support more than 1 affinity that preserves slot roll
    [HarmonyPatch(typeof(RulesetCharacter), nameof(RulesetCharacter.SpendSpellSlot))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class SpendSpellSlot_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(RulesetCharacter __instance, RulesetEffectSpell activeSpell)
        {
            FeatureDefinition preserveSlotThresholdFeature = null;
            var preserveSlotThreshold = Int32.MaxValue;

            var effectLevel = activeSpell.EffectLevel;

            if (effectLevel > 0)
            {
                foreach (var featureDefinition in __instance.GetFeaturesByType<ISpellCastingAffinityProvider>()
                             .Where(featureDefinition => featureDefinition.PreserveSlotRoll
                                                         && featureDefinition.PreserveSlotLevelCap >= effectLevel))
                {
                    preserveSlotThreshold =
                        Math.Min(preserveSlotThreshold, featureDefinition.PreserveSlotThreshold);
                    preserveSlotThresholdFeature = (FeatureDefinition)featureDefinition;
                }

                var rolledValue = 0;

                if (preserveSlotThreshold != Int32.MaxValue)
                {
                    rolledValue = RollDie(DieType.D20, AdvantageType.None, out _, out _);
                }

                if (rolledValue >= preserveSlotThreshold)
                {
                    var caster = GameLocationCharacter.GetFromActor(__instance);

                    if (caster != null)
                    {
                        EffectHelpers.StartVisualEffect(caster, caster, LesserRestoration,
                            EffectHelpers.EffectType.Caster);
                    }

                    __instance.SpellSlotPreserved?.Invoke(__instance, preserveSlotThresholdFeature, rolledValue);
                }
                else
                {
                    activeSpell.SpellRepertoire.SpendSpellSlot(activeSpell.SlotLevel);
                }
            }

            __instance.AccountUsedMagicAndPower(activeSpell.SlotLevel);

            return false;
        }
    }
}
