using System;
using System.Drawing;
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
                Text = "Recherche du téléphone (Hotspot)...",
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
                string ip = _manualIpBox.Text.Trim().Replace("http://", "").Replace(":8080", "");
                LoadPage(ip);
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

        static void StartAutoDiscovery()
        {
            if (_isSearching) return;
            _isSearching = true;

            new Thread(() => {
                while (_isSearching) {
                    try
                    {
                        // 1. Trouver l'IP du PC (sur le Hotspot)
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
                        }

                        if (string.IsNullOrEmpty(localIp)) {
                            Thread.Sleep(1000);
                            continue;
                        }

                        string subnet = localIp.Substring(0, localIp.LastIndexOf('.'));

                        _form.BeginInvoke(new Action(() => {
                            _status.Text = "Scan du Hotspot " + subnet + ".x...";
                            _status.ForeColor = Color.Orange;
                        }));

                        bool found = false;
                        string foundIp = null;

                        // 2. On teste les adresses très rapidement
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

                        if (found && foundIp != null) {
                            _isSearching = false;
                            LoadPage(foundIp);
                        } else if (_isSearching) {
                            _form.BeginInvoke(new Action(() => {
                                _status.Text = "Pare-feu Windows bloque. Entrez l'IP manuellement :";
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
                    var success = result.AsyncWaitHandle.WaitOne(60); 
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
            _form.BeginInvoke(new Action(() => {
                _status.Text = "Téléphone trouvé sur " + ip + " ! Chargement...";
                _status.ForeColor = Color.LimeGreen;
                _manualIpBox.Visible = false;
                _manualConnectBtn.Visible = false;
                
                string url = $"http://{ip}:8080/";
                _form.Text = "ZeroCable - " + ip;
                
                try {
                    _web.CoreWebView2.Navigate(url); 
                } catch (Exception ex) {
                    _status.Text = "Erreur : " + ex.Message;
                }
            }));
        }
    }
}
