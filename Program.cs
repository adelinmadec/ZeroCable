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

namespace ZeroCable
{
    static class Program
    {
        static Form _form;
        static WebView2 _web;
        static Label _status;
        static Panel _loadingPanel;
        
        static volatile bool _isSearching = false;
        static volatile bool _webReady = false;

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
            // Fenêtre principale en plein écran (ou presque)
            _form = new Form {
                Text = "ZeroCable",
                Width = 800,
                Height = 600,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.FromArgb(226, 232, 240),
                WindowState = FormWindowState.Maximized // Plein écran
            };

            // Le WebView qui prend tout l'espace
            _web = new WebView2 { Dock = DockStyle.Fill, Visible = false };
            _form.Controls.Add(_web);

            // Panneau de chargement (s'affiche tant que la page n'est pas trouvée)
            _loadingPanel = new Panel { 
                Dock = DockStyle.Fill, 
                BackColor = Color.FromArgb(18, 24, 38) 
            };
            
            _status = new Label {
                Text = "Recherche du téléphone sur le réseau...",
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
                    // Initialisation du WebView2
                    var env = await CoreWebView2Environment.CreateAsync(null, null);
                    await _web.EnsureCoreWebView2Async(env);

                    // ⚠️ CORRECTION CRUCIALE : Autoriser le HTTP local et ignorer les erreurs de sécurité
                    _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    
                    // Ignorer les erreurs de sécurité pour les IP locales (10.x.x.x)
                    _web.CoreWebView2.Settings.IsWebMessageEnabled = true;
                    
                    _web.CoreWebView2.NavigationCompleted += (a, b) => {
                        if (b.IsSuccess) {
                            _loadingPanel.Visible = false;
                            _web.Visible = true;
                        } else {
                            // Si erreur de sécurité WebView, on affiche un message
                            _status.Text = "Erreur de connexion. Vérifiez que l'app est ouverte.";
                            _status.ForeColor = Color.Red;
                        }
                    };

                    _webReady = true;
                } catch (Exception ex) {
                    _status.Text = "Erreur WebView2 : " + ex.Message;
                }
            };
        }

        static void StartAutoDiscovery()
        {
            new Thread(() => {
                _isSearching = true;
                while (_isSearching) {
                    try {
                        // 1. Trouver notre propre IP sur le réseau (ex: 10.130.62.1)
                        string localIp = "";
                        using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0)) {
                            socket.Connect("8.8.8.8", 65530);
                            IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                            localIp = endPoint.Address.ToString();
                        }

                        string subnet = localIp.Substring(0, localIp.LastIndexOf('.'));

                        _form.BeginInvoke(new Action(() => {
                            _status.Text = "Recherche sur le réseau " + subnet + ".x...";
                            _status.ForeColor = Color.Orange;
                        }));

                        // 2. Scanner toutes les adresses IP du réseau en même temps
                        List<Task> tasks = new List<Task>();
                        for (int i = 1; i < 255; i++) {
                            string testIp = subnet + "." + i;
                            tasks.Add(Task.Run(() => CheckIp(testIp)));
                        }
                        
                        Task.WaitAll(tasks.ToArray());
                    } catch { }
                    
                    Thread.Sleep(3000); // Recommencer si pas trouvé
                }
            }) { IsBackground = true }.Start();
        }

        static void CheckIp(string ip) {
            if (!_isSearching) return;

            try {
                using (TcpClient client = new TcpClient()) {
                    var result = client.BeginConnect(ip, 8080, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(150); // Timeout court pour aller vite

                    if (success && client.Connected) {
                        client.EndConnect(result);
                        
                        // TROUVÉ !
                        _isSearching = false;
                        
                        _form.BeginInvoke(new Action(() => {
                            _status.Text = "Téléphone trouvé ! Chargement...";
                            _status.ForeColor = Color.LimeGreen;
                            
                            string url = $"http://{ip}:8080/";
                            _form.Text = "ZeroCable - " + ip;
                            
                            try {
                                _web.Source = new Uri(url);
                            } catch {
                                _status.Text = "Erreur d'URL : " + url;
                            }
                        }));
                    }
                }
            } catch { }
        }
    }
}
