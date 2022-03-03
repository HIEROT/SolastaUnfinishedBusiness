﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using SolastaCommunityExpansion.CustomFeatureDefinitions;
using SolastaCommunityExpansion.Models;

namespace SolastaCommunityExpansion.Patches.CustomFeatures
{
    //
    // INotifyConditionRemoval
    //
    [HarmonyPatch(typeof(RulesetCharacter), "Kill")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_Kill
    {
        internal static void Prefix(RulesetCharacter __instance)
        {
            foreach (var keyValuePair in __instance.ConditionsByCategory)
            {
                foreach (RulesetCondition rulesetCondition in keyValuePair.Value)
                {
                    if (rulesetCondition?.ConditionDefinition is INotifyConditionRemoval notifiedDefinition)
                    {
                        notifiedDefinition.BeforeDyingWithCondition(__instance, rulesetCondition);
                    }
                }
            }
        }
    }

    //
    // IStartOfTurnRecharge
    //
    [HarmonyPatch(typeof(RulesetCharacter), "RechargePowersForTurnStart")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_RechargePowersForTurnStart
    {
        internal static void Postfix(RulesetCharacter __instance)
        {
            foreach (RulesetUsablePower usablePower in __instance.UsablePowers)
            {
                if (usablePower?.PowerDefinition is IStartOfTurnRecharge startOfTurnRecharge && usablePower.RemainingUses < usablePower.MaxUses)
                {
                    usablePower.Recharge();

                    if (!startOfTurnRecharge.IsRechargeSilent && __instance.PowerRecharged != null)
                    {
                        __instance.PowerRecharged(__instance, usablePower);
                    }
                }
            }
        }
    }

    //
    // IChangeAbilityCheck
    //
    [HarmonyPatch(typeof(RulesetCharacter), "RollAbilityCheck")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_RollAbilityCheck
    {
        internal static void Postfix(
            RulesetCharacter __instance,
            int baseBonus,
            int rollModifier,
            string abilityScoreName,
            string proficiencyName,
            ref int minRoll)
        {
            minRoll = RulesetCharacter_ResolveContestCheck.GetNewResult(__instance, baseBonus, rollModifier, abilityScoreName, proficiencyName, minRoll);
        }
    }

    //
    // IChangeAbilityCheck
    //
    [HarmonyPatch(typeof(RulesetCharacter), "ResolveContestCheck")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_ResolveContestCheck
    {
        internal static int GetNewResult(
            RulesetCharacter rulesetCharacter,
            int baseBonus,
            int rollModifier,
            string abilityScoreName,
            string proficiencyName,
            int result)
        {
            var min = 9999;
            var max = 0;
            var featuresToBrowse = new List<FeatureDefinition>();

            rulesetCharacter.EnumerateFeaturesToBrowse<IChangeAbilityCheck>(featuresToBrowse);

            foreach (var feature in featuresToBrowse.OfType<IChangeAbilityCheck>())
            {
                min = Math.Min(min, feature.MinAbilityCheck(rulesetCharacter, baseBonus, rollModifier, abilityScoreName, proficiencyName));
                max = Math.Max(max, feature.MaxAbilityCheck(rulesetCharacter, baseBonus, rollModifier, abilityScoreName, proficiencyName));
            }

            if (max < min)
            {
                return result;
            }

            if (result < min)
            {
                result = min;
            }

            if (result > max)
            {
                result = max;
            }

            return result;
        }

        public static int ExtendedRollDie(
            RulesetCharacter rulesetCharacter,
            RuleDefinitions.DieType dieType,
            RuleDefinitions.RollContext rollContext,
            bool isProficient,
            RuleDefinitions.AdvantageType advantageType,
            out int firstRoll,
            out int secondRoll,
            bool enumerateFeatures,
            bool canRerollDice,
            int baseBonus,
            int rollModifier,
            string abilityScoreName,
            string proficiencyName)
        {
            var result = rulesetCharacter.RollDie(dieType, rollContext, isProficient, advantageType, out firstRoll, out secondRoll, enumerateFeatures, canRerollDice);

            return GetNewResult(
                rulesetCharacter,
                baseBonus,
                rollModifier,
                abilityScoreName,
                proficiencyName, 
                result);
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var found = 0;
            var rollDieMethod = typeof(RulesetActor).GetMethod("RollDie");
            var extendedRollDieMethod = typeof(RulesetCharacter_ResolveContestCheck).GetMethod("ExtendedRollDie");

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(rollDieMethod))
                {
                    ++found;

                    // first call to roll die checks the initiator
                    if (found == 1)
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg, 1); // baseBonus
                        yield return new CodeInstruction(OpCodes.Ldarg, 2); // rollModifier
                        yield return new CodeInstruction(OpCodes.Ldarg, 3); // abilityScoreName
                        yield return new CodeInstruction(OpCodes.Ldarg, 4); // proficiencyName
                    }
                    // second call to roll die checks the opponent
                    else if (found == 2)
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg, 7); // opponentBaseBonus
                        yield return new CodeInstruction(OpCodes.Ldarg, 8); // opponentRollModifier
                        yield return new CodeInstruction(OpCodes.Ldarg, 9); // opponentAbilityScoreName
                        yield return new CodeInstruction(OpCodes.Ldarg, 10); // opponentProficiencyName
                    }

                    yield return new CodeInstruction(OpCodes.Call, extendedRollDieMethod);
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }

    //
    // Power Related Patches
    //
    [HarmonyPatch(typeof(RulesetCharacter), "UsePower")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_UsePower
    {
        public static void Postfix(RulesetCharacter __instance, RulesetUsablePower usablePower)
        {
            __instance.UpdateUsageForPowerPool(usablePower, usablePower.PowerDefinition.CostPerUse);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), "RepayPowerUse")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_RepayPowerUse
    {
        public static void Postfix(RulesetCharacter __instance, RulesetUsablePower usablePower)
        {
            __instance.UpdateUsageForPowerPool(usablePower, -usablePower.PowerDefinition.CostPerUse);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), "GrantPowers")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_GrantPowers
    {
        public static void Postfix(RulesetCharacter __instance)
        {
            CustomFeaturesContext.RechargeLinkedPowers(__instance, RuleDefinitions.RestType.LongRest);
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), "ApplyRest")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_ApplyRest
    {
        internal static void Postfix(
            RulesetCharacter __instance, RuleDefinitions.RestType restType, bool simulate)
        {
            if (!simulate)
            {
                CustomFeaturesContext.RechargeLinkedPowers(__instance, restType);
            }

            // The player isn't recharging the shared pool features, just the pool.
            // Hide the features that use the pool from the UI.
            foreach (FeatureDefinition feature in __instance.RecoveredFeatures.Where(f => f is IPowerSharedPool).ToArray())
            {
                __instance.RecoveredFeatures.Remove(feature);
            }
        }
    }

    [HarmonyPatch(typeof(RulesetCharacter), "ComputeAutopreparedSpells")]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class RulesetCharacter_ComputeAutopreparedSpells_Patch
    {
        internal static bool Prefix(RulesetCharacter __instance, RulesetSpellRepertoire spellRepertoire)
        {
            if (!Main.Settings.SupportAutoPreparedSpellsOnSubclassCasters)
            {
                return true;
            }

            CharacterClassDefinition spellcastingClass = spellRepertoire.SpellCastingClass;
            if (spellRepertoire.SpellCastingSubclass != null)
            {
                spellcastingClass = GetClassForSubclass(spellRepertoire.SpellCastingSubclass);
            }

            spellRepertoire.AutoPreparedSpells.Clear();
            __instance.EnumerateFeaturesToBrowse<FeatureDefinitionAutoPreparedSpells>(__instance.FeaturesToBrowse);
            foreach (FeatureDefinition featureDefinition in __instance.FeaturesToBrowse)
            {
                FeatureDefinitionAutoPreparedSpells autoPreparedSpells = featureDefinition as FeatureDefinitionAutoPreparedSpells;
                if (autoPreparedSpells.SpellcastingClass == spellcastingClass)
                {
                    foreach (FeatureDefinitionAutoPreparedSpells.AutoPreparedSpellsGroup preparedSpellsGroup in autoPreparedSpells.AutoPreparedSpellsGroups)
                    {
                        if (preparedSpellsGroup.ClassLevel <= GetSpellcastingLevel(__instance, spellRepertoire))
                        {
                            spellRepertoire.AutoPreparedSpells.AddRange(preparedSpellsGroup.SpellsList);
                            spellRepertoire.AutoPreparedTag = autoPreparedSpells.AutoPreparedTag;
                        }
                    }
                }
            }
            // This includes all the logic for the base function and a little extra, so skip it.
            return false;
        }

        private static int GetSpellcastingLevel(RulesetCharacter character, RulesetSpellRepertoire spellRepertoire)
        {
            if (character is RulesetCharacterHero hero)
            {
                if (spellRepertoire.SpellCastingClass != null)
                {
                    return hero.ClassesAndLevels[spellRepertoire.SpellCastingClass];
                }
                if (spellRepertoire.SpellCastingSubclass != null)
                {
                    return hero.ComputeSubclassLevel(spellRepertoire.SpellCastingSubclass);
                }
            }
            return character.GetAttribute(AttributeDefinitions.CharacterLevel).BaseValue;
        }

        private static CharacterClassDefinition GetClassForSubclass(CharacterSubclassDefinition subclass)
        {
            return DatabaseRepository.GetDatabase<CharacterClassDefinition>().FirstOrDefault(klass =>
            {
                return klass.FeatureUnlocks.Any(unlock =>
                {
                    if (unlock.FeatureDefinition is FeatureDefinitionSubclassChoice subclassChoice)
                    {
                        return subclassChoice.Subclasses.Contains(subclass.Name);
                    }
                    return false;
                });
            });
        }
    }
}
