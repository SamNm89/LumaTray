using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LumaTray
{
    public class UpdateInfo
    {
        public string VersionTag { get; set; }
        public Version Version { get; set; }
        public string ReleaseName { get; set; }
        public string PatchNotes { get; set; }
        public string DownloadUrl { get; set; }
        public string HtmlUrl { get; set; }
    }

    public static class UpdateManager
    {
        private const string GitHubApiLatest = "https://api.github.com/repos/SamNm89/LumaTray/releases/latest";

        public static event Action<UpdateInfo> UpdateFound;
        public static UpdateInfo AvailableUpdate { get; private set; }
        public static bool HasUpdate { get { return AvailableUpdate != null; } }

        public static Version CurrentVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static void CheckForUpdatesAsync(bool isManual, Action<bool, UpdateInfo, string> onCompleted = null)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    string json;

                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "LumaTray-App");
                        json = client.DownloadString(GitHubApiLatest);
                    }

                    string tag = MatchValue(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    string name = MatchValue(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    string htmlUrl = MatchValue(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                    string downloadUrl = MatchValue(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*LumaTray\\.exe)\"");
                    string rawBody = MatchValue(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");

                    string patchNotes = UnescapeString(rawBody);
                    if (!string.IsNullOrEmpty(patchNotes))
                    {
                        SettingsManager.CachedPatchNotes = patchNotes;
                    }

                    string cleanTag = (tag ?? "").Trim().TrimStart('v', 'V');
                    Version remoteVersion;

                    if (Version.TryParse(cleanTag, out remoteVersion))
                    {
                        SettingsManager.LastCheckTime = DateTime.UtcNow;

                        if (remoteVersion > CurrentVersion)
                        {
                            UpdateInfo info = new UpdateInfo
                            {
                                VersionTag = tag,
                                Version = remoteVersion,
                                ReleaseName = string.IsNullOrEmpty(name) ? tag : name,
                                PatchNotes = patchNotes,
                                DownloadUrl = downloadUrl,
                                HtmlUrl = htmlUrl
                            };

                            AvailableUpdate = info;

                            if (UpdateFound != null)
                            {
                                UpdateFound(info);
                            }

                            if (onCompleted != null)
                            {
                                onCompleted(true, info, null);
                            }
                            return;
                        }
                    }

                    if (onCompleted != null)
                    {
                        onCompleted(false, null, null);
                    }
                }
                catch (Exception ex)
                {
                    if (onCompleted != null)
                    {
                        onCompleted(false, null, ex.Message);
                    }
                }
            });
        }

        public static void PromptAndInstallUpdate(UpdateInfo update, IWin32Window parent = null)
        {
            if (update == null) return;

            string notes = !string.IsNullOrEmpty(update.PatchNotes)
                ? update.PatchNotes
                : "Bug fixes and performance improvements.";

            // Clean up excessive markdown symbols for clean native dialog presentation
            string displayNotes = notes.Replace("##", "").Replace("**", "").Replace("`", "").Trim();
            if (displayNotes.Length > 800)
            {
                displayNotes = displayNotes.Substring(0, 797) + "...";
            }

            string promptMsg = string.Format(
                "LumaTray {0} is available! (Current: v{1})\n\n" +
                "What's New:\n{2}\n\n" +
                "Click 'Yes' to update & restart immediately.\n" +
                "Click 'No' to view on GitHub.\n" +
                "Click 'Cancel' to postpone.",
                update.VersionTag,
                CurrentVersion.ToString(3),
                displayNotes);

            DialogResult dr = MessageBox.Show(parent, promptMsg, "LumaTray Update", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

            if (dr == DialogResult.Yes)
            {
                StartDownloadAndApply(update);
            }
            else if (dr == DialogResult.No)
            {
                try
                {
                    Process.Start(!string.IsNullOrEmpty(update.HtmlUrl) ? update.HtmlUrl : "https://github.com/SamNm89/LumaTray/releases");
                }
                catch { }
            }
        }

        private static void StartDownloadAndApply(UpdateInfo update)
        {
            if (string.IsNullOrEmpty(update.DownloadUrl))
            {
                MessageBox.Show("Direct download URL was not found for this release. Opening GitHub Releases page instead.", "LumaTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start(update.HtmlUrl); } catch { }
                return;
            }

            string tempExe = Path.Combine(Path.GetTempPath(), "LumaTray_Update.exe");

            try
            {
                if (File.Exists(tempExe)) File.Delete(tempExe);
            }
            catch { }

            Form progressForm = new Form
            {
                Text = "Updating LumaTray...",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = true,
                Width = 320,
                Height = 110,
                TopMost = true
            };

            Label lblStatus = new Label
            {
                Text = "Downloading " + update.VersionTag + "...",
                Location = new System.Drawing.Point(16, 14),
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 9f)
            };

            ProgressBar pb = new ProgressBar
            {
                Location = new System.Drawing.Point(16, 36),
                Width = 272,
                Height = 18,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };

            progressForm.Controls.Add(lblStatus);
            progressForm.Controls.Add(pb);
            progressForm.Show();

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "LumaTray-App");
                        wc.DownloadFile(update.DownloadUrl, tempExe);
                    }

                    FileInfo fi = new FileInfo(tempExe);
                    if (!fi.Exists || fi.Length < 30000)
                    {
                        throw new Exception("Downloaded file is incomplete or invalid.");
                    }

                    // Prepare in-place swap command
                    string currentExe = Application.ExecutablePath;
                    string cmdArgs = string.Format(
                        "/c timeout /t 1 /nobreak > NUL & move /y \"{0}\" \"{1}\" & start \"\" \"{1}\"",
                        tempExe, currentExe);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = cmdArgs,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    Process.Start(psi);

                    // Exit current instance cleanly so cmd can overwrite it
                    progressForm.BeginInvoke(new Action(delegate
                    {
                        progressForm.Close();
                        Application.Exit();
                    }));
                }
                catch (Exception ex)
                {
                    progressForm.BeginInvoke(new Action(delegate
                    {
                        progressForm.Close();
                        MessageBox.Show("Could not download update: " + ex.Message + "\n\nOpening release page...", "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        try { Process.Start(update.HtmlUrl); } catch { }
                    }));
                }
            });
        }

        private static string MatchValue(string input, string pattern)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var match = Regex.Match(input, pattern, RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string UnescapeString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            try
            {
                return Regex.Unescape(input);
            }
            catch
            {
                return input.Replace("\\r\\n", "\r\n").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
        }
    }
}
