using System;
using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DeathRecap.Events;
using DeathRecap.Game;
using DeathRecap.UI;

namespace DeathRecap;

public class DeathRecapPlugin : IDalamudPlugin {
    public DeathRecapWindow Window { get; }

    public ConfigWindow ConfigWindow { get; }

    public Configuration Configuration { get; }

    public ConditionEvaluator ConditionEvaluator { get; }

    public CombatEventCapture CombatEventCapture { get; }

    public NotificationHandler NotificationHandler { get; }

    public WindowSystem WindowSystem { get; }

    public Dictionary<ulong, List<Death>> DeathsPerPlayer { get; } = new();

    private DateTime lastClean = DateTime.Now;

    public DeathRecapPlugin(IDalamudPluginInterface pluginInterface) {
        Service.Initialize(pluginInterface);

        Configuration = Configuration.Get(pluginInterface);
        Window = new DeathRecapWindow(this);
        ConfigWindow = new ConfigWindow(this);
        ConditionEvaluator = new ConditionEvaluator(this);
        CombatEventCapture = new CombatEventCapture(this);
        NotificationHandler = new NotificationHandler(this);
        WindowSystem = new WindowSystem("DeathRecap");

        WindowSystem.AddWindow(Window);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(NotificationHandler);

        pluginInterface.UiBuilder.Draw += () => WindowSystem.Draw();
        pluginInterface.UiBuilder.OpenMainUi += () => Window.Toggle();
        pluginInterface.UiBuilder.OpenConfigUi += () => ConfigWindow.Toggle();
        Service.Framework.Update += FrameworkOnUpdate;
        var commandInfo = new CommandInfo((_, _) => Window.Toggle()) { HelpMessage = "Open the death recap window" };
        Service.CommandManager.AddHandler("/deathrecap", commandInfo);
        Service.CommandManager.AddHandler("/dr", commandInfo);
        Service.CommandManager.AddHandler("/drs",
            new CommandInfo(this.Snapshot)
            {
                HelpMessage = "Snapshot last x seconds of damage"
            }
            );

#if DEBUG
        try {
            DummyData.AddDummyData(this);
        } catch (Exception e) {
            Service.PluginLog.Error(e, "Failed to add dummy data");
        }
#endif
    }

    private void FrameworkOnUpdate(IFramework framework) {
#if !DEBUG
        var now = DateTime.Now;
        if ((now - lastClean).TotalSeconds >= 10) {
            CombatEventCapture.CleanCombatEvents();
            lastClean = now;
        }
#endif
    }

    private void Snapshot(string command, string arguments)
    {
        var combatEvents = CombatEventCapture.combatEvents;
        var chatMsg = new SeString(new TextPayload("Creating snapshot"));
        Service.ChatGui.Print(new XivChatEntry { Message = chatMsg, Type = this.Configuration.ChatType });
        foreach (Dalamud.Game.ClientState.Party.IPartyMember player in Service.PartyList)
        {
            if (combatEvents.Remove(player.ObjectId, out var events))
            {
                var death = new Death { PlayerId = player.ObjectId, PlayerName = player.Name.TextValue, TimeOfDeath = DateTime.Now, Events = events };
                this.DeathsPerPlayer.AddEntry(player.ObjectId, death);
                this.NotificationHandler.DisplaySnapshot(death);
            }
        }
    }

    public void Dispose() {
        CombatEventCapture.Dispose();
        Service.Framework.Update -= FrameworkOnUpdate;
        Service.CommandManager.RemoveHandler("/deathrecap");
        Service.CommandManager.RemoveHandler("/dr");
        Service.CommandManager.RemoveHandler("/drs");
    }
}
