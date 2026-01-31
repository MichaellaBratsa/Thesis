using UnityEngine;
using System.Globalization;

public class CubeDebug : MonoBehaviour
{
    public enum SourceAxis { X, Y, Z }

    [System.Serializable]
    public class AxisMapper
    {
        public Transform cube;
        public string Name = "Sensor";

        [Header("Unity X (Pitch - Πάνω/Κάτω)")]
        public SourceAxis MapToUnityX = SourceAxis.X;
        public bool InvertX = false;

        [Header("Unity Y (Yaw - Στροφή)")]
        public SourceAxis MapToUnityY = SourceAxis.Y;
        public bool InvertY = false;

        [Header("Unity Z (Roll - Κλίση)")]
        public SourceAxis MapToUnityZ = SourceAxis.Z;
        public bool InvertZ = false;

        // Κρυφές μεταβλητές για calibration
        [HideInInspector] public Vector3 calibrationOffset;
    }

    public AxisMapper Shoulder; // Red
    public AxisMapper Wrist;    // Green
    public AxisMapper Elbow;    // Blue

    private string buffer = "";
    private bool calibrateNextFrame = false;

    void Start()
    {
        if (SimpleBleReceiver.Instance != null)
            SimpleBleReceiver.Instance.OnPacketReceived += ProcessData;
    }

    public void CalibrateBoxes()
    {
        calibrateNextFrame = true;
    }

    void ProcessData(string rawData)
    {
        buffer += rawData;
        int newlineIdx = buffer.IndexOf('\n');
        while (newlineIdx >= 0)
        {
            string line = buffer.Substring(0, newlineIdx).Trim();
            buffer = buffer.Substring(newlineIdx + 1);
            if (line.StartsWith("ALL:")) ParseLine(line);
            newlineIdx = buffer.IndexOf('\n');
        }
    }

    void ParseLine(string line)
    {
        try
        {
            string content = line.Substring(4);
            string[] sensors = content.Split('|');

            for (int i = 0; i < sensors.Length; i++)
            {
                if (i > 2) break;

                string[] q = sensors[i].Split(',');
                if (q.Length == 4)
                {
                    // Parse Raw Data
                    float x = float.Parse(q[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(q[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(q[2], CultureInfo.InvariantCulture);
                    float w = float.Parse(q[3], CultureInfo.InvariantCulture);

                    // Μετατροπή σε Euler Angles (Μοίρες)
                    // Χρησιμοποιούμε μια ουδέτερη μετατροπή αρχικά
                    Quaternion rawQ = new Quaternion(y, -z, -x, w);
                    Vector3 euler = rawQ.eulerAngles;

                    // Επιλογή του σωστού κύβου
                    AxisMapper mapper = (i == 0) ? Shoulder : (i == 1) ? Wrist : Elbow;

                    if (mapper.cube != null)
                    {
                        // 1. Calibration (Μηδενισμός)
                        if (calibrateNextFrame) mapper.calibrationOffset = euler;

                        // 2. Υπολογισμός Διαφοράς (Delta) για να αποφύγουμε το "τρελό γύρισμα" στο 360
                        float dx = Mathf.DeltaAngle(mapper.calibrationOffset.x, euler.x);
                        float dy = Mathf.DeltaAngle(mapper.calibrationOffset.y, euler.y);
                        float dz = Mathf.DeltaAngle(mapper.calibrationOffset.z, euler.z);

                        // 3. MAPPING - Η καρδιά του προβλήματος
                        // Εδώ διαλέγουμε ποιος άξονας του Arduino (dx, dy, dz) πάει πού

                        float finalX = GetSourceVal(dx, dy, dz, mapper.MapToUnityX) * (mapper.InvertX ? -1 : 1);
                        float finalY = GetSourceVal(dx, dy, dz, mapper.MapToUnityY) * (mapper.InvertY ? -1 : 1);
                        float finalZ = GetSourceVal(dx, dy, dz, mapper.MapToUnityZ) * (mapper.InvertZ ? -1 : 1);

                        // 4. Εφαρμογή στον Κύβο
                        mapper.cube.localRotation = Quaternion.Euler(finalX, finalY, finalZ);
                    }
                }
            }
            if (calibrateNextFrame) calibrateNextFrame = false;
        }
        catch { }
    }

    float GetSourceVal(float x, float y, float z, SourceAxis axis)
    {
        if (axis == SourceAxis.X) return x;
        if (axis == SourceAxis.Y) return y;
        return z;
    }
}