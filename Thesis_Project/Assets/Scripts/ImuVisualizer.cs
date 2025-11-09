// Assets/Scripts/ImuVisualizer.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class ImuVisualizer : MonoBehaviour
{
    [Header("Targets per sensor id")]
    public Transform sensor0Target; // on-board LSM9DS1
    public Transform sensor1Target; // MPU 0x68
    public Transform sensor2Target; // MPU 0x69

    [Header("Per-sensor offsets (deg) to fix mounting / frame mismatches")]
    public Vector3 sensor0OffsetEuler; // applied after data
    public Vector3 sensor1OffsetEuler;
    public Vector3 sensor2OffsetEuler;

    [Header("Optional axis flips (apply before Quaternion.Euler)")]
    public bool flipX, flipY, flipZ; // if κάποιο axis είναι ανεστραμμένο

    // Χάρτης id->target και id->offset
    readonly Dictionary<int, Transform> targets = new();
    readonly Dictionary<int, Vector3> offsets = new();

    void OnEnable()
    {
        // Subscribe
        if (BleBridge.Instance != null)
        {
            BleBridge.Instance.OnLine += OnBleLine;
            BleBridge.Instance.OnStatus += s => UnityEngine.Debug.Log("[BLE] " + s);
            BleBridge.Instance.OnDeviceFound += (name, addr) => UnityEngine.Debug.Log($"[BLE] Found {name} {addr}");
        }

        targets[0] = sensor0Target;
        targets[1] = sensor1Target;
        targets[2] = sensor2Target;

        offsets[0] = sensor0OffsetEuler;
        offsets[1] = sensor1OffsetEuler;
        offsets[2] = sensor2OffsetEuler;
    }

    void OnDisable()
    {
        if (BleBridge.Instance != null)
            BleBridge.Instance.OnLine -= OnBleLine;
    }

    void OnBleLine(string line)
    {
        // Αναμένουμε "id,roll,pitch,yaw"
        // roll=X, pitch=Y, yaw=Z (deg) σύμφωνα με το sketch
        try
        {
            var parts = line.Split(',');
            if (parts.Length < 4) return;

            if (!int.TryParse(parts[0], out int id)) return;
            if (!targets.TryGetValue(id, out var t) || t == null) return;

            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float roll)) return;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pitch)) return;
            if (!float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float yaw)) return;

            // Optional flips για mismatch κατεύθυνσης αξόνων
            if (flipX) roll = -roll;
            if (flipY) pitch = -pitch;
            if (flipZ) yaw = -yaw;

            // Δημιουργία rotation από Euler (Unity δέχεται deg)
            // Στο sketch: ZYX (yaw, pitch, roll) σύμβαση, αλλά δίνονται ως (roll,pitch,yaw) γύρω από X,Y,Z αντίστοιχα.
            // Για οπτικοποίηση συνήθως αρκεί: Quaternion.Euler(roll, pitch, yaw).
            var rot = Quaternion.Euler(roll, pitch, yaw);

            // Εφαρμογή διορθωτικού offset ανά sensor (deg)
            if (offsets.TryGetValue(id, out var off))
            {
                var offQ = Quaternion.Euler(off);
                rot = rot * offQ;
            }

            // Θέσε rotation στο target (world ή local ανάλογα με το setup σου)
            t.rotation = rot;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"Parse error line='{line}': {e.Message}");
        }
    }

    // Βοηθοί για UI κουμπιά
    public void StartScan() => BleBridge.Instance?.StartScan();
    public void StopScan() => BleBridge.Instance?.StopScan();
    public void RequestMtu185() => BleBridge.Instance?.RequestMtu(185);
}
