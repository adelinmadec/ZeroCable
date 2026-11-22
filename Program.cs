using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Linq;
using System.Diagnostics;

namespace ZeroCable
{
    static class Program
    {
        static Form _form;
        static WebView2 _web;
        static Label _status;
        static Panel _loadingPanel;
        static TextBox _manualIpBox;
        static Button _manualConnectBtn;
        
        static volatile bool _isSearching = false;
        static string _configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZeroCable_IPs.txt");

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            BuildUI();
            StartAutoDiscovery();
            Application.Run(_form);
        }

        static void BuildUI()
        {
            _form = new Form {
                Text = "ZeroCable",
                Width = 800,
                Height = 600,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(18, 24, 38),
                WindowState = FormWindowState.Maximized 
            };

            _web = new WebView2 { Dock = DockStyle.Fill, Visible = false };
            _form.Controls.Add(_web);

            _loadingPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 24, 38) };
            
            _status = new Label {
                Text = "Initialisation...",
                Location = new Point(0, 200),
                Width = 800,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(0, 240, 255),
                Font = new Font("Segoe UI", 16, FontStyle.Bold)
            };
            
            _manualIpBox = new TextBox {
                Location = new Point(250, 300),
                Width = 200,
                Font = new Font("Segoe UI", 12),
                Visible = false
            };
            
            _manualConnectBtn = new Button {
                Text = "Connexion Manuelle",
                Location = new Point(460, 298),
                Width = 150,
                Height = 30,
                BackColor = Color.Orange,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Visible = false
            };
            
            _manualConnectBtn.Click += (s, e) => {
                string ip = _manualIpBox.Text.Trim().Replace("http://", "").Replace(":8080", "").Replace("/", "");
                if (!string.IsNullOrEmpty(ip)) {
                    SaveIpToMemory(ip);
                    LoadPage(ip);
                }
            };

            _loadingPanel.Controls.Add(_status);
            _loadingPanel.Controls.Add(_manualIpBox);
            _loadingPanel.Controls.Add(_manualConnectBtn);
            _form.Controls.Add(_loadingPanel);
            _loadingPanel.BringToFront();

            _form.Load += async (s, e) => {
                try {
                    var env = await CoreWebView2Environment.CreateAsync(null, null);
                    await _web.EnsureCoreWebView2Async(env);
                    _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    
                    _web.CoreWebView2.NavigationCompleted += (a, b) => {
                        if (b.IsSuccess) {
                            _loadingPanel.Visible = false;
                            _web.Visible = true;
                        } else {
                            _loadingPanel.Visible = true;
                            _web.Visible = false;
                            _status.Text = "Erreur de chargement.";
                            _status.ForeColor = Color.Red;
                        }
                    };
                } catch (Exception ex) {
                    _status.Text = "Erreur WebView2 : " + ex.Message;
                }
            };
        }

        static string GetCurrentWifiName()
        {
            try
            {
                Process process = new Process();
                process.StartInfo.FileName = "netsh";
                process.StartInfo.Arguments = "wlan show interfaces";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (string line in output.Split('\n'))
                {
                    if (line.Trim().StartsWith("SSID") && !line.Trim().StartsWith("BSSID"))
                    {
                        int index = line.IndexOf(':');
                        if (index >= 0)
                        {
                            return line.Substring(index + 1).Trim();
                        }
                    }
                }
            }
            catch { }
            return "Inconnu (Ethernet ou non connecté)";
        }

        static List<string> LoadKnownIps()
        {
            List<string> ips = new List<string>();
            if (File.Exists(_configFile))
            {
                foreach (string line in File.ReadAllLines(_configFile))
                {
                    if (!string.IsNullOrWhiteSpace(line) && !ips.Contains(line.Trim()))
                        ips.Add(line.Trim());
                }
            }
            return ips;
        }

        static void SaveIpToMemory(string ip)
        {
            List<string> ips = LoadKnownIps();
            if (!ips.Contains(ip))
            {
                ips.Add(ip);
                File.WriteAllLines(_configFile, ips);
            }
        }

        static void StartAutoDiscovery()
        {
            if (_isSearching) return;
            _isSearching = true;

            new Thread(() => {
                while (_isSearching) {
                    try
                    {
                        string wifiName = GetCurrentWifiName();
                        _form.BeginInvoke(new Action(() => { 
                            _status.Text = $"WiFi: {wifiName} | Recherche du téléphone..."; 
                            _status.ForeColor = Color.Orange;
                        }));

                        bool found = false;
                        string foundIp = null;

                        List<string> knownIps = LoadKnownIps();
                        foreach (string knownIp in knownIps)
                        {
                            if (CheckIp(knownIp)) {
                                found = true;
                                foundIp = knownIp;
                                break;
                            }
                        }

                        if (!found && _isSearching)
                        {
                            string[] magicIps = new string[] {
                                "192.168.43.1", "192.168.43.2", "192.168.43.100",
                                "10.0.0.1", "10.0.0.2", "10.0.0.10",
                                "10.130.62.1", "10.130.62.2", "10.130.62.100",
                                "172.20.10.1", "172.20.10.2",
                                "192.168.42.1", "192.168.42.129"
                            };

                            foreach (string magicIp in magicIps)
                            {
                                if (CheckIp(magicIp)) {
                                    found = true;
                                    foundIp = magicIp;
                                    break;
                                }
                            }
                        }

                        if (!found && _isSearching)
                        {
                            string localIp = "";
                            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback) {
                                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses) {
                                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork) {
                                            localIp = ip.Address.ToString();
                                            break;
                                        }
                                    }
                                }
                                if (!string.IsNullOrEmpty(localIp)) break;
                            }

                            if (!string.IsNullOrEmpty(localIp))
                            {
                                string subnet = localIp.Substring(0, localIp.LastIndexOf('.'));
                                List<Task> tasks = new List<Task>();
                                object lockObj = new object();

                                for (int i = 1; i < 255; i++)
                                {
                                    string testIp = subnet + "." + i;
                                    tasks.Add(Task.Run(() => {
                                        if (CheckIp(testIp)) {
                                            lock (lockObj) {
                                                if (!found) { found = true; foundIp = testIp; }
                                            }
                                        }
                                    }));
                                }
                                Task.WaitAll(tasks.ToArray());
                            }
                        }

                        if (found && foundIp != null) {
                            _isSearching = false;
                            SaveIpToMemory(foundIp); 
                            LoadPage(foundIp);
                        } else if (_isSearching) {
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = $"WiFi: {wifiName} | Entrez l'IP manuellement :";
                                _status.ForeColor = Color.Red;
                                _manualIpBox.Visible = true;
                                _manualConnectBtn.Visible = true;
                            }));
                        }
                    } 
                    catch { }
                    if (_isSearching) Thread.Sleep(2000); 
                }
            }) { IsBackground = true }.Start();
        }

        static bool CheckIp(string ip) {
            if (!_isSearching) return false;
            try {
                using (TcpClient client = new TcpClient()) {
                    var result = client.BeginConnect(ip, 8080, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(100); 
                    if (success && client.Connected) {
                        client.EndConnect(result);
                        return true;
                    }
                }
            } catch { }
            return false;
        }

        static void LoadPage(string ip)
        {
            _isSearching = false;
            
            // SÉCURITÉ : On vérifie que la fenêtre et le WebView existent avant d'agir
            if (_form == null || _form.IsDisposed) return;

            _form.BeginInvoke(new Action(() => {
                string wifiName = GetCurrentWifiName();
                _status.Text = $"Connecté sur {wifiName} ({ip}) ! Chargement...";
                _status.ForeColor = Color.LimeGreen;
                _manualIpBox.Visible = false;
                _manualConnectBtn.Visible = false;
                
                string url = $"http://{ip}:8080/";
                _form.Text = "ZeroCable - " + ip;
                
                try {
                    // SÉCURITÉ : On vérifie que le WebView est prêt
                    if (_web != null && _web.CoreWebView2 != null) {
                        _web.CoreWebView2.Navigate(url); 
                    } else {
                        // Si le WebView n'est pas prêt, on attend 500ms et on réessaie
                        Task.Delay(500).ContinueWith(t => LoadPage(ip));
                    }
                } catch (Exception ex) {
                    _status.Text = "Erreur : " + ex.Message;
                }
            }));
        }
    }
}
