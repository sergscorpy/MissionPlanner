﻿using ICSharpCode.SharpZipLib.Checksum;
using log4net;
using MissionPlanner.Controls;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Org.BouncyCastle.Crypto.Digests;
using System.Reflection;

namespace MissionPlanner.Utilities
{
    class Update
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        static bool MONO = false;
        public static bool dobeta = false;
        public static bool domaster = false;

        static HttpClient client = new HttpClient();

        static Update()
        {
            client.DefaultRequestHeaders.Add("User-Agent", Settings.Instance.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        }

        public static void updateCheckMain(IProgressReporterDialogue frmProgressReporter)
        {
            var t = Type.GetType("Mono.Runtime");
            MONO = (t != null);

            try
            {
                if (domaster)
                {
                    CheckMD5(frmProgressReporter,
                        ConfigurationManager.AppSettings["MasterUpdateLocationMD5"].ToString(),
                        ConfigurationManager.AppSettings["MasterUpdateLocationZip"]);
                }
                else if (dobeta)
                {
                    CheckMD5(frmProgressReporter,
                        ConfigurationManager.AppSettings["BetaUpdateLocationMD5"].ToString(),
                        ConfigurationManager.AppSettings["BetaUpdateLocationZip"]);
                }
                else
                {
                    CheckMD5(frmProgressReporter,
                        ConfigurationManager.AppSettings["UpdateLocationMD5"].ToString(),
                        ConfigurationManager.AppSettings["UpdateLocation"]);
                }

                var process = new Process();
                string exePath = Path.GetDirectoryName(Application.ExecutablePath);
                if (MONO)
                {
                    process.StartInfo.WorkingDirectory = exePath;
                    process.StartInfo.FileName = "/bin/bash";
                    process.StartInfo.Arguments = " -c 'mono \"" + exePath + Path.DirectorySeparatorChar + "Updater.exe\"" +
                                                  "  \"" + Application.ExecutablePath + "\"'";
                }
                else
                {
                    process.StartInfo.WorkingDirectory = exePath;
                    process.StartInfo.FileName = exePath + Path.DirectorySeparatorChar + "Updater.exe";
                    process.StartInfo.Arguments = Application.ExecutablePath;
                }

                try
                {
                    foreach (string newupdater in Directory.GetFiles(exePath, "Updater.exe*.new"))
                    {
                        File.Copy(newupdater, newupdater.Remove(newupdater.Length - 4), true);
                        File.Delete(newupdater);
                    }
                }
                catch (Exception ex)
                {
                    log.Error("Exception during update", ex);
                }
                if (frmProgressReporter != null)
                    frmProgressReporter.UpdateProgressAndStatus(-1, "Starting Updater");
                log.Info("Starting new process: " + process.StartInfo.FileName + " with " +
                         process.StartInfo.Arguments);
                process.Start();
                log.Info("Quitting existing process");

                if (frmProgressReporter != null)
                    frmProgressReporter.BeginInvoke((Action)delegate { Application.Exit(); });
            }
            catch (AggregateException ex)
            {
                log.Error("Update Failed", ex.InnerException);
                CustomMessageBox.Show("Update Failed " + ex.InnerException?.Message);
            }
            catch (Exception ex)
            {
                log.Error("Update Failed", ex);
                CustomMessageBox.Show("Update Failed " + ex.Message);
            }
        }

        public static void CheckForUpdate(bool NotifyNoUpdate = false)
        {
            var baseurl = ConfigurationManager.AppSettings["UpdateLocationVersion"];

            if (dobeta)
                baseurl = ConfigurationManager.AppSettings["BetaUpdateLocationVersion"];

            if (string.IsNullOrWhiteSpace(baseurl))
                return;

            string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "version.txt");

            log.Debug(path);

            // Create a request using a URL that can receive a post. 
            string requestUriString = baseurl;
            log.Info("Checking for update at: " + requestUriString);

            bool updateFound = false;

            // Get the response.
            try
            {
                using (var response = client.GetAsync(requestUriString).GetAwaiter().GetResult())
                {
                    // Display the status.
                    log.Debug("Response status: " + response.StatusCode);
                    // Get the stream containing content returned by the server.

                    if (File.Exists(path))
                    {
                        Version LocalVersion, WebVersion;

                        using (var fs = File.OpenRead(path))
                        using (var sr = new StreamReader(fs))
                            LocalVersion = new Version(sr.ReadLine());

                        using (var sr = new StreamReader(response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()))
                            WebVersion = new Version(sr.ReadLine());

                        log.Info("New file Check: local " + LocalVersion + " vs Remote " + WebVersion);

                        if (LocalVersion < WebVersion)
                            updateFound = true;
                    }
                    else
                    {
                        updateFound = true;
                        log.Info("Local version file does not exist: Getting " + path);
                        // get it
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("Failed to check for update", ex);
                CustomMessageBox.Show("Не вдалося з'єднатися з сервером оновлень,\n" +
                                        "перевірте інтернет, якщо зв'зок стабільний\n" +
                                        "повідомте інженерку\n");
                return; // вихід з методу, бо не вдалося перевірити оновлення
            }

            if (updateFound)
            {
                // do the update in the main thread
                MainV2.instance.Invoke((Action)delegate
                {
                    string extra = dobeta ? "BETA " : "";
                    var dr = CustomMessageBox.Show(
                        extra + Strings.UpdateFound + " [link;" + baseurl.Replace("version.txt", "ChangeLog.txt") + ";ChangeLog]",
                        Strings.UpdateNow, MessageBoxButtons.YesNo);

                    if (dr == (int)DialogResult.Yes)
                        DoUpdate();
                });
            }
            else if (NotifyNoUpdate)
            {
                CustomMessageBox.Show(Strings.UpdateNotFound);
            }
        }

        public static void DoUpdate()
        {
            if (Program.WindowsStoreApp)
            {
                CustomMessageBox.Show(Strings.Not_available_when_used_as_a_windows_store_app);
                return;
            }

            IProgressReporterDialogue frmProgressReporter = new ProgressReporterDialogue()
            {
                Text = "Check for Updates",
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
            };

            ThemeManager.ApplyThemeTo(frmProgressReporter);

            frmProgressReporter.DoWork += new DoWorkEventHandler(DoUpdateWorker_DoWork);

            frmProgressReporter.doWorkArgs.CancelRequestChanged += (sender, args) => { frmProgressReporter.doWorkArgs.CancelAcknowledged = true; };
            frmProgressReporter.doWorkArgs.ForceExit = true;

            frmProgressReporter.UpdateProgressAndStatus(-1, "Checking for Updates");

            frmProgressReporter.RunBackgroundOperationAsync();

            frmProgressReporter.Dispose();
        }

        static void CheckMD5(IProgressReporterDialogue frmProgressReporter, string md5url, string baseurl)
        {
            log.InfoFormat("get checksums {0} - base {1}", md5url, baseurl);

            string responseFromServer = "";
            responseFromServer = client.GetStringAsync(md5url).GetAwaiter().GetResult();

            File.WriteAllText(Settings.GetRunningDirectory() + "checksums.txt.new", responseFromServer);

            Regex regex = new Regex(@"([^\s]+)\s+[^/]+/(.*)", RegexOptions.IgnoreCase);

            if (regex.IsMatch(responseFromServer))
            {
                if (frmProgressReporter != null)
                    frmProgressReporter.UpdateProgressAndStatus(-1, "Hashing Files");

                // cleanup dll's with the same exe name
                var dlls = Directory.GetFiles(Settings.GetRunningDirectory(), "*.dll", SearchOption.AllDirectories);
                var exes = Directory.GetFiles(Settings.GetRunningDirectory(), "*.exe", SearchOption.AllDirectories);
                List<string> files = new List<string>();

                // hash everything
                MatchCollection matchs = regex.Matches(responseFromServer);
                for (int i = 0; i < matchs.Count; i++)
                {
                    string hash = matchs[i].Groups[1].Value.ToString();
                    string file = matchs[i].Groups[2].Value.ToString();

                    files.Add(file);
                }

                // background md5
                List<Tuple<string, string, Task<bool>>> tasklist = new List<Tuple<string, string, Task<bool>>>();

                for (int i = 0; i < matchs.Count; i++)
                {
                    string hash = matchs[i].Groups[1].Value.ToString().Trim();
                    string file = matchs[i].Groups[2].Value.ToString().Trim();

                    if (file.ToLower().EndsWith("files.html"))
                        continue;

                    Task<bool> ismatch = Task<bool>.Factory.StartNew(() => MD5File(file, hash));

                    tasklist.Add(new Tuple<string, string, Task<bool>>(file, hash, ismatch));
                }
                // get count and wait for all hashing to be done
                int count = tasklist.Count(a =>
                {
                    a.Item3.Wait();
                    return !a.Item3.GetAwaiter().GetResult();
                });

                // parallel download
                ParallelOptions opt = new ParallelOptions() { MaxDegreeOfParallelism = 3 };

                tasklist.Sort((a, b) =>
                {
                    if (a == null || b == null) return 0;

                    if (a.Item1.ToLower().EndsWith(".exe") && b.Item1.ToLower().EndsWith(".exe"))
                        return a.Item1.CompareTo(b.Item1);
                    if (a.Item1.ToLower().EndsWith(".exe")) return -1;
                    if (b.Item1.ToLower().EndsWith(".exe")) return 1;

                    if (a.Item1.ToLower().EndsWith(".dll") && b.Item1.ToLower().EndsWith(".dll"))
                        return a.Item1.CompareTo(b.Item1);
                    if (a.Item1.ToLower().EndsWith(".dll")) return -1;
                    if (b.Item1.ToLower().EndsWith(".dll")) return 1;

                    return a.Item1.CompareTo(b.Item1);
                });
                /*
                if (frmProgressReporter != null)
                    frmProgressReporter.UpdateProgressAndStatus(-1, "Downloading parts");

                // start download
                if (baseurl.ToLower().Contains(".zip"))
                {
                    List<(int, int)> ranges = new List<(int, int)>();

                    using (DownloadStream ds = new DownloadStream(baseurl))
                    using (ZipArchive zip = new ZipArchive(ds))
                    {
                        FieldInfo fieldInfo = typeof(ZipArchiveEntry).GetField("_offsetOfLocalHeader", BindingFlags.NonPublic | BindingFlags.Instance);
                        var extents = zip.Entries.Select(e =>
                        {
                            var _offsetOfLocalHeader = (long)fieldInfo.GetValue(e);
                            return (e.FullName, _offsetOfLocalHeader);
                        }).OrderBy(a => a._offsetOfLocalHeader);

                        tasklist.ForEach(task => {

                            task.Item3.Wait();
                            bool match = task.Item3.GetAwaiter().GetResult();

                            if (!match)
                            {
                                extents.ForEach(entry1 =>
                                {
                                    var fn = entry1.FullName;

                                    var diskfn = task.Item1;

                                    if (diskfn.EndsWith(fn))
                                    {
                                        var next = ds.Length;
                                        zip.Entries.ForEach(entry2 => {
                                            var _offsetOfLocalHeader2 = (long)fieldInfo.GetValue(entry2);
                                            if (_offsetOfLocalHeader2 > entry1._offsetOfLocalHeader)
                                                next = Math.Min(_offsetOfLocalHeader2, next);
                                        });

                                        ranges.Add(((int)entry1._offsetOfLocalHeader, (int)(next)));
                                    }
                                });
                            }
                        });

                        ranges = ranges.SimplifyIntervals().ToList();
                        ranges.ForEach(range => {
                            ds.chunksize = range.Item2 - range.Item1;
                            ds.getAllData(range.Item1, range.Item2);
                        });
                        
                    }
                }
                */
                int done = 0;

                var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", Settings.Instance.UserAgent);

                Parallel.ForEach(tasklist, opt, task =>
                //foreach (var task in tasklist)
                {
                    string file = task.Item1;
                    string hash = task.Item2;
                    // check if existing matchs hash
                    task.Item3.Wait();
                    bool match = task.Item3.GetAwaiter().GetResult();

                    if (!match)
                    {
                        done++;
                        log.Info("Newer File " + file);

                        if (frmProgressReporter != null && frmProgressReporter.doWorkArgs.CancelRequested)
                        {
                            frmProgressReporter.doWorkArgs.CancelAcknowledged = true;
                            throw new Exception("User Request");
                        }

                        // check is we have already downloaded and matchs hash
                        if (!MD5File(file + ".new", hash))
                        {
                            if (frmProgressReporter != null)
                                frmProgressReporter.UpdateProgressAndStatus((int)((done / (double)count) * 100),
                                    Strings.Getting + file + "\n" + done + " of " + count + " of total " +
                                    tasklist.Count);

                            string subdir = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar;

                            subdir = subdir.Replace("" + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar,
                                "" + Path.DirectorySeparatorChar);

                            if (baseurl.ToLower().Contains(".zip"))
                            {
                                GetNewFileZip(frmProgressReporter, baseurl, subdir,
                                    Path.GetFileName(file));
                            }
                            else
                            {
                                GetNewFile(frmProgressReporter, baseurl + subdir.Replace('\\', '/'), subdir,
                                    Path.GetFileName(file), client);
                            }

                            // check the new downloaded file matchs hash
                            if (!MD5File(file + ".new", hash))
                            {
                                throw new Exception("File downloaded does not match hash: " + file);
                            }
                        }
                        else
                        {
                            log.Info("already got new File " + file);
                        }
                    }
                    else
                    {
                        log.Info("Same File " + file);

                        if (frmProgressReporter != null)
                            frmProgressReporter.UpdateProgressAndStatus(-1, Strings.Checking + file);
                    }
                });

                // cleanup unused dlls and exes
                dlls.ForEach(dll =>
                {
                    try
                    {
                        var result = files.Any(task => Path.GetFullPath(Path.Combine(Settings.GetRunningDirectory(), task)).ToLower().Equals(dll.ToLower()));

                        if (result == false)
                            File.Delete(dll);
                    }
                    catch { }
                });

                exes.ForEach(exe =>
                {
                    try
                    {
                        var result = files.Any(task => Path.GetFullPath(Path.Combine(Settings.GetRunningDirectory(), task)).ToLower().Equals(exe.ToLower()));

                        if (result == false)
                            File.Delete(exe);
                    }
                    catch { }
                });
            }
        }

        static bool MD5File(string filename, string hash)
        {
            try
            {
                if (File.Exists(filename))
                {
                    var md5 = new MD5Digest();
                    {
                        using (var stream = File.OpenRead(filename))
                        {
                            stream.ReadChunks().ForEach(a => md5.BlockUpdate(a, 0, a.Length));
                            var result = new byte[md5.GetDigestSize()];
                            md5.DoFinal(result, 0);
                            var answer = BitConverter.ToString(result).Replace("-", "").ToLower();

                            log.Debug(filename + "," + hash + "," + answer);

                            return hash == answer;
                        }
                    }
                }

                log.Debug(filename + "," + hash + "," + "File does not not exist");
            }
            catch (Exception ex)
            {
                log.Info("md5 fail " + ex.ToString());
            }

            return false;
        }

        static bool CRC32File(string filename, string hash)
        {
            try
            {
                if (!File.Exists(filename))
                    return false;



                var crc = new Crc32();

                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var buf = new byte[1024 * 1024];
                        while (stream.Position < stream.Length)
                        {
                            var read = stream.Read(buf, 0, buf.Length);
                            crc.Update(buf.Take(read).ToArray());
                        }

                        log.Debug(filename + "," + hash + "," + crc.Value.ToString("X"));

                        return hash == crc.Value.ToString("X");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Info("crc32 fail " + ex.ToString());
            }

            return false;
        }

        static void GetNewFileZip(IProgressReporterDialogue frmProgressReporter, string baseurl, string subdir, string file)
        {
            // create dest dir
            string dir = Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + subdir;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // get dest path
            string path = Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + subdir +
                          file;

            using (DownloadStream ds = new DownloadStream(baseurl))
            using (ZipArchive zip = new ZipArchive(ds))
            {
                log.InfoFormat("zip entry get {0}", (subdir.TrimStart('/').TrimStart('\\').Replace('\\', '/') + file));

                var entry = zip.GetEntry((subdir.TrimStart('/').TrimStart('\\').Replace('\\', '/') + file));

                if (entry == null)
                {
                    log.InfoFormat("zip missing entry {0} {1}", file, baseurl);
                    return;
                }

                ds.chunksize = (int)entry.CompressedLength + 2048;

                log.InfoFormat("unzip {0}", file);

                using (var fo = File.Open(path + ".new", FileMode.Create))
                {
                    entry.Open().CopyTo(fo, 1024 * 1024);
                    fo.Flush(true);
                    fo.Dispose();
                }

                zip.Dispose();
                ds.Dispose();
            }
        }

        static void GetNewFile(IProgressReporterDialogue frmProgressReporter, string baseurl, string subdir,
            string file, HttpClient httpClient)
        {
            // create dest dir
            string dir = Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + subdir;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // get dest path
            string path = Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + subdir +
                          file;

            Exception fail = null;
            int attempt = 0;

            // attempt to get file
            while (attempt < 2)
            {
                // check if user canceled
                if (frmProgressReporter.doWorkArgs.CancelRequested)
                {
                    frmProgressReporter.doWorkArgs.CancelAcknowledged = true;
                    throw new Exception("Cancel");
                }

                try
                {
                    string url = baseurl + file + "?" + new Random().Next();
                    // Get the response.
                    using (var response = client.GetAsync(url).GetAwaiter().GetResult())
                    {
                        // Display the status.
                        log.Info(response.ReasonPhrase);
                        // Get the stream containing content returned by the server.
                        Stream dataStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();

                        // from head
                        long bytes = response.Content.Headers.ContentLength();

                        long contlen = bytes;

                        byte[] buf1 = new byte[4096];

                        // if the file doesnt exist. just save it inplace
                        string fn = path + ".new";

                        using (FileStream fs = new FileStream(fn, FileMode.Create))
                        {
                            DateTime dt = DateTime.Now;

                            log.Debug("ContentLength: " + file + " " + bytes);

                            while (dataStream.CanRead)
                            {
                                try
                                {
                                    if (dt.Second != DateTime.Now.Second)
                                    {
                                        if (frmProgressReporter != null)
                                            frmProgressReporter.UpdateProgressAndStatus(
                                                (int)(((double)(contlen - bytes) / (double)contlen) * 100),
                                                Strings.Getting + file + ": " +
                                                (((double)(contlen - bytes) / (double)contlen) * 100)
                                                .ToString("0.0") +
                                                "%"); //+ Math.Abs(bytes) + " bytes");
                                        dt = DateTime.Now;
                                    }
                                }
                                catch
                                {
                                }

                                int len = dataStream.Read(buf1, 0, buf1.Length);
                                if (len == 0)
                                {
                                    log.Debug("GetNewFile: 0 byte read " + file);
                                    break;
                                }
                                bytes -= len;
                                fs.Write(buf1, 0, len);
                            }

                            log.Info("GetNewFile: " + file + " Done with length: " + fs.Length);
                            fs.Flush(true);
                            fs.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                    fail = ex;
                    attempt++;
                    continue;
                }

                // break if we have no exception
                break;
            }

            if (attempt == 2)
            {
                throw fail;
            }
        }

        static void DoUpdateWorker_DoWork(IProgressReporterDialogue sender)
        {
            // TODO: Is this the right place?

            #region Fetch Parameter Meta Data

            var progressReporterDialogue = ((IProgressReporterDialogue)sender);
            /*
            progressReporterDialogue.UpdateProgressAndStatus(-1, "Getting updated parameter documentation");

            try
            {
                var dns = Dns.GetHostAddresses("github.com");
                var dns2 = Dns.GetHostAddresses("raw.githubusercontent.com");

                if (dns.Length != 0)
                {
                    if (MissionPlanner.Utilities.Update.dobeta)
                        ParameterMetaDataParser.GetParameterInformation(
                            ConfigurationManager.AppSettings["ParameterLocationsBleeding"], "ParameterMetaData.xml");
                    else
                        ParameterMetaDataParser.GetParameterInformation(
                            ConfigurationManager.AppSettings["ParameterLocations"], "ParameterMetaData.xml");
                }
            }
            catch (Exception ex)
            {
                log.Error(ex.ToString());
                CustomMessageBox.Show("Error getting Parameter Information");
            }
            */
            #endregion Fetch Parameter Meta Data

            progressReporterDialogue.UpdateProgressAndStatus(-1, "Getting Base URL");

            try
            {
                File.WriteAllText(
                    Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar + "writetest.txt",
                    "this is a test");
            }
            catch (Exception ex)
            {
                log.Info("Write test failed");
                throw new Exception("Unable to write to the install directory", ex);
            }
            finally
            {
                try
                {
                    File.Delete(Path.GetDirectoryName(Application.ExecutablePath) + Path.DirectorySeparatorChar +
                                "writetest.txt");
                }
                catch
                {
                    log.Info("Write test cleanup failed");
                }
            }

            updateCheckMain(progressReporterDialogue);
        }
    }
}