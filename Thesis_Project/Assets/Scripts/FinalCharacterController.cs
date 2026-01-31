using UnityEngine;
using System.Globalization;

// Controls a character's bones (shoulder, wrist, elbow) using quaternion data coming from BLE/Arduino.
public class FinalCharacterController : MonoBehaviour
{
    // Which axis (X/Y/Z) from the incoming Euler delta should drive pitch/yaw/roll.
    public enum SourceAxis { X, Y, Z }

    [System.Serializable]
    public class BoneConfig
    {
        // The bone Transform in the rig that will be rotated.
        public Transform bone;

        public string Name = "Sensor";

        [Header("Axis Mapping & Sensitivity")]
        // Choose which incoming axis drives the bone's pitch (X rotation).
        public SourceAxis Source_For_Pitch = SourceAxis.X;
        [Range(-2f, 2f)] public float Pitch_Multiplier = 1f;

        // Choose which incoming axis drives the bone's yaw (Y rotation).
        public SourceAxis Source_For_Yaw = SourceAxis.Y;
        [Range(-2f, 2f)] public float Yaw_Multiplier = 1f;

        // Choose which incoming axis drives the bone's roll (Z rotation).
        public SourceAxis Source_For_Roll = SourceAxis.Z;
        [Range(-2f, 2f)] public float Roll_Multiplier = 1f;

        [Header("Smoothing (Noise Filter)")]
        // Higher values = faster response, lower values = smoother but laggier motion.
        [Range(1f, 50f)] public float SmoothSpeed = 20f;

        // Stores the "zero pose" quaternion captured during calibration (used as reference).
        [HideInInspector] public Quaternion calibrationOffset = Quaternion.identity;

        // The bone's initial world rotation at Start() so we can apply deltas on top of it.
        [HideInInspector] public Quaternion initialWorldRot;
    }

    // Bone driven by sensor index 0
    public BoneConfig Shoulder; 
    // Bone driven by sensor index 1.
    public BoneConfig Wrist;    
    // Bone driven by sensor index 2.
    public BoneConfig Elbow;   

    // Accumulates incoming BLE text until we have full lines ending with '\n'.
    private string buffer = "";
    // When true, the next processed frame will capture calibration offsets.
    private bool calibrateNextFrame = false;

    void Start()
    {
        // Cache initial bone rotations so we can always return to base + delta.
        if (Shoulder.bone) Shoulder.initialWorldRot = Shoulder.bone.rotation;
        if (Wrist.bone) Wrist.initialWorldRot = Wrist.bone.rotation;
        if (Elbow.bone) Elbow.initialWorldRot = Elbow.bone.rotation;

        // Subscribe to BLE packet event if the receiver exists.
        if (SimpleBleReceiver.Instance != null)
            SimpleBleReceiver.Instance.OnPacketReceived += ProcessData;
    }

    public void CalibratePlayer()
    {
        // Request a calibration capture on the next ParseLine processing pass.
        calibrateNextFrame = true;
    }

    void ProcessData(string rawData)
    {
        buffer += rawData;

        // Look for first newline to know we have a complete line.
        int newlineIdx = buffer.IndexOf('\n');

        while (newlineIdx >= 0)
        {
            // Extract one line (without newline) and trim whitespace.
            string line = buffer.Substring(0, newlineIdx).Trim();

            // Remove the processed line from the buffer.
            buffer = buffer.Substring(newlineIdx + 1);

            // Only parse lines with the expected prefix.
            if (line.StartsWith("ALL:")) ParseLine(line);

            // Check again for more complete lines.
            newlineIdx = buffer.IndexOf('\n');
        }
    }

    void ParseLine(string line)
    {
        try
        {
            // Remove "ALL:" prefix.
            string content = line.Substring(4);

            // Each sensor's quaternion is separated by '|'.
            string[] sensors = content.Split('|');

            // Expect up to 3 sensors: 0=Shoulder, 1=Wrist, 2=Elbow.
            for (int i = 0; i < sensors.Length; i++)
            {
                // Ignore any sensors beyond the first three.
                if (i > 2)
                    break;

                // Quaternion components arrive as "x,y,z,w".
                string[] q = sensors[i].Split(',');

                // Only proceed if the quaternion is complete.
                if (q.Length == 4)
                {
                    // Parse floats using invariant culture to avoid comma/decimal locale issues.
                    float x = float.Parse(q[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(q[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(q[2], CultureInfo.InvariantCulture);
                    float w = float.Parse(q[3], CultureInfo.InvariantCulture);

                    // Convert sensor coordinate system to Unity's coordinate system.
                    Quaternion rawQ = new Quaternion(y, -z, -x, w);

                    // Convert to Euler angles for easy per-axis delta mapping.
                    Vector3 euler = rawQ.eulerAngles;

                    // Pick which bone config this sensor index controls.
                    BoneConfig cfg = (i == 0) ? Shoulder : (i == 1) ? Wrist : Elbow;

                    // Only rotate if the bone was assigned.
                    if (cfg.bone != null)
                    {
                        // On calibration request, store current sensor quaternion as the "zero" reference.
                        if (calibrateNextFrame)
                            cfg.calibrationOffset = rawQ;

                        // Compute per-axis angular differences relative to calibration (handles wraparound).
                        float dx = Mathf.DeltaAngle(cfg.calibrationOffset.eulerAngles.x, euler.x);
                        float dy = Mathf.DeltaAngle(cfg.calibrationOffset.eulerAngles.y, euler.y);
                        float dz = Mathf.DeltaAngle(cfg.calibrationOffset.eulerAngles.z, euler.z);

                        // MAPPING: choose which delta axis drives pitch/yaw/roll, then scale with multipliers.
                        float worldX = GetVal(dx, dy, dz, cfg.Source_For_Pitch) * cfg.Pitch_Multiplier;
                        float worldY = GetVal(dx, dy, dz, cfg.Source_For_Yaw) * cfg.Yaw_Multiplier;
                        float worldZ = GetVal(dx, dy, dz, cfg.Source_For_Roll) * cfg.Roll_Multiplier;

                        // Build a delta rotation from mapped angles.
                        Quaternion deltaRotation = Quaternion.Euler(worldX, worldY, worldZ);

                        // Apply delta on top of the initial bone rotation captured at Start().
                        Quaternion targetRotation = deltaRotation * cfg.initialWorldRot;

                        // Smoothly interpolate current rotation toward the target to reduce jitter.
                        cfg.bone.rotation = Quaternion.Slerp(
                            cfg.bone.rotation,
                            targetRotation,
                            Time.deltaTime * cfg.SmoothSpeed
                        );
                    }
                }
            }

            // After using the calibration flag once, turn it off.
            if (calibrateNextFrame)
                calibrateNextFrame = false;
        }
        // Swallow any parsing/format errors to avoid crashing the update loop.
        catch { }
    }

    float GetVal(float x, float y, float z, SourceAxis axis)
    {
        // Return the requested axis value from the provided x/y/z values.
        if (axis == SourceAxis.X)
            return x;

        if (axis == SourceAxis.Y)
            return y;

        return z;
    }
}
