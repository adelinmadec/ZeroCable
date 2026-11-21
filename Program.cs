using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace ZeroCable
{
    static class Program
    {
        static Form _form;
        static WebView2 _web;
        static Label _status;
        static TextBox _manualIpBox;
        static Button _connectBtn;
        
        static string _currentIp = "";
        static string _currentName = "";
        static int _currentPort = 8080;
        static volatile bool _webReady = false;

        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            BuildUI();
            StartBeaconListener();
            Application.Run(_form);
        }

        static void BuildUI()
        {
            _form = new Form {
                Text = "ZeroCable",
                Width = 420,
                Height = 320,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.FromArgb(226, 232, 240),
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimumSize = new Size(400, 300)
            };

            _web = new WebView2 { Dock = DockStyle.Fill, Visible = false };
            _form.Controls.Add(_web);

            // --- BARRE DE CONNEXION MANUELLE (NOUVEAU) ---
            Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(10, 14, 23) };
            
            _manualIpBox = new TextBox { 
                Text = "10.10.0.251", // Met l'IP actuelle par défaut
                Width = 200, 
                Location = new Point(10, 18),
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _connectBtn = new Button {
                Text = "Connecter",
                Location = new Point(220, 16),
                Width = 100,
                Height = 26,
                BackColor = Color.FromArgb(0, 240, 255),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            _connectBtn.Click += (s, e) => {
                string ip = _manualIpBox.Text.Trim();
                if (!string.IsNullOrEmpty(ip)) {
                    _currentIp = ip;
                    _currentName = "Manuel";
                    if (_webReady) LoadPage();
                }
            };

            bottomPanel.Controls.Add(_manualIpBox);
            bottomPanel.Controls.Add(_connectBtn);
            _form.Controls.Add(bottomPanel);

            // --- BARRE DE STATUT ---
            _status = new Label {
                Text = "Recherche du telephone...",
                Dock = DockStyle.Bottom,
                Height = 34,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(10, 14, 23),
                ForeColor = Color.FromArgb(0, 240, 255),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            _form.Controls.Add(_status);
            _status.BringToFront(); // S'assurer que le statut est bien en bas

            _form.Load += async (s, e) => {
                try {
                    var env = await CoreWebView2Environment.CreateAsync(null, null);
                    await _web.EnsureCoreWebView2Async(env);

                    _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _web.CoreWebView2.NavigationCompleted += (a, b) => {
                        if (b.IsSuccess) _status.Text = "Connecte - " + _currentName + " @ " + _currentIp;
                    };

                    _webReady = true;
                    if (!string.IsNullOrEmpty(_currentIp)) LoadPage();
                } catch (Exception ex) {
                    _status.Text = "Erreur WebView2 : " + ex.Message;
                }
            };
        }

        static void StartBeaconListener()
        {
            new Thread(() => {
                UdpClient udp;
                try { 
                    udp = new UdpClient(new IPEndPoint(IPAddress.Any, 8081)); 
                }
                catch (Exception ex) { 
                    _form?.BeginInvoke(new Action(() => _status.Text = "Erreur port 8081 : " + ex.Message));
                    return; 
                }

                while (true) {
                    try {
                        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref ep);
                        string rawMessage = Encoding.UTF8.GetString(data);
                        
                        _form?.BeginInvoke(new Action(() => { 
                            if (!_webReady) _status.Text = "Signal: " + rawMessage; 
                        }));

                        var parts = rawMessage.Split('|');
                        if (parts.Length != 4 || parts[0] != "ZEROCABLE") continue;

                        string name = parts[1];
                        string ip   = parts[2];
                        int    port = int.TryParse(parts[3], out var p) ? p : 8080;

                        if (ip == _currentIp && port == _currentPort && name == _currentName)
                            continue;

                        _currentName = name;
                        _currentIp   = ip;
                        _currentPort = port;

                        _form?.BeginInvoke(new Action(() => {
                            _manualIpBox.Text = ip; // Met à jour la case
                            if (_webReady) LoadPage();
                            else _status.Text = "Trouve : " + name + " @ " + ip + "...";
                        }));
                    } catch { }
                }
            }) { IsBackground = true }.Start();
        }

        static void LoadPage()
        {
            string url = "http://" + _currentIp + ":" + _currentPort;
            _form.Text = "ZeroCable - " + _currentName;
            _web.Visible = true;
            _status.Text = "Chargement " + url + "...";
            _web.Source = new Uri(url);
        }
    }
}
