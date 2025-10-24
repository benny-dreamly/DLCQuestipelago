using Core;
using DLCLib.NIS;
using HarmonyLib;
using KaitoKid.ArchipelagoUtilities.Net.Interfaces;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Core.Input;

namespace DLCQuestipelago.QualityOfLife
{
    public static class CutsceneSkipperPatch
    {
        public const string DEFAULT_CUTSCENE_SKIP_KEY = "OemTilde";
        
        private static ILogger _logger;
        private static MethodInfo _completeMethod;
        private static bool _skipRequested = false;

        public static void Initialize(ILogger logger)
        {
            _logger = logger;
            
            // Get the protected Complete method via reflection
            var nisManagerType = typeof(NISManager);
            _completeMethod = AccessTools.Method(nisManagerType, "Complete");
        }

        // Call this from your existing InputPatch to check for skip input during cutscenes
        public static void HandleCutsceneSkipInput(InputState input)
        {
            try
            {
                var currentKeyboardState = input.CurrentKeyboardState;
                var pressedKeys = currentKeyboardState.GetPressedKeys();
                var skipKeysString = Plugin.Instance.APConnectionInfo.CutsceneSkipKey ?? DEFAULT_CUTSCENE_SKIP_KEY;
        
                if (string.IsNullOrWhiteSpace(skipKeysString))
                {
                    return;
                }

                // Parse the entire string as a key name, not character by character
                if (Enum.TryParse<Keys>(skipKeysString, true, out var skipKey))
                {
                    if (pressedKeys.Contains(skipKey))
                    {
                        _skipRequested = true;
                        _logger?.LogInfo("Cutscene skip requested!");
                    }
                }
                else
                {
                    _logger?.LogWarning($"Could not parse cutscene skip key: {skipKeysString}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed in {nameof(CutsceneSkipperPatch)}.{nameof(HandleCutsceneSkipInput)}:\n\t{ex}");
                Debugger.Break();
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(NISManager), "Play")]
        public static IEnumerable<CodeInstruction> SkipCutscenes(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            
            try
            {
                // Find the instruction right after storing onCompleteAction
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Stfld && 
                        codes[i].operand.ToString().Contains("onCompleteAction"))
                    {
                        // Insert our skip logic right after this instruction
                        var newInstructions = new List<CodeInstruction>
                        {
                            // Check if skip was requested
                            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CutsceneSkipperPatch), nameof(CheckAndClearSkipRequest))),
                            new CodeInstruction(OpCodes.Brfalse_S, codes[i + 1]), // Jump to normal flow if false
                            
                            // Skip logic - load 'this' and call Complete()
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Call, _completeMethod)
                        };
                        
                        codes.InsertRange(i + 1, newInstructions);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed in {nameof(CutsceneSkipperPatch)} transpiler:\n\t{ex}");
                Debugger.Break();
            }
            
            return codes;
        }
        
        private static bool CheckAndClearSkipRequest()
        {
            if (_skipRequested)
            {
                _skipRequested = false;
                return true;
            }
            return false;
        }
    }
}