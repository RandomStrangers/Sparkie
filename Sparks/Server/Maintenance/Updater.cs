/*
    Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/GoldenSparks)
    
    Dual-licensed under the Educational Community License, Version 2.0 and
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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using GoldenSparks.Network;
using GoldenSparks.Tasks;

namespace GoldenSparks {
    /// <summary> Checks for and applies software updates. </summary>
    public static class Updater {
        
        public static string SourceURL = "https://github.com/GoldenSparks/Sparkie/";
        public const string BaseURL    = "https://github.com/GoldenSparks/Sparkie/blob/Sparks/";
        public const string UploadsURL = "https://github.com/GoldenSparks/Sparkie/tree/Sparks/Uploads";
        public const string UpdatesURL = "https://github.com/GoldenSparks/Sparkie/raw/Sparks/Uploads/";
        public const string SQLiteURL = "https://github.com/GoldenSparks/Sparkie/raw/Sparks/Uploads/sqlite3.dll";
        public static string WikiURL = "https://github.com/UnknownShadow200/MCGalaxy";


        const string CurrentVersionURL = BaseURL + "Uploads/current_version.txt";

        const string dllURL = UpdatesURL + "GoldenSparks_.dll";
        const string guiURL = UpdatesURL + "GoldenSparks.exe";
        const string cliURL = UpdatesURL + "GoldenSparksCLI.exe";

        public static event EventHandler NewerVersionDetected;
        
        public static void UpdaterTask(SchedulerTask task) {
            UpdateCheck();
            task.Delay = TimeSpan.FromHours(2);
        }

        static void UpdateCheck() {
            if (!Server.Config.CheckForUpdates) return;
            WebClient client = HttpUtil.CreateWebClient();

            try {
                string latest = client.DownloadString(CurrentVersionURL);
                
                if (new Version(Server.Version) >= new Version(latest)) {
                    Logger.Log(LogType.SystemActivity, "No update found!");
                } else if (NewerVersionDetected != null) {
                    NewerVersionDetected(null, EventArgs.Empty);
                }
            } catch (Exception ex) {
                Logger.LogError("Error checking for updates", ex);
            }
            
            client.Dispose();
        }

        public static void PerformUpdate() {
            try {
                try {
                    DeleteFiles("Changelog.txt", "GoldenSparks_.update", "GoldenSparks.update", "GoldenSparksCLI.update",
                    "prev_GoldenSparks_.dll", "prev_GoldenSparks.exe", "prev_GoldenSparksCLI.exe");
                }
                catch {
                }
                
                WebClient client = HttpUtil.CreateWebClient();
                client.DownloadFile(dllURL, "GoldenSparks_.update");
                client.DownloadFile(guiURL, "GoldenSparks.update");
                client.DownloadFile(cliURL, "GoldenSparksCLI.update");

                Level[] levels = LevelInfo.Loaded.Items;
                foreach (Level lvl in levels) {
                    if (!lvl.SaveChanges) continue;
                    lvl.Save();
                    lvl.SaveBlockDBChanges();
                }

                Player[] players = PlayerInfo.Online.Items;
                foreach (Player pl in players) pl.SaveStats();

                // Move current files to previous files (by moving instead of copying, 
                //  can overwrite original the files without breaking the server)

                AtomicIO.TryMove("GoldenSparks_.dll", "prev_GoldenSparks_.dll");
                AtomicIO.TryMove("GoldenSparks.exe", "prev_GoldenSparks.exe");
                AtomicIO.TryMove("GoldenSparksCLI.exe", "prev_GoldenSparksCLI.exe");


                File.Move("GoldenSparks_.update", "GoldenSparks_.dll");
                File.Move("GoldenSparks.update",    "GoldenSparks.exe");
                File.Move("GoldenSparksCLI.update", "GoldenSparksCLI.exe");
                Server.Update(true, "Updating server.");
            } catch (Exception ex) {
                Logger.LogError("Error performing update", ex);
            }
        }

        static void DeleteFiles(params string[] paths) {
            foreach (string path in paths) { AtomicIO.TryDelete(path); }
        }
    }
}
