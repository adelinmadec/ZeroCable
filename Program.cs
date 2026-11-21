using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace ZeroCable
{
    static class Program
    {
        static Form _form;
        static WebView2 _web;
        static Label _status;
        static Panel _loadingPanel;
        
        static volatile bool _isSearching = false;

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
                Text = "Recherche du téléphone (sans Internet)...",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(0, 240, 255),
                Font = new Font("Segoe UI", 16, FontStyle.Bold)
            };
            _loadingPanel.Controls.Add(_status);
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
                            _status.Text = "Erreur de chargement. Nouvelle recherche...";
                            _status.ForeColor = Color.Red;
                            _isSearching = false; // Relance la recherche
                            StartAutoDiscovery();
                        }
                    };
                } catch (Exception ex) {
                    _status.Text = "Erreur WebView2 : " + ex.Message;
                }
            };
        }

        static void StartAutoDiscovery()
        {
            if (_isSearching) return;
            _isSearching = true;

            new Thread(() => {
                while (_isSearching) {
                    try {
                        // 1. Trouver les cartes réseaux actives SANS utiliser Internet
                        List<string> localIps = new List<string>();
                        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                            // On ne garde que les cartes WiFi ou Hotspot (pas l'Ethernet ni le virtuel)
                            if (ni.OperationalStatus == OperationalStatus.Up && 
                               (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) {
                                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses) {
                                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address)) {
                                        localIps.Add(ip.Address.ToString());
                                    }
                                }
                            }
                        }

                        if (localIps.Count == 0) {
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = "Veuillez connecter le PC au WiFi du téléphone.";
                                _status.ForeColor = Color.Red;
                            }));
                            Thread.Sleep(2000);
                            continue;
                        }

                        bool found = false;

                        // 2. Scanner les sous-réseaux trouvés
                        foreach (string localIp in localIps) {
                            if (!_isSearching) break;

                            string subnet = localIp.Substring(0, localIp.LastIndexOf('.'));
                            
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = "Scan du réseau " + subnet + ".x...";
                                _status.ForeColor = Color.Orange;
                            }));

                            // Les IP de Hotspot Android finissent souvent par .1, .2, .100, etc.
                            List<int> ipEnds = Enumerable.Range(1, 254).ToList();
                            int[] priority = { 1, 2, 100, 101, 254, 50, 51, 10, 20 };
                            foreach (var p in priority) { ipEnds.Remove(p); ipEnds.Insert(0, p); }

                            List<Task> tasks = new List<Task>();
                            foreach (int i in ipEnds) {
                                string testIp = subnet + "." + i;
                                tasks.Add(Task.Run(() => {
                                    if (CheckIp(testIp)) found = true;
                                }));
                                if (found) break;
                            }
                            Task.WaitAll(tasks.ToArray());
                            if (found) break;
                        }

                        if (!found && _isSearching) {
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = "Téléphone introuvable sur ce réseau. Vérifiez le Hotspot.";
                                _status.ForeColor = Color.Red;
                            }));
                        }
                    } catch { }
                    
                    if (_isSearching) Thread.Sleep(2000); 
                }
            }) { IsBackground = true }.Start();
        }

        static bool CheckIp(string ip) {
            if (!_isSearching) return false;

            try {
                using (TcpClient client = new TcpClient()) {
                    // On réduit le timeout à 80ms pour que le scan soit ultra rapide
                    var result = client.BeginConnect(ip, 8080, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(80); 

                    if (success && client.Connected) {
                        client.EndConnect(result);
                        
                        _isSearching = false; 
                        
                        _form.BeginInvoke(new Action(() => {
                            _status.Text = "Téléphone trouvé sur " + ip + " ! Chargement...";
                            _status.ForeColor = Color.LimeGreen;
                            
                            string url = $"http://{ip}:8080/";
                            _form.Text = "ZeroCable - " + ip;
                            
                            try {
                                _web.CoreWebView2.Navigate(url); 
                            } catch (Exception ex) {
                                _status.Text = "Erreur : " + ex.Message;
                            }
                        }));
                        return true;
                    }
                }
            } catch { }
            return false;
        }
    }
}
