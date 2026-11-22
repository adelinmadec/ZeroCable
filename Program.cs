static void StartAutoDiscovery()
{
    if (_isSearching) return;
    _isSearching = true;

    new Thread(() => {
        while (_isSearching) {
            try
            {
                _form.BeginInvoke(new Action(() => { _status.Text = "Vérification des adresses connues..."; }));

                bool found = false;
                string foundIp = null;

                // 1. D'ABORD : Tester les IP mémorisées
                List<string> knownIps = LoadKnownIps();
                foreach (string knownIp in knownIps)
                {
                    if (CheckIp(knownIp)) {
                        found = true;
                        foundIp = knownIp;
                        break;
                    }
                }

                // 2. ENSUITE : Tester les IP "Magiques" (celles que les Hotspots Android utilisent le plus souvent)
                if (!found && _isSearching)
                {
                    _form.BeginInvoke(new Action(() => { _status.Text = "Test des adresses Hotspot typiques..."; }));
                    
                    // Liste des IP très souvent utilisées par Android
                    string[] magicIps = new string[] {
                        "192.168.43.1", "192.168.43.2", "192.168.43.100", // Android Classique
                        "10.0.0.1", "10.0.0.2", "10.0.0.10",              // Certains Huawei/Xiaomi
                        "10.130.62.1", "10.130.62.2", "10.130.62.100",    // Votre réseau actuel
                        "172.20.10.1", "172.20.10.2",                     // iPhone
                        "192.168.42.1", "192.168.42.129"                  // Autres Android
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

                // 3. ENFIN : Si toujours rien, on scanne tout le réseau
                if (!found && _isSearching)
                {
                    _form.BeginInvoke(new Action(() => { _status.Text = "Scan complet du réseau..."; _status.ForeColor = Color.Orange; }));

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

                // 4. Si on a trouvé, on sauvegarde et on se connecte
                if (found && foundIp != null) {
                    _isSearching = false;
                    SaveIpToMemory(foundIp); 
                    LoadPage(foundIp);
                } else if (_isSearching) {
                    // 5. Vraiment rien trouvé : on demande l'IP manuelle
                    _form.BeginInvoke(new Action(() => {
                        _status.Text = "Entrez l'IP affichée sur l'écran du téléphone :";
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
