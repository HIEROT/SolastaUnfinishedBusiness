﻿using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using JetBrains.Annotations;
using SolastaUnfinishedBusiness.Api.GameExtensions;

namespace SolastaUnfinishedBusiness.Patches;

[UsedImplicitly]
public static class ActionDefinitionsPatcher
{
    [HarmonyPatch(typeof(ActionDefinitions), nameof(ActionDefinitions.CanActionHaveMultipleGuiItems))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class CanActionHaveMultipleGuiItems_Patch
    {
        [UsedImplicitly]
        public static void Postfix(ref bool __result, ActionDefinitions.Id actionId)
        {
            //PATCH: (ExtraAttacksOnActionPanel) allow multiple offhand attacks on action panel
            if (actionId == ActionDefinitions.Id.AttackOff)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ActionDefinitions), nameof(ActionDefinitions.IsProxyAction))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class IsProxyAction_Patch
    {
        [UsedImplicitly]
        public static void Postfix(ref bool __result, ActionDefinitions.Id actionId, bool includeFree = true)
        {
            if (actionId == (ActionDefinitions.Id)ExtraActionId.ProxyHoundWeapon ||
                actionId == (ActionDefinitions.Id)ExtraActionId.ProxyPactWeapon ||
                actionId == (ActionDefinitions.Id)ExtraActionId.ProxyPetalStorm ||
                (actionId == (ActionDefinitions.Id)ExtraActionId.ProxyHoundWeaponFree && includeFree) ||
                (actionId == (ActionDefinitions.Id)ExtraActionId.ProxyPactWeaponFree && includeFree))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ActionDefinitions), nameof(ActionDefinitions.IsProxyFreeAction))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class IsProxyFreeAction_Patch
    {
        [UsedImplicitly]
        public static void Postfix(ref bool __result, ActionDefinitions.Id actionId)
        {
            if (actionId
                is (ActionDefinitions.Id)ExtraActionId.ProxyHoundWeaponFree
                or (ActionDefinitions.Id)ExtraActionId.ProxyPactWeaponFree)
            {
                __result = true;
            }
        }
    }
}
