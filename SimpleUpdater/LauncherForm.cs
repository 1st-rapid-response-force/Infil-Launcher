using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace SimpleUpdater
{
    public partial class LauncherForm : Form
    {
        bool RequiresUpdate = false;

        public LauncherForm()
        {
            InitializeComponent();
            SetChangelog();

            CheckForUpdate();

            propertyGrid1.SelectedObject = Properties.Settings.Default;
        }





        private void SetChangelog()
        {
            changelogBrowser.Navigate(SimpleUpdater.Properties.Settings.Default.ChangelogURL);
            changelogBrowser.Refresh(WebBrowserRefreshOption.Completely);
        }



        #region Check For Update
        private void CheckForUpdate()
        {
            updatePlayButton.Enabled = false;
            StatusLabel.Text = "Checking For Update...";

            backgroundWorker1.DoWork += CheckForUpdate_Work;
            backgroundWorker1.RunWorkerCompleted += CheckForUpdate_WorkerComplete;
            backgroundWorker1.RunWorkerAsync();



        }

        void CheckForUpdate_WorkerComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show(e.Error.ToString());
            }

            bool NeedsUpdate = (bool)e.Result;

            if (NeedsUpdate)
            {
                StatusLabel.Text = "Update Required";
                updatePlayButton.Text = "Update";
                updatePlayButton.Enabled = true;
                RequiresUpdate = true;
            }
            else
            {
                StatusLabel.Text = "Game up to date.  Press Play to start";
                updatePlayButton.Text = "Play";
                updatePlayButton.Enabled = true;
                RequiresUpdate = false;
            }
        }

        const string LocalManifestFilename = "LastUpdateManifest.txt";
        private void CheckForUpdate_Work(object sender, DoWorkEventArgs e)
        {

            e.Result = false;

            WebClient webClient = new WebClient();

            string gameDirectory = Properties.Settings.Default.ARMA_ModpackLocation;
            string localManifest = gameDirectory + Path.DirectorySeparatorChar + LocalManifestFilename;

            if (!File.Exists(localManifest))
            {
                //We don't have the game downloaded.  Need to update.

                e.Result = true;
            }
            else
            {
                string ManifestURL = Properties.Settings.Default.ManifestURL;

                string manifest = webClient.DownloadString(ManifestURL);

                LauncherManifest RemoteManifest = JsonConvert.DeserializeObject<LauncherManifest>(manifest);



                LauncherManifest LocalManifest = JsonConvert.DeserializeObject<LauncherManifest>(File.ReadAllText(localManifest));

                if (RemoteManifest.Build > LocalManifest.Build)
                {
                    e.Result = true; ;
                }
            }

        }
        #endregion

        #region Update Game

        private void UpdateGame()
        {
            updatePlayButton.Text = "Updating...";
            updatePlayButton.Enabled = false;

            Directory.CreateDirectory(Properties.Settings.Default.ARMA_ModpackLocation);

            backgroundWorker1.WorkerReportsProgress = true;
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.DoWork += UpdateGame_Worker;
            backgroundWorker1.ProgressChanged += UpdateGame_ProgressChanged;
            backgroundWorker1.RunWorkerCompleted += UpdateGame_RunWorkerCompleted;
            backgroundWorker1.RunWorkerAsync();

        }

        void UpdateGame_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                StatusLabel.Text = "Error when downloading. Try again";
                updatePlayButton.Enabled = true;
                return;
            }

            //Deal with User Config files
            var armaUserConfig = Properties.Settings.Default.ARMA_Executable + "\\userconfig";
            var modpackUserConfig = Properties.Settings.Default.ARMA_ModpackLocation + "\\userconfig";

            Process proc = new Process();
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.FileName = @"C:\WINDOWS\system32\xcopy.exe";
            proc.StartInfo.Arguments = @"C:\source C:\destination /E /I";
            proc.StartInfo.Arguments = modpackUserConfig + " "+armaUserConfig + " /E /I";
            proc.Start();

       
            StatusLabel.Text = "Ready to play";
            RequiresUpdate = false;
            updatePlayButton.Text = "Play";
            updatePlayButton.Enabled = true;

        }

        void UpdateGame_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            StatusLabel.Text = e.UserState as string;
            progressBar1.Value = e.ProgressPercentage;
        }

        void UpdateGame_Worker(object sender, DoWorkEventArgs e)
        {
            backgroundWorker1.ReportProgress(0, "Downloading Manifest...");


            string ManifestURL = Properties.Settings.Default.ManifestURL;
            WebClient webClient = new WebClient();
            webClient.DownloadProgressChanged += webClient_DownloadProgressChanged;
            webClient.DownloadFileCompleted += webClient_DownloadFileCompleted;

            string manifest = webClient.DownloadString(ManifestURL);
            LauncherManifest RemoteManifest = JsonConvert.DeserializeObject<LauncherManifest>(manifest);


            string gameInstallDir = Properties.Settings.Default.ARMA_ModpackLocation;

            var md5 = MD5.Create();
            int totalFiles = RemoteManifest.Files.Count;

            int curFile = 0;
            foreach (KeyValuePair<string, string> kv in RemoteManifest.Files)
            {

                bool ShouldDownload = false;
                string gameFilePath = gameInstallDir + kv.Key.Replace("/", Path.DirectorySeparatorChar.ToString());
                if (File.Exists(gameFilePath))
                {
                    int progress = (int)(((float)curFile / (float)totalFiles) * 100);
                    backgroundWorker1.ReportProgress(progress, "(" + (curFile) + " / " + totalFiles + ") Checking " + kv.Key);
                    //Check it's md5 hash
                    using (var stream = File.OpenRead(gameFilePath))
                    {
                        var hash = Util.ComputeMD5(gameFilePath);

                        if (hash != kv.Value)
                        {
                            ShouldDownload = true;

                        }

                    }
                }
                else
                {
                    ShouldDownload = true;
                }


                if (ShouldDownload)
                {
                    DownloadFile(curFile, totalFiles, kv, RemoteManifest, gameFilePath, webClient);

                    var hash = Util.ComputeMD5(gameFilePath);

                    if (hash != kv.Value)
                    {
                        MessageBox.Show("Failed Validating " + kv.Key);
                        DownloadFile(curFile, totalFiles, kv, RemoteManifest, gameFilePath, webClient);
                    }



                }
                if (backgroundWorker1.CancellationPending)
                {

                    return;
                }
                curFile++;
            }

            backgroundWorker1.ReportProgress(100, "Writing Local Manifest");

            string gameDirectory = Properties.Settings.Default.ARMA_ModpackLocation;
            string localManifest = gameDirectory + Path.DirectorySeparatorChar + LocalManifestFilename;

            File.WriteAllText(localManifest, manifest);

        }

        void webClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show(e.Error.ToString());
                backgroundWorker1.CancelAsync();
            }
        }


        private void DownloadFile(int curFile, int totalFiles, KeyValuePair<string, string> kv, LauncherManifest RemoteManifest, string gameFilePath, WebClient webClient)
        {
            int progress = (int)(((float)curFile / (float)totalFiles) * 100);

            string status = "(" + (curFile) + " / " + totalFiles + ") Downloading: " + kv.Key;

            backgroundWorker1.ReportProgress(progress, status);

            string remoteFile = (Properties.Settings.Default.ContentURL + RemoteManifest.ProjectRoot + kv.Key.Substring(1));

            Directory.CreateDirectory(Path.GetDirectoryName(gameFilePath));
            if (File.Exists(gameFilePath))
            {
                File.Delete(gameFilePath);
            }

            webClient.DownloadFileAsync(new Uri(remoteFile), gameFilePath, status);

            while (webClient.IsBusy)
            {
                Thread.Sleep(1);
            }
        }

        private void DownloadFile(WebClient cl, string file)
        {

        }

        void webClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {


            string status = e.UserState as string + " ( " + Size(e.BytesReceived) + " / " + Size(e.TotalBytesToReceive) + " )";
            backgroundWorker1.ReportProgress(e.ProgressPercentage, status);
        }

        private string Size(long bytes)
        {
            if (bytes > 1000000000)
            {
                return ((float)bytes / 1000000000f).ToString("f") + " GB";
            }

            if (bytes > 1000000)
            {
                return ((float)bytes / 1000000f).ToString("f") + " MB";
            }
            if (bytes > 1000)
            {
                return ((float)bytes / 1000f).ToString("f") + " KB";
            }
            return ((float)bytes).ToString("f") + " B";
        }



        #endregion

        private void buildManifestButton_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog folderBrowser = new FolderBrowserDialog();
            folderBrowser.SelectedPath = Properties.Settings.Default.Builder_LastDirectory;
            if (folderBrowser.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string path = folderBrowser.SelectedPath;

                int buildNumber = Properties.Settings.Default.Builder_LastBuildNumber;

                ManifestBuilder.BuildManifest(path, buildNumber, Properties.Settings.Default.Builder_LastURL);
            }
        }

        private void updatePlayButton_Click(object sender, EventArgs e)
        {
            if (RequiresUpdate)
            {
                if (Properties.Settings.Default.ARMA_ModpackLocation == null || Properties.Settings.Default.ARMA_ModpackLocation == "")
                {
                    MessageBox.Show("Please set your preferred modpack download location in the options tab");
                } else
                {
                    UpdateGame();
                }
            }
            else
            {
                if (Properties.Settings.Default.ARMA_Executable == null || Properties.Settings.Default.ARMA_Executable == "")
                {
                    MessageBox.Show("Please set your preferred modpack download location in the options tab");
                }
                else
                {
                    LaunchGame();
                }
        
            }
        }

        private void LaunchGame()
        {
            string gameInstallDir = Properties.Settings.Default.ARMA_ModpackLocation;
            var commandLine = "-noLauncher -useBE";
            var modpackLocation = Properties.Settings.Default.ARMA_ModpackLocation;
            var customCommandLine = Properties.Settings.Default.ARMA_CustomCommandLine;
            var modLine = " -mod=" +
                modpackLocation + "\\@1rrf_content;" +
                modpackLocation + "\\@1rrf_maps;" +
                modpackLocation + "\\@1rrf_utility;" +
                modpackLocation + "\\@ace;" +
                modpackLocation + "\\@ares;" +
                modpackLocation + "\\@cba_a3;" +
                modpackLocation + "\\@rhs_afrf;" +
                modpackLocation + "\\@rhs_usaf;" +
                modpackLocation + "\\@task_force_radio;";
            commandLine = commandLine + modLine + customCommandLine;

            //Save Last Command Line Run for Debug
            Properties.Settings.Default.ARMA_CustomCommandLine = commandLine;
            Properties.Settings.Default.Save();

            Process.Start(Properties.Settings.Default.ARMA_Executable, commandLine);
           
                
        }

        void process_Exited(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
        }

        private void propertyGrid1_SelectedObjectsChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.Save();
        }

        private void validateButton_Click(object sender, EventArgs e)
        {
            UpdateGame();
        }

        private void setARMA_Click(object sender, EventArgs e)
        {
            // Create an instance of the open file dialog box.
            OpenFileDialog armaExecutable = new OpenFileDialog();

            // Set filter options and filter index.
            armaExecutable.Filter = "ARMA 3 Launcher (arma3launcher.exe)|arma3launcher.exe";
            armaExecutable.FilterIndex = 1;

            // Process input if the user clicked OK.
            if (armaExecutable.ShowDialog() == DialogResult.OK)
            {
                Properties.Settings.Default.ARMA_Executable = armaExecutable.FileName;
                Properties.Settings.Default.Save();
                propertyGrid1.SelectedObject = Properties.Settings.Default;
            }
        }

        private void setModpackFolder_Click(object sender, EventArgs e)
        {
            // Create an instance of the open file dialog box.
            FolderBrowserDialog modpackLocation = new FolderBrowserDialog();

            // Process input if the user clicked OK.
            if (modpackLocation.ShowDialog() == DialogResult.OK)
            {
                Properties.Settings.Default.ARMA_ModpackLocation = modpackLocation.SelectedPath;
                Properties.Settings.Default.Save();
                propertyGrid1.SelectedObject = Properties.Settings.Default;
            }
        }

        private void setTeamspeakPluginFolder_Click(object sender, EventArgs e)
        {
            // Create an instance of the open file dialog box.
            FolderBrowserDialog teamspeakLocation = new FolderBrowserDialog();

            // Process input if the user clicked OK.
            if (teamspeakLocation.ShowDialog() == DialogResult.OK)
            {
                Properties.Settings.Default.TeamspeakPluginFolder = teamspeakLocation.SelectedPath;
                Properties.Settings.Default.Save();
                propertyGrid1.SelectedObject = Properties.Settings.Default;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.TeamspeakPluginFolder == null | Properties.Settings.Default.TeamspeakPluginFolder == "")
            {
                MessageBox.Show("You need to the Teamspeak Plugin Folder before we can copy for the plugin files.");
            } else if(RequiresUpdate == true){
                MessageBox.Show("You need to update/verify your modpack before you can install the teamspeak plugin files.");
            } else {
                var teamspeakLocation = Properties.Settings.Default.TeamspeakPluginFolder;
                var pluginsFolder = Properties.Settings.Default.ARMA_ModpackLocation+ "\\plugin_files\\teamspeak\\plugins";

                //Now Create all of the directories
                foreach (string dirPath in Directory.GetDirectories(pluginsFolder, "*",
                    SearchOption.AllDirectories))
                    Directory.CreateDirectory(dirPath.Replace(pluginsFolder, teamspeakLocation));

                //Copy all the files & Replaces any files with the same name
                foreach (string newPath in Directory.GetFiles(pluginsFolder, "*.*",
                    SearchOption.AllDirectories))
                {
                    File.SetAttributes(newPath, FileAttributes.Normal);
                    File.Copy(newPath, newPath.Replace(pluginsFolder, teamspeakLocation), true);
                }
                    

            }
              
        }
    }
}
