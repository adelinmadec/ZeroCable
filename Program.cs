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
                Text = "Démarrage de la recherche...",
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
                            // Si le téléphone se déconnecte ou change d'IP, on relance
                            _loadingPanel.Visible = true;
                            _web.Visible = false;
                            _status.Text = "Connexion perdue. Nouvelle recherche...";
                            _status.ForeColor = Color.Red;
                            _isSearching = false; 
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
                    try
                    {
                        // 1. Récupérer TOUTES les adresses IP locales (WiFi, Ethernet, Hotspot, etc.)
                        List<string> localIps = new List<string>();
                        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) 
                        {
                            if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback) 
                            {
                                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses) 
                                {
                                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork) 
                                    {
                                        localIps.Add(ip.Address.ToString());
                                    }
                                }
                            }
                        }

                        // 2. En déduire les sous-réseaux à scanner (ex: 192.168.1, 10.130.62)
                        HashSet<string> subnets = new HashSet<string>();
                        foreach (string ip in localIps)
                        {
                            string subnet = ip.Substring(0, ip.LastIndexOf('.'));
                            subnets.Add(subnet);
                        }

                        if (subnets.Count == 0)
                        {
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = "Aucun réseau détecté. Connectez le PC au WiFi/Hotspot.";
                                _status.ForeColor = Color.Red;
                            }));
                            Thread.Sleep(2000);
                            continue;
                        }

                        _form.BeginInvoke(new Action(() => {
                            _status.Text = "Scan de " + subnets.Count + " réseau(x) en cours...";
                            _status.ForeColor = Color.Orange;
                        }));

                        // 3. Lancer le scan massif sur TOUS les sous-réseaux en même temps
                        bool found = false;
                        string foundIp = null;

                        foreach (string subnet in subnets)
                        {
                            if (found || !_isSearching) break;

                            // On prépare la liste des IP à tester (de 1 à 254)
                            List<int> ipEnds = Enumerable.Range(1, 254).ToList();
                            
                            // On priorise les adresses de type passerelle (.1, .2) ou Android (.100, .101)
                            int[] priority = { 1, 2, 100, 101, 254, 50, 51, 10, 20 };
                            foreach (var p in priority) { ipEnds.Remove(p); ipEnds.Insert(0, p); }

                            List<Task> tasks = new List<Task>();
                            object lockObj = new object();

                            foreach (int i in ipEnds)
                            {
                                string testIp = subnet + "." + i;
                                tasks.Add(Task.Run(() => {
                                    if (CheckIp(testIp))
                                    {
                                        lock (lockObj) 
                                        { 
                                            if (!found) { found = true; foundIp = testIp; }
                                        }
                                    }
                                }));
                            }
                            
                            Task.WaitAll(tasks.ToArray());
                        }

                        // 4. Si on a trouvé l'IP, on charge la page
                        if (found && foundIp != null)
                        {
                            _isSearching = false; 
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = "Téléphone trouvé sur " + foundIp + " ! Chargement...";
                                _status.ForeColor = Color.LimeGreen;
                                
                                string url = $"http://{foundIp}:8080/";
                                _form.Text = "ZeroCable - " + foundIp;
                                
                                try {
                                    _web.CoreWebView2.Navigate(url); 
                                } catch (Exception ex) {
                                    _status.Text = "Erreur : " + ex.Message;
                                }
                            }));
                        }
                        else if (_isSearching)
                        {
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = "Téléphone introuvable. Vérifiez le Hotspot/Pare-feu.";
                                _status.ForeColor = Color.Red;
                            }));
                        }
                    } 
                    catch { }
                    
                    if (_isSearching) Thread.Sleep(1500); 
                }
            }) { IsBackground = true }.Start();
        }

        static bool CheckIp(string ip) {
            if (!_isSearching) return false;

            try {
                using (TcpClient client = new TcpClient()) {
                    var result = client.BeginConnect(ip, 8080, null, null);
                    // Timeout ultra court de 60ms pour que le scan soit hyper rapide
                    var success = result.AsyncWaitHandle.WaitOne(60); 

                    if (success && client.Connected) {
                        client.EndConnect(result);
                        return true;
                    }
                }
            } catch { }
            return false;
        }
    }
}
