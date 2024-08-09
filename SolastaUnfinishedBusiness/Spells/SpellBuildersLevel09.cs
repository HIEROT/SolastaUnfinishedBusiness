﻿using System.Collections;
using System.Linq;
using SolastaUnfinishedBusiness.Api.Helpers;
using SolastaUnfinishedBusiness.Builders;
using SolastaUnfinishedBusiness.Builders.Features;
using SolastaUnfinishedBusiness.CustomUI;
using SolastaUnfinishedBusiness.Interfaces;
using SolastaUnfinishedBusiness.Properties;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.FeatureDefinitionPowers;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.MonsterDefinitions;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.SpellDefinitions;
using static RuleDefinitions;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.FeatureDefinitionAbilityCheckAffinitys;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.FeatureDefinitionCombatAffinitys;
using static SolastaUnfinishedBusiness.Api.DatabaseHelper.FeatureDefinitionSavingThrowAffinitys;

namespace SolastaUnfinishedBusiness.Spells;

internal static partial class SpellBuilders
{
    #region Foresight

    internal static SpellDefinition BuildForesight()
    {
        const string NAME = "Foresight";

        return SpellDefinitionBuilder
            .Create(NAME)
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite(NAME, Resources.Foresight, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolTransmutation)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Minute1)
            .SetMaterialComponent(MaterialComponentType.Mundane)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Divination)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetDurationData(DurationType.Hour, 8)
                    .SetTargetingData(Side.Ally, RangeType.Touch, 0, TargetType.IndividualsUnique)
                    .SetEffectForms(
                        EffectFormBuilder.ConditionForm(
                            ConditionDefinitionBuilder
                                .Create(ConditionDefinitions.ConditionBearsEndurance, "ConditionForesight")
                                .SetOrUpdateGuiPresentation(Category.Condition)
                                .SetFeatures(
                                    AbilityCheckAffinityConditionBearsEndurance,
                                    AbilityCheckAffinityConditionBullsStrength,
                                    AbilityCheckAffinityConditionCatsGrace,
                                    AbilityCheckAffinityConditionEaglesSplendor,
                                    AbilityCheckAffinityConditionFoxsCunning,
                                    AbilityCheckAffinityConditionOwlsWisdom,
                                    CombatAffinityStealthy,
                                    SavingThrowAffinityShelteringBreeze)
                                .AddToDB()))
                    .SetParticleEffectParameters(DispelMagic)
                    .Build())
            .AddToDB();
    }

    #endregion

    #region Invulnerability

    internal static SpellDefinition BuildInvulnerability()
    {
        const string NAME = "Invulnerability";

        var conditionInvulnerability = ConditionDefinitionBuilder
            .Create($"Condition{NAME}")
            .SetGuiPresentation(NAME, Category.Spell, ConditionDefinitions.ConditionShielded)
            .SetFeatures(
                DatabaseRepository.GetDatabase<DamageDefinition>()
                    .Select(damageType =>
                        FeatureDefinitionDamageAffinityBuilder.Create($"DamageAffinity{NAME}{damageType.Name}")
                            .SetGuiPresentationNoContent(true)
                            .SetDamageType(damageType.Name)
                            .SetDamageAffinityType(DamageAffinityType.Immunity)
                            .AddToDB())
                    .ToList())
            .CopyParticleReferences(DispelEvilAndGood)
            .AddToDB();

        conditionInvulnerability.GuiPresentation.description = Gui.EmptyContent;

        return SpellDefinitionBuilder
            .Create(NAME)
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite(NAME, Resources.Invulnerability, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolAbjuration)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.Specific)
            .SetSpecificMaterialComponent(TagsDefinitions.ItemTagDiamond, 500, true)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Buff)
            .SetRequiresConcentration(true)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetDurationData(DurationType.Minute, 10)
                    .SetTargetingData(Side.All, RangeType.Self, 0, TargetType.Self)
                    .SetEffectForms(EffectFormBuilder.ConditionForm(conditionInvulnerability))
                    .SetParticleEffectParameters(DispelMagic)
                    .SetCasterEffectParameters(HolyAura)
                    .Build())
            .AddToDB();
    }

    #endregion

    #region Mass Heal

    internal static SpellDefinition BuildMassHeal()
    {
        return SpellDefinitionBuilder
            .Create("MassHeal")
            .SetGuiPresentation(Category.Spell, Heal)
            .SetCastingTime(ActivationTime.Action)
            .SetSpellLevel(9)
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolTransmutation)
            .SetMaterialComponent(MaterialComponentType.None)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Healing)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetTargetingData(Side.All, RangeType.Distance, 12, TargetType.IndividualsUnique, 6)
                    .SetEffectForms(
                        EffectFormBuilder
                            .Create()
                            .SetHealingForm(
                                HealingComputation.Dice,
                                120,
                                DieType.D1,
                                0,
                                false,
                                HealingCap.MaximumHitPoints)
                            .Build())
                    .SetParticleEffectParameters(Heal)
                    .Build())
            .AddToDB();
    }

    #endregion

    #region Meteor Swarm

    internal static SpellDefinition BuildMeteorSwarmSingleTarget()
    {
        const string NAME = "MeteorSwarmSingleTarget";

        return SpellDefinitionBuilder
            .Create(NAME)
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite(NAME, Resources.MeteorSwarm, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolTransmutation)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.None)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Attack)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetTargetingData(Side.All, RangeType.Distance, 18, TargetType.Sphere, 8)
                    // 20 dice number because hits dont stack even on single target
                    .SetEffectForms(
                        EffectFormBuilder
                            .Create()
                            .SetDamageForm(DamageTypeFire, 20, DieType.D6)
                            .HasSavingThrow(EffectSavingThrowType.HalfDamage)
                            .Build(),
                        EffectFormBuilder
                            .Create()
                            .SetDamageForm(DamageTypeBludgeoning, 20, DieType.D6)
                            .HasSavingThrow(EffectSavingThrowType.HalfDamage)
                            .Build())
                    .SetSavingThrowData(
                        false,
                        AttributeDefinitions.Dexterity,
                        true,
                        EffectDifficultyClassComputation.SpellCastingFeature,
                        AttributeDefinitions.Dexterity,
                        13)
                    .SetParticleEffectParameters(FlameStrike)
                    .Build())
            .AddToDB();
    }

    #endregion

    #region Power Word Heal

    internal static SpellDefinition BuildPowerWordHeal()
    {
        return SpellDefinitionBuilder
            .Create("PowerWordHeal")
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite("PowerWordHeal", Resources.PowerWordHeal, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolEnchantment)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.None)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Healing)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetTargetingData(Side.Ally, RangeType.Distance, 12, TargetType.IndividualsUnique)
                    .SetEffectForms(
                        EffectFormBuilder
                            .Create()
                            .SetHealingForm(
                                HealingComputation.Dice,
                                700,
                                DieType.D1,
                                0,
                                false,
                                HealingCap.MaximumHitPoints)
                            .Build(),
                        EffectFormBuilder
                            .Create()
                            .SetConditionForm(
                                ConditionDefinitions.ConditionParalyzed,
                                ConditionForm.ConditionOperation.RemoveDetrimentalAll,
                                false,
                                false,
                                ConditionDefinitions.ConditionCharmed,
                                ConditionDefinitions.ConditionFrightened,
                                ConditionDefinitions.ConditionParalyzed,
                                ConditionDefinitions.ConditionProne)
                            .Build())
                    .SetParticleEffectParameters(Regenerate)
                    .Build())
            .AddToDB();
    }

    #endregion

    #region Power Word Kill

    internal static SpellDefinition BuildPowerWordKill()
    {
        return SpellDefinitionBuilder
            .Create("PowerWordKill")
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite("PowerWordKill", Resources.PowerWordKill, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolTransmutation)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.None)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Attack)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetTargetingData(Side.Enemy, RangeType.Distance, 12, TargetType.IndividualsUnique)
                    .SetEffectForms(
                        EffectFormBuilder
                            .Create()
                            .SetKillForm(KillCondition.UnderHitPoints, 0F, 100)
                            .Build())
                    .SetParticleEffectParameters(FingerOfDeath)
                    .Build())
            .AddToDB();
    }

    #endregion

    #region Time Stop

    internal static SpellDefinition BuildTimeStop()
    {
        const string NAME = "TimeStop";

        var conditionTimeStop = ConditionDefinitionBuilder
            .Create("ConditionTimeStop")
            .SetGuiPresentation(Category.Condition, Sprites.GetSprite(NAME, Resources.ConditionTimeStop, 27, 32))
            .SetSilent(Silent.WhenAddedOrRemoved)
            .AddCustomSubFeatures(new ActionFinishedByMeTimeStop())
            .AddToDB();

        return SpellDefinitionBuilder
            .Create(NAME)
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite(NAME, Resources.TimeStop, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolTransmutation)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.None)
            .SetSomaticComponent(false)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Divination)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetDurationData(DurationType.Permanent)
                    .SetTargetingData(Side.All, RangeType.Self, 0, TargetType.Self)
                    .SetEffectForms(EffectFormBuilder.ConditionForm(conditionTimeStop))
                    .SetParticleEffectParameters(DispelMagic)
                    .Build())
            .AddCustomSubFeatures(new CustomBehaviorTimeStop())
            .AddToDB();
    }

    private sealed class ActionFinishedByMeTimeStop 
        : IActionFinishedByMe, ICharacterBeforeTurnEndListener, IOnConditionAddedOrRemoved
    {
        public IEnumerator OnActionFinishedByMe(CharacterAction action)
        {
            var character = action.ActingCharacter;
            var targets = action.ActionParams.TargetCharacters;
            var hasNonSelfTarget = targets.Any(x => x != character);

            if (!hasNonSelfTarget)
            {
                yield break;
            }

            var rulesetCharacter = character.RulesetCharacter;

            if (rulesetCharacter.TryGetConditionOfCategoryAndType(
                    AttributeDefinitions.TagEffect, "ConditionTimeStop", out var activeCondition))
            {
                rulesetCharacter.RemoveCondition(activeCondition);
            }
        }

        public void OnConditionAdded(RulesetCharacter target, RulesetCondition rulesetCondition)
        {

        }

        public void OnConditionRemoved(RulesetCharacter target, RulesetCondition rulesetCondition)
        {
            RemoveDuplicateContenders(GameLocationCharacter.GetFromActor(target));
        }
        
        public void OnCharacterBeforeTurnEnded(GameLocationCharacter locationCharacter)
        {
            var battle = Gui.Battle;

            if (battle == null || battle.CurrentRound > 1)
            {
                return;
            }

            var index = battle.InitiativeSortedContenders.FindLastIndex(x => x.Guid == locationCharacter.Guid);
            
            if (battle.activeContenderIndex != index)
            {
                return;
            }

            RemoveDuplicateContenders(locationCharacter);
        }

        private static void RemoveDuplicateContenders(GameLocationCharacter locationCharacter)
        {
            var battle = Gui.Battle;
            
            while (battle.InitiativeSortedContenders.Count(x => x == locationCharacter) > 1)
            {
                var index = battle.InitiativeSortedContenders.FindLastIndex(x => x.Guid == locationCharacter.Guid);
                
                battle.InitiativeSortedContenders.RemoveAt(index);
            }
            
            var gameLocationScreenBattle = Gui.GuiService.GetScreen<GameLocationScreenBattle>();

            gameLocationScreenBattle.initiativeTable.ContenderModified(locationCharacter,
                GameLocationBattle.ContenderModificationMode.Remove, false, false); 
        }
    }
    
    private sealed class CustomBehaviorTimeStop : IPowerOrSpellFinishedByMe
    {
        public IEnumerator OnPowerOrSpellFinishedByMe(CharacterActionMagicEffect action, BaseDefinition baseDefinition)
        {
            var locationCharacter = action.ActingCharacter;
            var rulesetCharacter = locationCharacter.RulesetCharacter;
            var initiativeSortedContenders = Gui.Battle.InitiativeSortedContenders;
            var positionCharacter = initiativeSortedContenders.FirstOrDefault(x => x == locationCharacter);
            var positionCharacterIndex = initiativeSortedContenders.IndexOf(positionCharacter);
            var dieRoll = RollDie(DieType.D4, AdvantageType.None, out _, out _);

            rulesetCharacter.LogCharacterActivatesAbility(
                string.Empty,
                "Feedback/&TimeStop",
                extra:
                [
                    (ConsoleStyleDuplet.ParameterType.Base, dieRoll.ToString())
                ]);
            
            for (var i = 0; i < dieRoll; i++)
            {
                initiativeSortedContenders.Insert(positionCharacterIndex + 1, locationCharacter);
            }
            
            var gameLocationScreenBattle = Gui.GuiService.GetScreen<GameLocationScreenBattle>();

            gameLocationScreenBattle.initiativeTable.ContenderModified(locationCharacter,
                GameLocationBattle.ContenderModificationMode.Add, false, false);
            
            yield break;
        }
    }
    
    #endregion

    #region Weird

    internal static SpellDefinition BuildWeird()
    {
        return SpellDefinitionBuilder
            .Create("Weird")
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite("Weird", Resources.Weird, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolIllusion)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.None)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Attack)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetDurationData(DurationType.Minute, 1)
                    .SetTargetingData(Side.Enemy, RangeType.Distance, 12, TargetType.Sphere, 6)
                    .SetSavingThrowData(
                        false,
                        AttributeDefinitions.Wisdom,
                        true,
                        EffectDifficultyClassComputation.SpellCastingFeature,
                        AttributeDefinitions.Constitution,
                        13)
                    .SetEffectForms(
                        EffectFormBuilder
                            .Create()
                            .SetConditionForm(
                                ConditionDefinitionBuilder
                                    .Create(ConditionDefinitions.ConditionFrightenedPhantasmalKiller, "ConditionWeird")
                                    .SetOrUpdateGuiPresentation(Category.Condition)
                                    .AddToDB(),
                                ConditionForm.ConditionOperation.Add)
                            .HasSavingThrow(EffectSavingThrowType.Negates, TurnOccurenceType.EndOfTurn, true)
                            .Build())
                    .SetCasterEffectParameters(PhantasmalKiller)
                    .SetImpactEffectParameters(
                        PhantasmalKiller.EffectDescription.EffectParticleParameters.effectParticleReference)
                    .Build())
            .SetRequiresConcentration(true)
            .AddToDB();
    }

    #endregion

    #region Psychic Scream

    internal static SpellDefinition BuildPsychicScream()
    {
        const string NAME = "PsychicScream";

        return SpellDefinitionBuilder
            .Create(NAME)
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite(NAME, Resources.PsychicScream, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolEnchantment)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.Mundane)
            .SetSomaticComponent(true)
            .SetVerboseComponent(false)
            .SetVocalSpellSameType(VocalSpellSemeType.Attack)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetDurationData(DurationType.Minute, 1)
                    .SetTargetingData(Side.All, RangeType.Distance, 18, TargetType.IndividualsUnique, 10)
                    .SetSavingThrowData(false, AttributeDefinitions.Intelligence, false,
                        EffectDifficultyClassComputation.SpellCastingFeature)
                    .SetEffectForms(
                        EffectFormBuilder
                            .Create()
                            .HasSavingThrow(EffectSavingThrowType.Negates, TurnOccurenceType.EndOfTurn, true)
                            .SetConditionForm(ConditionDefinitions.ConditionStunned,
                                ConditionForm.ConditionOperation.Add)
                            .Build(),
                        EffectFormBuilder
                            .Create()
                            .HasSavingThrow(EffectSavingThrowType.HalfDamage)
                            .SetDamageForm(DamageTypePsychic, 14, DieType.D6)
                            .Build())
                    .SetParticleEffectParameters(PowerWordStun)
                    .Build())
            .AddToDB();
    }

    #endregion

    #region Shapechange

    internal const string ShapechangeName = "Shapechange";

    internal static SpellDefinition BuildShapechange()
    {
        return SpellDefinitionBuilder
            .Create(ShapechangeName)
            .SetGuiPresentation(Category.Spell, Sprites.GetSprite(ShapechangeName, Resources.ShapeChange, 128))
            .SetSchoolOfMagic(SchoolOfMagicDefinitions.SchoolTransmutation)
            .SetSpellLevel(9)
            .SetCastingTime(ActivationTime.Action)
            .SetMaterialComponent(MaterialComponentType.Specific)
            .SetSpecificMaterialComponent("Diamond", 1500, false)
            .SetSomaticComponent(true)
            .SetVerboseComponent(true)
            .SetVocalSpellSameType(VocalSpellSemeType.Buff)
            .SetEffectDescription(
                EffectDescriptionBuilder
                    .Create()
                    .SetParticleEffectParameters(PowerDruidWildShape)
                    .SetDurationData(DurationType.Hour, 1)
                    .SetTargetingData(Side.Ally, RangeType.Self, 0, TargetType.Self)
                    .SetEffectForms(
                        EffectFormBuilder
                            .Create()
                            .SetShapeChangeForm(
                                ShapeChangeForm.Type.FreeListSelection,
                                true,
                                ConditionDefinitions.ConditionWildShapeSubstituteForm,
                                [
                                    new ShapeOptionDescription
                                    {
                                        requiredLevel = 1, substituteMonster = BlackDragon_MasterOfNecromancy
                                    },
                                    new ShapeOptionDescription { requiredLevel = 1, substituteMonster = Divine_Avatar },
                                    new ShapeOptionDescription
                                    {
                                        requiredLevel = 1, substituteMonster = Emperor_Laethar
                                    },
                                    new ShapeOptionDescription { requiredLevel = 1, substituteMonster = Giant_Ape },
                                    new ShapeOptionDescription
                                    {
                                        requiredLevel = 1, substituteMonster = GoldDragon_AerElai
                                    },
                                    new ShapeOptionDescription
                                    {
                                        requiredLevel = 1, substituteMonster = GreenDragon_MasterOfConjuration
                                    },
                                    new ShapeOptionDescription { requiredLevel = 1, substituteMonster = Remorhaz },
                                    new ShapeOptionDescription { requiredLevel = 1, substituteMonster = Spider_Queen },
                                    new ShapeOptionDescription
                                    {
                                        requiredLevel = 1, substituteMonster = Sorr_Akkath_Shikkath
                                    },
                                    new ShapeOptionDescription
                                    {
                                        requiredLevel = 1, substituteMonster = Sorr_Akkath_Tshar_Boss
                                    }
                                ])
                            .Build())
                    .Build())
            .SetRequiresConcentration(true)
            .AddToDB();
    }

    #endregion
}
