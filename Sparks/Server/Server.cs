/*
    Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/GoldenSparks)
    
    Dual-licensed under the    Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using GoldenSparks.Authentication;
using GoldenSparks.Commands;
using GoldenSparks.DB;
using GoldenSparks.Drawing;
using GoldenSparks.Eco;
using GoldenSparks.Events.ServerEvents;
using GoldenSparks.Games;
using GoldenSparks.Network;
using GoldenSparks.Scripting;
using GoldenSparks.Tasks;
using GoldenSparks.Util;
using GoldenSparks.Modules.Awards;
using System.Reflection;

namespace GoldenSparks {
    public sealed partial class Server {
        
        public Server() { s = this; }

        //True = cancel event
        //Fale = dont cacnel event
        public static bool Check(string cmd, string message)
        {
            if (SparksCommand != null) SparksCommand(cmd, message);
            return cancelcommand;
        }

        
        [Obsolete("Use Logger.Log(LogType, String)")]
        public void Log(string message) { Logger.Log(LogType.SystemActivity, message); }
        
        [Obsolete("Use Logger.Log(LogType, String)")]
        public void Log(string message, bool systemMsg = false) {
            LogType type = systemMsg ? LogType.BackgroundActivity : LogType.SystemActivity;
            Logger.Log(type, message);
        }
        
        static void CheckFile(string file) {
            if (File.Exists(file)) return;
            
            Logger.Log(LogType.SystemActivity, file + " doesn't exist, Downloading..");
            try {
                using (WebClient client = HttpUtil.CreateWebClient()) {
                    client.DownloadFile(Updater.BaseURL + file + "?raw=true", file);
                }
                if (File.Exists(file)) {
                    Logger.Log(LogType.SystemActivity, file + " download succesful!");
                }
            } catch (Exception ex) {
                Logger.LogError("Downloading " + file +" failed, try again later", ex);
            }
        }
        static void CheckSQLiteFile()
        {
            string file = "sqlite3.dll";
            if (File.Exists(file)) return;

            Logger.Log(LogType.SystemActivity,"SQLite dll doesn't exist, Downloading..");
            try
            {
                using (WebClient client = HttpUtil.CreateWebClient())
                {
                    client.DownloadFile(Updater.SQLiteURL, file);
                }
                if (File.Exists(file))
                {
                    Logger.Log(LogType.SystemActivity, "SQLite dll download succesful!");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Downloading " + file + " failed, try again later", ex);
            }
        }
        public static ConfigElement[] serverConfig, levelConfig, zoneConfig;
        public static void Start() {
            serverConfig = ConfigElement.GetAll(typeof(ServerConfig));
            levelConfig  = ConfigElement.GetAll(typeof(LevelConfig));
            zoneConfig   = ConfigElement.GetAll(typeof(ZoneConfig));
            
#pragma warning disable 0618
            Player.players = PlayerInfo.Online.list;
            Server.levels = LevelInfo.Loaded.list;
#pragma warning restore 0618
            
            StartTime = DateTime.UtcNow;
            shuttingDown = false;
            Logger.Log(LogType.SystemActivity, "Starting Server");
            ServicePointManager.Expect100Continue = false;
            ForceEnableTLS();
            
            CheckFile("MySql.Data.dll");
            CheckSQLiteFile();
            CheckFile("sqlite3.dll");

            EnsureFilesExist();
            MoveOutdatedFiles();

            LoadAllSettings();
            InitDatabase();
            Economy.LoadDatabase();

            Background.QueueOnce(LoadMainLevel);
            Background.QueueOnce(LoadAllPlugins);
            Background.QueueOnce(LoadAutoloadMaps);
            Background.QueueOnce(UpgradeTasks.UpgradeOldTempranks);
            Background.QueueOnce(UpgradeTasks.UpgradeDBTimeSpent);
            Background.QueueOnce(InitPlayerLists);
            
            Background.QueueOnce(SetupSocket);
            Background.QueueOnce(InitTimers);
            Background.QueueOnce(InitRest);
            Background.QueueOnce(InitHeartbeat);

            ServerTasks.QueueTasks();
            Background.QueueRepeat(ThreadSafeCache.DBCache.CleanupTask,
                                   null, TimeSpan.FromMinutes(5));
        }
        
        static void ForceEnableTLS() {
            // Force enable TLS 1.1/1.2, otherwise checking for updates on Github doesn't work
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)0x300; } catch { }
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)0xC00; } catch { }
        }
        static void EnsureFilesExist() {
            EnsureDirectoryExists("properties");
            EnsureDirectoryExists("levels");
            EnsureDirectoryExists("bots");
            EnsureDirectoryExists("text");
            EnsureDirectoryExists("ranks");
            RankInfo.EnsureExists();
            Ban.EnsureExists();
            PlayerDB.EnsureDirectoriesExist();

            EnsureDirectoryExists("extra");
            EnsureDirectoryExists(Paths.WaypointsDir);
            EnsureDirectoryExists("extra/bots");
            EnsureDirectoryExists(Paths.ImportsDir);
            EnsureDirectoryExists("blockdefs");
            EnsureDirectoryExists(IScripting.DllDir);
            EnsureDirectoryExists(ICompiler.SourceDir);
        }
        
        public static void EnsureDirectoryExists(string dir) {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        
        static void MoveOutdatedFiles() {
            try {
                if (File.Exists("blocks.json")) File.Move("blocks.json", "blockdefs/global.json");
            }
            catch { }
        }        
        
        public static void LoadAllSettings() {
            // Unload custom plugins
            List<Plugin> plugins = new List<Plugin>(Plugin.all);
            foreach (Plugin p in plugins) {
                if (Plugin.core.Contains(p)) continue;
                Plugin.Unload(p, false);
            }
            
            ZSGame.Instance.infectMessages = ZSConfig.LoadInfectMessages();
            Colors.Load();
            Alias.Load();
            BlockDefinition.LoadGlobal();
            ImagePalette.Load();
            
            SrvProperties.Load();
            AuthService.ReloadDefault();
            Group.LoadAll();
            CommandPerms.Load();
            Command.InitAll();
            Block.SetBlocks();
            AwardsList.Load();
            PlayerAwards.Load();
            Economy.Load();
            WarpList.Global.Filename = "extra/warps.save";
            WarpList.Global.Load();
            CommandExtraPerms.Load();
            ProfanityFilter.Init();
            Team.LoadList();
            ChatTokens.LoadCustom();
            SrvProperties.FixupOldPerms();
            CpeExtension.LoadDisabledList();
            
            TextFile announcementsFile = TextFile.Files["Announcements"];
            announcementsFile.EnsureExists();
            announcements = announcementsFile.GetText();
            
            // Reload custom plugins
            foreach (Plugin p in plugins) {
                if (Plugin.core.Contains(p)) continue;
                Plugin.Load(p, false);
            }
            // Unload custom simple plugins
            List<Plugin_Simple> plugins2 = new List<Plugin_Simple>(Plugin_Simple.all);
            foreach (Plugin_Simple p2 in plugins2)
            {
                if (Plugin_Simple.core.Contains(p2)) continue;
                Plugin_Simple.Unload(p2, false);
            }
            OnConfigUpdatedEvent.Call();
        }
        
        static readonly object stopLock = new object();
        static volatile Thread stopThread;
        public static Thread Stop(bool restart, string msg) {
            shuttingDown = true;
            lock (stopLock) {
                if (stopThread != null) return stopThread;
                stopThread = new Thread(() => ShutdownThread(restart, msg));
                stopThread.Start();
                return stopThread;
            }
        }
        public static Thread Update(bool restart, string msg)
        {
            shuttingDown = true;
            lock (stopLock)
            {
                if (stopThread != null) return stopThread;
                stopThread = new Thread(() => UpdateThread(restart, msg));
                stopThread.Start();
                return stopThread;
            }
        }

        static void UpdateThread(bool restarting, string msg)
        {
            try
            {
                Logger.Log(LogType.SystemActivity, "Server Updating ({0})", msg);
            }
            catch { }

            // Stop accepting new connections and disconnect existing sessions
            try
            {
                if (Listener != null) Listener.Close();
            }
            catch (Exception ex) { Logger.LogError(ex); }

            try
            {
                Player[] players = PlayerInfo.Online.Items;
                foreach (Player p in players) { p.Leave("Updating Server..."); }
            }
            catch (Exception ex) { Logger.LogError(ex); }

            byte[] kick = Packet.Kick(msg, false);
            try
            {
                INetSocket[] pending = INetSocket.pending.Items;
                foreach (INetSocket p in pending) { p.Send(kick, SendFlags.None); }
            }
            catch (Exception ex) { Logger.LogError(ex); }

            OnShuttingDownEvent.Call(restarting, msg);
            Plugin.UnloadAll();

            try
            {
                string autoload = null;
                Level[] loaded = LevelInfo.Loaded.Items;
                foreach (Level lvl in loaded)
                {
                    if (!lvl.SaveChanges) continue;

                    autoload = autoload + lvl.name + "=" + lvl.physics + Environment.NewLine;
                    lvl.Save();
                    lvl.SaveBlockDBChanges();
                }

                if (SetupFinished && !Config.AutoLoadMaps)
                {
                    File.WriteAllText("text/autoload.txt", autoload);
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }

            try
            {
                Logger.Log(LogType.SystemActivity, "Server shutdown completed");
            }
            catch { }

            try { FileLogger.Flush(null); } catch { }

            if (restarting)
            {
                // first try to use excevp to restart in CLI mode under mono 
                // - see detailed comment in HACK_Execvp for why this is required
                if (HACK_TryExecvp()) HACK_Execvp();
                Process.Start(RestartPath);
            }
            Environment.Exit(0);
        }







        static void ShutdownThread(bool restarting, string msg) {
            if (Config.SayBye)
            {
                Command.Find("say").Use(Player.Sparks, Colors.Strip(SoftwareNameVersioned) + " &Sshutting down!");
            }
            try {

                Logger.Log(LogType.SystemActivity, "Server shutting down ({0})", msg);
            } catch { }
            
            // Stop accepting new connections and disconnect existing sessions
            try {
                if (Listener != null) Listener.Close();
            } catch (Exception ex) { Logger.LogError(ex); }
            
            try {
                Player[] players = PlayerInfo.Online.Items;
                foreach (Player p in players) { p.Leave(msg); }
            } catch (Exception ex) { Logger.LogError(ex); }
            
            byte[] kick = Packet.Kick(msg, false);
            try {
                INetSocket[] pending = INetSocket.pending.Items;
                foreach (INetSocket p in pending) { p.Send(kick, SendFlags.None); }
            } catch (Exception ex) { Logger.LogError(ex); }

            OnShuttingDownEvent.Call(restarting, msg);
            Plugin.UnloadAll();

            try {
                string autoload = null;
                Level[] loaded = LevelInfo.Loaded.Items;
                foreach (Level lvl in loaded) {
                    if (!lvl.SaveChanges) continue;
                    
                    autoload = autoload + lvl.name + "=" + lvl.physics + Environment.NewLine;
                    lvl.Save();
                    lvl.SaveBlockDBChanges();
                }
                
                if (SetupFinished && !Config.AutoLoadMaps) {
                    File.WriteAllText("text/autoload.txt", autoload);
                }
            } catch (Exception ex) { Logger.LogError(ex); }
            
            try {
                Logger.Log(LogType.SystemActivity, "Server shutdown completed");
            } catch { }
            
            try { FileLogger.Flush(null); } catch { }
            
            if (restarting) {
                // first try to use excevp to restart in CLI mode under mono 
                // - see detailed comment in HACK_Execvp for why this is required
                if (HACK_TryExecvp()) HACK_Execvp();
                Process.Start(RestartPath);
            }
            Environment.Exit(0);
        }
        
        [DllImport("libc", SetLastError = true)]
        static extern int execvp(string path, string[] argv);
        
        static bool HACK_TryExecvp() {
            return CLIMode && Environment.OSVersion.Platform == PlatformID.Unix 
                && RunningOnMono();
        }
        public static string GetServerDLLPath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }
        static void HACK_Execvp() {
            // With using normal Process.Start with mono, after Environment.Exit
            //  is called, all FDs (including standard input) are also closed.
            // Unfortunately, this causes the new server process to constantly error with
            //   Type: IOException
            //   Message: Invalid handle to path "server_folder_path/[Unknown]"
            //   Trace:   at System.IO.FileStream.ReadData (System.Runtime.InteropServices.SafeHandle safeHandle, System.Byte[] buf, System.Int32 offset, System.Int32 count) [0x0002d]
            //     at System.IO.FileStream.ReadInternal (System.Byte[] dest, System.Int32 offset, System.Int32 count) [0x00026]
            //     at System.IO.FileStream.Read (System.Byte[] array, System.Int32 offset, System.Int32 count) [0x000a1] 
            //     at System.IO.StreamReader.ReadBuffer () [0x000b3]
            //     at System.IO.StreamReader.Read () [0x00028]
            //     at System.TermInfoDriver.GetCursorPosition () [0x0000d]
            //     at System.TermInfoDriver.ReadUntilConditionInternal (System.Boolean haltOnNewLine) [0x0000e]
            //     at System.TermInfoDriver.ReadLine () [0x00000]
            //     at System.ConsoleDriver.ReadLine () [0x00000]
            //     at System.Sparks.ReadLine () [0x00013]
            //     at GoldenSparks.Cli.CLI.ConsoleLoop () [0x00002]
            // (this errors multiple times a second and can quickly fill up tons of disk space)
            // And also causes console to be spammed with '1R3;1R3;1R3;' or '363;1R;363;1R;'
            //
            // Note this issue does NOT happen with GUI mode for some reason - and also
            // don't want to use excevp in GUI mode, otherwise the X socket FDs pile up
            try {
                execvp("mono", new string[] { "mono", RestartPath });
            } catch {
            }
        }
        
        static bool checkedOnMono, runningOnMono;
        public static bool RunningOnMono() {
            if (!checkedOnMono) {
                runningOnMono = Type.GetType("Mono.Runtime") != null;
                checkedOnMono = true;
            }
            return runningOnMono;
        }

        public static void UpdateUrl(string url) {
            if (OnURLChange != null) OnURLChange(url);
        }
        static void RandomMessage(SchedulerTask task) {
            if (PlayerInfo.Online.Count > 0 && announcements.Length > 0) {
                Chat.MessageGlobal(announcements[new Random().Next(0, announcements.Length)]);
            }
        }

        public static void SettingsUpdate() {
            if (OnSettingsUpdate != null) OnSettingsUpdate();
        }
        
        public static bool SetMainLevel(string map) {
            string main = mainLevel != null ? mainLevel.name : Config.MainLevel;
            if (map.CaselessEq(main)) return false;
            
            Level lvl = LevelInfo.FindExact(map);
            if (lvl == null)
                lvl = LevelActions.Load(Player.Sparks, map, false);

            if (lvl == null) return false;
            
            SetMainLevel(lvl); return true;
        }
        
        public static void SetMainLevel(Level lvl) {
            Level oldMain = mainLevel;
            mainLevel = lvl;
            Config.MainLevel = lvl.name;         
            oldMain.AutoUnload();
        }
        
        public static void DoGC() {
            long start = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            long end = GC.GetTotalMemory(false);
            double deltaKB = (start - end) / 1024.0;
            if (deltaKB >= 100.0) {
                string track = (end / 1024.0).ToString("F2");
                string delta = deltaKB.ToString("F2");
                Logger.Log(LogType.BackgroundActivity, "GC performed (tracking {0} KB, freed {1} KB)", track, delta);
            }
        }
        
        
        // only want ASCII alphanumerical characters for salt
        static bool AcceptableSaltChar(char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') 
                || (c >= '0' && c <= '9');
        }
        
        /// <summary> Generates a random salt that is used for calculating mppasses. </summary>
        public static string GenerateSalt() {
            RandomNumberGenerator rng = RandomNumberGenerator.Create();
            char[] str = new char[32];
            byte[] one = new byte[1];
            
            for (int i = 0; i < str.Length; ) {
                rng.GetBytes(one);
                if (!AcceptableSaltChar((char)one[0])) continue;
                
                str[i] = (char)one[0]; i++;
            }
            return new string(str);
        }
        
        static System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
        static MD5CryptoServiceProvider md5  = new MD5CryptoServiceProvider();
        static object md5Lock = new object();
        
        /// <summary> Calculates mppass (verification token) for the given username. </summary>
        public static string CalcMppass(string name, string salt) {
            byte[] hash = null;
            lock (md5Lock) hash = md5.ComputeHash(enc.GetBytes(salt + name));
            return BitConverter.ToString(hash).Replace("-", "");
        }
        
        /// <summary> Converts a formatted username into its original username </summary>
        /// <remarks> If ClassiCubeAccountPlus option is set, removes trailing + </remarks>
        public static string ToRawUsername(string name) {
            if (Config.ClassicubeAccountPlus)
                return name.RemoveLastPlus();
            return name;
        }

        /// <summary> Converts a username into its formatted username </summary>
        /// <remarks> If ClassiCubeAccountPlus option is set, adds trailing + </remarks>
        public static string FromRawUsername(string name) {
            if (!Config.ClassicubeAccountPlus) return name;

            if (!name.EndsWith("+")) name += "+";
            return name;
        }
    }
}
