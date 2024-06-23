using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using Terraria.GameContent.Creative;
using System.Threading.Channels;

namespace OnusUtility
{
    [ApiVersion(2, 1)]
    public class OnusUtility : TerrariaPlugin
    {
        public OnusUtility(Main game) : base(game) { }
        public override string Author => "Onusai";
        public override string Description => "A pack of utilities";
        public override string Name => "OnusUtility";
        public override Version Version => new Version(1, 0, 0, 1);


        public bool active = true;

        public Thread delayThread;
        public List<string> delayedMsgs = new List<string>();

        public Stopwatch stopWatch = new Stopwatch();
        public bool watchStarted;
        public Dictionary<string, TimeSpan> playerTimeCounter = new Dictionary<string, TimeSpan>();

        public bool deathcore;
        public Dictionary<string, int> deathCount = new Dictionary<string, int>();


        public class ConfigData
        { }
        ConfigData configData;


        public override void Initialize()
        {
            // configData = PluginConfig.Load("OnusUtility");


            ServerApi.Hooks.GameInitialize.Register(this, OnGameLoad);

            RegisterCommand("clock", "", true, ShowClock,
                "Shows you in-game time and session uptime.");

            RegisterCommand("deathcore", "tshock.admin", true, ToggleDeathcore,
                "Players only have 1 hp. Any damage is instant death.");

            RegisterCommand("deaths", "", true, ShowDeaths,
                "Shows you your death count.");

            RegisterCommand("tsandstorm", "tshock.admin", true, ToggleSandstorm,
                "Toggles sandstorm.");

        }


        void OnGameLoad(EventArgs e)
        {
            ServerApi.Hooks.NetGreetPlayer.Register(this, PlayerJoin);
            TShockAPI.GetDataHandlers.KillMe += OnPlayerDeath;
            TShockAPI.GetDataHandlers.PlayerDamage += OnDamage;

            delayThread = new Thread(new ThreadStart(DelayMsg));
            delayThread.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                active = false;
                delayThread.Join();

                ServerApi.Hooks.GameInitialize.Deregister(this, OnGameLoad);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, PlayerJoin);
                TShockAPI.GetDataHandlers.KillMe -= OnPlayerDeath;
                TShockAPI.GetDataHandlers.PlayerDamage -= OnDamage;
            }
            base.Dispose(disposing);
        }

        void RegisterCommand(string name, string perm, bool enabled, CommandDelegate handler, string helptext)
        {
            TShockAPI.Commands.ChatCommands.Add(new Command(perm, handler, name) { HelpText = helptext });
        }

        void PlayerJoin(GreetPlayerEventArgs e)
        {
            if (!watchStarted)
            {
                stopWatch.Start();
                watchStarted = true;
            }

            var player = TShock.Players[e.Who];
            if (player == null) return;

            if (player.Difficulty == 2 && !playerTimeCounter.ContainsKey(player.UUID)) playerTimeCounter[player.UUID] = stopWatch.Elapsed;

            if (!deathCount.ContainsKey(TShock.Players[e.Who].UUID)) deathCount.Add(TShock.Players[e.Who].UUID, 0);
        }

        void OnPlayerDeath(object sender, TShockAPI.GetDataHandlers.KillMeEventArgs args)
        {
            var player = TShock.Players[args.PlayerId];

            if (player.Difficulty == 2)
            {
                TimeSpan ts = stopWatch.Elapsed - playerTimeCounter[player.UUID];
                string tsHour = ts.Hours > 0 ? String.Format("{0:0}h ", ts.Hours) : "";
                delayedMsgs.Add(String.Format("{0} lasted {1}{2:0}m", player.Name, tsHour, ts.Minutes));
                playerTimeCounter.Remove(player.UUID);
            }

            deathCount[player.UUID] += 1;

            if (deathcore) delayedMsgs.Add(String.Format("{0}'s death count: {1}", player.Name, deathCount[player.UUID]));
        }

        void DelayMsg()
        {
            while (active)
            {
                Thread.Sleep(1000);

                if (delayedMsgs.Count > 0)
                {
                    foreach (string i in delayedMsgs)
                    {
                        TSPlayer.All.SendMessage(i, Color.Red);
                    }
                    delayedMsgs.Clear();
                }
            }
        }

        void ShowClock(CommandArgs args)
        {
            double time = Main.time / 3600.0;
            time += 4.5;
            if (!Main.dayTime)
                time += 15.0;
            time = time % 24.0;

            int hour = (int)Math.Floor(time);
            string flag = hour > 11 ? "pm" : "am";
            hour = hour > 12 ? hour - 12 : hour;
            args.Player.SendInfoMessage("In-game time {0}:{1:D2} {2}", hour, (int)Math.Floor((time % 1.0) * 60.0), flag);

            TimeSpan ts = stopWatch.Elapsed;
            string tsHour = ts.Hours > 0 ? String.Format("{0:0}h ", ts.Hours) : "";
            args.Player.SendInfoMessage("Session began {0}{1:0}m ago", tsHour, ts.Minutes);
        }


        void ToggleDeathcore(CommandArgs args)
        {
            deathcore = !deathcore;
            args.Player.SendInfoMessage("Deathcore has been {0}", deathcore ? "activated." : "deactivated.");
        }

        void OnDamage(object sender, TShockAPI.GetDataHandlers.PlayerDamageEventArgs args)
        {
            if (deathcore) TShock.Players[args.ID].KillPlayer();
        }

        void ShowDeaths(CommandArgs args)
        {
            if (deathCount.ContainsKey(args.Player.UUID)) args.Player.SendInfoMessage("Your death count is: {0}", deathCount[args.Player.UUID]);
        }

        void ToggleSandstorm(CommandArgs args)
        {
            if (Terraria.GameContent.Events.Sandstorm.Happening)
            {
                Terraria.GameContent.Events.Sandstorm.StopSandstorm();
                //Main.windSpeedCurrent = 20;
                Main.windSpeedTarget = 20;
                args.Player.SendMessage("Sandstorm: stopped", Color.DarkSlateGray);
            }
            else
            {
                Terraria.GameContent.Events.Sandstorm.StartSandstorm();
                //Main.windSpeedCurrent = 35;
                Main.windSpeedTarget = 35;
                TSPlayer.All.SendData(PacketTypes.WorldInfo);
                args.Player.SendMessage("Sandstorm: started", Color.DarkSlateGray);
            }
        }

        public static class PluginConfig
        {
            public static string filePath;
            public static ConfigData Load(string Name)
            {
                filePath = String.Format("{0}/{1}.json", TShock.SavePath, Name);

                if (!File.Exists(filePath))
                {
                    var data = new ConfigData();
                    Save(data);
                    return data;
                }

                var jsonString = File.ReadAllText(filePath);
                var myObject = JsonSerializer.Deserialize<ConfigData>(jsonString);

                return myObject;
            }

            public static void Save(ConfigData myObject)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var jsonString = JsonSerializer.Serialize(myObject, options);

                File.WriteAllText(filePath, jsonString);
            }
        }


    }
}