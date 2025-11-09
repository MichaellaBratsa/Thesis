using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class SimpleBlePanel : MonoBehaviour
{
    [Header("UI")]
    public Button scanConnectBtn;
    public Button disconnectBtn;
    public TMP_Text logText; // ή TMP_Text
    public bool onlyImuLines = true; // αν θες να βλέπεις μόνο "id,roll,pitch,yaw"

    readonly List<(string name, string addr)> devices = new();
    StringBuilder sb = new StringBuilder(8192);

    void Awake()
    {
        if (scanConnectBtn) scanConnectBtn.onClick.AddListener(OnScanConnect);
        if (disconnectBtn) disconnectBtn.onClick.AddListener(OnDisconnect);
    }

    void OnEnable()
    {
        if (BleBridge.Instance == null)
        {
            Append("[ERR] BleBridge not found in scene.");
            return;
        }
        BleBridge.Instance.OnStatus += Append;
        BleBridge.Instance.OnDeviceFound += OnDeviceFound;
        BleBridge.Instance.OnLine += OnLine;
    }

    void OnDisable()
    {
        if (BleBridge.Instance == null) return;
        BleBridge.Instance.OnStatus -= Append;
        BleBridge.Instance.OnDeviceFound -= OnDeviceFound;
        BleBridge.Instance.OnLine -= OnLine;
    }

    void OnScanConnect()
    {
        devices.Clear();
        Append("[UI] Scanning...");
        BleBridge.Instance?.StartScan();
        // μόλις βρεθεί συσκευή, το OnDeviceFound θα κάνει auto-connect στο 1ο
    }

    void OnDisconnect()
    {
        BleBridge.Instance?.Disconnect();
        Append("[UI] Disconnect requested.");
    }

    void OnDeviceFound(string name, string addr)
    {
        // κράτα μία φορά μόνο
        if (devices.Exists(d => d.addr == addr)) return;
        devices.Add((name, addr));
        Append($"[Found] {name} [{addr}]");

        // auto-connect στον ΠΡΩΤΟ που βρέθηκε
        if (devices.Count == 1)
        {
            Append("[UI] Auto-connecting first device...");
            BleBridge.Instance?.Connect(addr);
        }
    }

    void OnLine(string line)
    {
        if (onlyImuLines)
        {
            // δέχεται μόνο γραμμές "id,roll,pitch,yaw"
            var parts = line.Split(',');
            if (parts.Length != 4) return;
            // optional: validation για id/float parsers, αλλά κρατάμε απλό
        }
        Append(line);
    }

    void Append(string msg)
    {
        if (sb.Length > 20000) sb.Length = 0; // απλό cap
        sb.AppendLine(msg);
        if (logText) logText.text = sb.ToString();
    }
}
