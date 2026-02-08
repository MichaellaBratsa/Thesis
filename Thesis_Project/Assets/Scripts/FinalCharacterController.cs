using UnityEngine;
using System.Globalization;

// Controls a character's bones (shoulder, wrist, elbow)
// using quaternion data coming from BLE/Arduino.
public class FinalCharacterController : MonoBehaviour
{
    // Which axis (X/Y/Z) from the incoming Euler delta
    // should drive pitch/yaw/roll.
    public enum SourceAxis
    {
        X,
        Y,
        Z
    }

    [System.Serializable]
    public class BoneConfig
    {
        // The bone Transform in the rig that will be rotated.
        public Transform bone;
        public string Name = "Sensor";

        [Header("Axis Mapping & Sensitivity")]
        // Choose which incoming axis drives the bone's pitch (X rotation).
        public SourceAxis Source_For_Pitch = SourceAxis.X;

        [Range(-2f, 2f)]
        public float Pitch_Multiplier = 1f;

        // Choose which incoming axis drives the bone's yaw (Y rotation).
        public SourceAxis Source_For_Yaw = SourceAxis.Y;

        [Range(-2f, 2f)]
        public float Yaw_Multiplier = 1f;

        // Choose which incoming axis drives the bone's roll (Z rotation).
        public SourceAxis Source_For_Roll = SourceAxis.Z;

        [Range(-2f, 2f)]
        public float Roll_Multiplier = 1f;

        [Header("Smoothing (Noise Filter)")]
        // Higher values = faster response,
        // lower values = smoother but laggier motion.
        [Range(1f, 50f)]
        public float SmoothSpeed = 20f;

        // Stores the "zero pose" quaternion captured during calibration.
        [HideInInspector]
        public Quaternion calibrationOffset = Quaternion.identity;

        // The bone's initial world rotation at Start().
        [HideInInspector]
        public Quaternion initialWorldRot;
    }

    // Bone driven by sensor index 0
    public BoneConfig Shoulder;

    // Bone driven by sensor index 1
    public BoneConfig Wrist;

    // Bone driven by sensor index 2
    public BoneConfig Elbow;

    // Accumulates incoming BLE text until we have full lines ending with '\n'.
    private string buffer = "";

    // When true, the next processed frame will capture calibration offsets.
    private bool calibrateNextFrame = false;

    void Start()
    {
        // Cache initial bone rotations.
        if (Shoulder.bone)
            Shoulder.initialWorldRot = Shoulder.bone.rotation;

        if (Wrist.bone)
            Wrist.initialWorldRot = Wrist.bone.rotation;

        if (Elbow.bone)
            Elbow.initialWorldRot = Elbow.bone.rotation;

        // Subscribe to BLE packet event.
        if (SimpleBleReceiver.Instance != null)
            SimpleBleReceiver.Instance.OnPacketReceived += ProcessData;
    }

    public void CalibratePlayer()
    {
        // Request a calibration capture on the next parse.
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

            if (line.StartsWith("ALL:"))
                ParseLine(line);

            newlineIdx = buffer.IndexOf('\n');
        }
    }

    void ParseLine(string line)
    {
        try
        {
            // Remove "ALL:" prefix.
            string content = line.Substring(4);

            // Each sensor quaternion separated by '|'.
            string[] sensors = content.Split('|');

            for (int i = 0; i < sensors.Length; i++)
            {
                if (i > 2)
                    break;

                string[] q = sensors[i].Split(',');
                if (q.Length != 4)
                    continue;

                float x = float.Parse(q[0], CultureInfo.InvariantCulture);
                float y = float.Parse(q[1], CultureInfo.InvariantCulture);
                float z = float.Parse(q[2], CultureInfo.InvariantCulture);
                float w = float.Parse(q[3], CultureInfo.InvariantCulture);

                // Convert sensor coordinates to Unity coordinates.
                Quaternion rawQ = new Quaternion(y, -z, -x, w);
                Vector3 euler = rawQ.eulerAngles;

                BoneConfig cfg =
                    (i == 0) ? Shoulder :
                    (i == 1) ? Wrist :
                    Elbow;

                if (cfg.bone == null)
                    continue;

                if (calibrateNextFrame)
                    cfg.calibrationOffset = rawQ;

                float dx = Mathf.DeltaAngle(
                    cfg.calibrationOffset.eulerAngles.x, euler.x);
                float dy = Mathf.DeltaAngle(
                    cfg.calibrationOffset.eulerAngles.y, euler.y);
                float dz = Mathf.DeltaAngle(
                    cfg.calibrationOffset.eulerAngles.z, euler.z);

                float worldX =
                    GetVal(dx, dy, dz, cfg.Source_For_Pitch) *
                    cfg.Pitch_Multiplier;

                float worldY =
                    GetVal(dx, dy, dz, cfg.Source_For_Yaw) *
                    cfg.Yaw_Multiplier;

                float worldZ =
                    GetVal(dx, dy, dz, cfg.Source_For_Roll) *
                    cfg.Roll_Multiplier;

                Quaternion deltaRotation =
                    Quaternion.Euler(worldX, worldY, worldZ);

                Quaternion targetRotation =
                    deltaRotation * cfg.initialWorldRot;

                cfg.bone.rotation = Quaternion.Slerp(
                    cfg.bone.rotation,
                    targetRotation,
                    Time.deltaTime * cfg.SmoothSpeed
                );
            }

            if (calibrateNextFrame)
                calibrateNextFrame = false;
        }
        catch
        {
        }
    }

    float GetVal(float x, float y, float z, SourceAxis axis)
    {
        if (axis == SourceAxis.X) return x;
        if (axis == SourceAxis.Y) return y;
        return z;
    }
}
