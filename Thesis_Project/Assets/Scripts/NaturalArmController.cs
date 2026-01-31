using UnityEngine;
using System.Globalization;

public class NaturalArmController : MonoBehaviour
{
    [System.Serializable]
    public class BoneConfig
    {
        public Transform bone;
        [Header("Correction (e.g. 0, 180, 0)")]
        public Vector3 SensorCorrection = Vector3.zero;
        public float SmoothSpeed = 20f;
        [HideInInspector] public Quaternion calibrationQuat = Quaternion.identity;
        [HideInInspector] public Quaternion initialBoneRot;
    }

    public BoneConfig Shoulder;
    public BoneConfig Wrist;
    public BoneConfig Elbow;

    private string buffer = "";
    private bool calibrateNextFrame = false;

    void Start()
    {
        // Αποθήκευση T-Pose
        if (Shoulder.bone) Shoulder.initialBoneRot = Shoulder.bone.rotation;
        if (Wrist.bone) Wrist.initialBoneRot = Wrist.bone.rotation;
        if (Elbow.bone) Elbow.initialBoneRot = Elbow.bone.rotation;

        if (SimpleBleReceiver.Instance != null)
            SimpleBleReceiver.Instance.OnPacketReceived += ProcessData;
    }

    public void CalibratePlayer() => calibrateNextFrame = true;

    void ProcessData(string rawData)
    {
        buffer += rawData;
        int newlineIdx = buffer.IndexOf('\n');
        while (newlineIdx >= 0)
        {
            string line = buffer.Substring(0, newlineIdx).Trim();
            buffer = buffer.Substring(newlineIdx + 1);
            ParseLine(line);
            newlineIdx = buffer.IndexOf('\n');
        }
    }

    void ParseLine(string line)
    {
        try
        {
            BoneConfig target = null;
            if (line.StartsWith("S:")) target = Shoulder;
            else if (line.StartsWith("W:")) target = Wrist;
            else if (line.StartsWith("E:")) target = Elbow;

            if (target != null && target.bone != null)
            {
                string content = line.Substring(2); // Remove "S:"
                string[] q = content.Split(',');

                float x = float.Parse(q[0], CultureInfo.InvariantCulture);
                float y = float.Parse(q[1], CultureInfo.InvariantCulture);
                float z = float.Parse(q[2], CultureInfo.InvariantCulture);
                float w = float.Parse(q[3], CultureInfo.InvariantCulture);

                // Μετατροπή Sensor -> Unity
                Quaternion raw = new Quaternion(y, -z, -x, w);
                Quaternion corrected = Quaternion.Euler(target.SensorCorrection) * raw;

                if (calibrateNextFrame) target.calibrationQuat = corrected;

                Quaternion delta = corrected * Quaternion.Inverse(target.calibrationQuat);
                Quaternion finalRot = delta * target.initialBoneRot;

                target.bone.rotation = Quaternion.Slerp(target.bone.rotation, finalRot, Time.deltaTime * target.SmoothSpeed);
            }
        }
        catch { }
    }
}