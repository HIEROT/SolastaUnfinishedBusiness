﻿using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using SolastaCommunityExpansion.Models;

namespace SolastaCommunityExpansion.Patches.GameUi.CharacterInspection
{
    [HarmonyPatch(typeof(GuiCharacter), "MainClassDefinition", MethodType.Getter)]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class GuiCharacter_MainClassDefinition_Getter
    {
        internal static void Postfix(ref CharacterClassDefinition __result)
        {
            if (!Main.Settings.EnableEnhancedCharacterInspection)
            {
                return;
            }

            // NOTE: don't use SelectedClass??. which bypasses Unity object lifetime check
            if (InspectionPanelContext.SelectedClass)
            {
                __result = InspectionPanelContext.SelectedClass;
            }
        }
    }

    [HarmonyPatch(typeof(GuiCharacter), "LevelAndClassAndSubclass", MethodType.Getter)]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class GuiCharacter_LevelAndClassAndSubclass_Getter
    {
        internal static void Postfix(GuiCharacter __instance, ref string __result)
        {
            if (__instance.RulesetCharacterHero.ClassesAndLevels.Count == 1)
            {
                return;
            }

            __result = MulticlassGameUiContext.GetAllClassesLabel(__instance, '-');
        }
    }

    [HarmonyPatch(typeof(GuiCharacter), "ClassAndLevel", MethodType.Getter)]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class GuiCharacter_ClassAndLevel_Getter
    {
        internal static void Postfix(GuiCharacter __instance, ref string __result)
        {
            if (__instance.RulesetCharacterHero.ClassesAndLevels.Count == 1)
            {
                return;
            }

            __result = MulticlassGameUiContext.GetAllClassesLabel(__instance, '-');
        }
    }

    // shouldn't be protected as we touch translation terms and this will abort otherwise
    [HarmonyPatch(typeof(GuiCharacter), "LevelAndExperienceTooltip", MethodType.Getter)]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    internal static class GuiCharacter_LevelAndExperienceTooltip_Getter
    {
        internal static void Postfix(GuiCharacter __instance, ref string __result)
        {
            if (__instance.RulesetCharacterHero.ClassesAndLevels.Count == 1)
            {
                return;
            }

            __result = MulticlassGameUiContext.GetLevelAndExperienceTooltip(__instance);
        }
    }
}
