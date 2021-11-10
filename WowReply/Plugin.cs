using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace WowReply
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "WoW Reply";


        private ChatGui ChatGui { get; init; }

        private readonly IntPtr processChatInputAddress;

        Hook<ProcessChatInputDelegate> processChatInputHook;

        private readonly CommandManager commandManager;

        private string lastPlayer = string.Empty;

        private bool debugMode = false;

        private const string debugCommand = "/wowreplydebug";

        public unsafe void Enable()
        {
            processChatInputHook = new Hook<ProcessChatInputDelegate>(processChatInputAddress, new ProcessChatInputDelegate(ProcessChatInputDetour));
            processChatInputHook?.Enable();
        }

        public Plugin([RequiredVersion("1.0")] ChatGui chatGui, [RequiredVersion("1.0")] SigScanner sigScanner, [RequiredVersion("1.0")] CommandManager commandManager)
        {
            this.ChatGui = chatGui;
            this.commandManager = commandManager;

            chatGui.ChatMessage += ChatGui_ChatMessage;

            processChatInputAddress = sigScanner.ScanText("E8 ?? ?? ?? ?? FE 86 ?? ?? ?? ?? C7 86 ?? ?? ?? ?? ?? ?? ?? ??");

            PluginLog.Information("Got processChat input address!");
            Enable();
            PluginLog.Information("Appears to be enabled!");

            commandManager.AddHandler(debugCommand, new CommandInfo(ToggleDebug));
        }

        private void ToggleDebug(string command, string _)
        {
            this.debugMode = !this.debugMode;

            ChatGui.Print(debugMode ? "Debug mode enabled. All messages will be handled as /tells." : "Debug mode disabled.");
        }

        private void ChatGui_ChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if(type == XivChatType.TellIncoming || debugMode)
            {
                var playerPayload = sender.Payloads.FirstOrDefault(x => x is PlayerPayload) as PlayerPayload;
                
                if(playerPayload != null)
                {
                    PluginLog.LogInformation($"Tell received from: {playerPayload.PlayerName}@{playerPayload.World.Name}");
                    lastPlayer = $"{playerPayload.PlayerName}@{playerPayload.World.Name}";
                }

            }
        }

        public void Dispose()
        {
            commandManager.RemoveHandler(debugCommand);

            processChatInputHook?.Disable();
            processChatInputHook?.Dispose();

            ChatGui.ChatMessage -= ChatGui_ChatMessage;

        }

       
        public unsafe delegate byte ProcessChatInputDelegate(IntPtr uiModule, byte** a2, IntPtr a3);


       

        private unsafe byte ProcessChatInputDetour(IntPtr uiModule, byte** message, IntPtr a3)
        {
            try
            {
                PluginLog.Information($"PROCESSCHAT HAS BEEN CALLED!");

                var bc = 0;
                for (var i = 0; i <= 500; i++)
                {
                    if (*(*message + i) != 0) continue;
                    bc = i;
                    break;
                }
                if (bc < 2 || bc > 500)
                {
                    return processChatInputHook.Original(uiModule, message, a3);
                }

                var inputString = Encoding.UTF8.GetString(*message, bc);
                if (inputString.Equals("/r") && !string.IsNullOrEmpty(lastPlayer))
                {
                    string newCommand = $"/tell {lastPlayer}";
                    PluginLog.Information($"Aliasing /r with {newCommand}");
                    var bytes = Encoding.UTF8.GetBytes(newCommand);
                    var mem1 = Marshal.AllocHGlobal(400);
                    var mem2 = Marshal.AllocHGlobal(bytes.Length + 30);
                    Marshal.Copy(bytes, 0, mem2, bytes.Length);
                    Marshal.WriteByte(mem2 + bytes.Length, 0);
                    Marshal.WriteInt64(mem1, mem2.ToInt64());
                    Marshal.WriteInt64(mem1 + 8, 64);
                    Marshal.WriteInt64(mem1 + 8 + 8, bytes.Length + 1);
                    Marshal.WriteInt64(mem1 + 8 + 8 + 8, 0);
                    var r = processChatInputHook.Original(uiModule, (byte**)mem1.ToPointer(), a3);
                    Marshal.FreeHGlobal(mem1);
                    Marshal.FreeHGlobal(mem2);
                    return r;
                }

                return processChatInputHook.Original(uiModule, message, a3);

            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error with process chat!: {ex}");
                return 0;
            }

        }
    }
}
