using UnityEngine;
using System.Globalization;
using TMPro;

public class FinalCharacterController : MonoBehaviour
{
    public enum SourceAxis { X, Y, Z }

    public TextMeshProUGUI elbowAngle, shoulderAngle;

    [System.Serializable]
    public class Bone
    {
        public Transform bone;
        public string Name = "Sensor";

        [Header("Axis Mapping & Sensitivity")]
        public SourceAxis Source_For_Pitch = SourceAxis.X;
        [Range(-2f, 2f)] public float Pitch_Multiplier = 1f;
        public SourceAxis Source_For_Yaw = SourceAxis.Y;
        [Range(-2f, 2f)] public float Yaw_Multiplier = 1f;
        public SourceAxis Source_For_Roll = SourceAxis.Z;
        [Range(-2f, 2f)] public float Roll_Multiplier = 1f;

        [Header("Smoothing")]
        [Range(1f, 50f)] public float SmoothSpeed = 20f;

        [HideInInspector] public Quaternion calibrationOffset = Quaternion.identity;
        [HideInInspector] public Quaternion initialWorldRot;
    }

    public Bone Elbow; //Sensor id = 1
    public Bone Shoulder; //Sensor id = 2

    private string buffer = ""; // Buffers incoming BLE data to handle partial messages
    private bool calibrateNextFrame = false;

    void Start()
    {
        // Capture the initial pose of the character model
        if (Elbow.bone)
        {
            Elbow.initialWorldRot = Elbow.bone.rotation;
            elbowAngle.text = "Elbow";
        }

        if (Shoulder.bone)
        {
            Shoulder.initialWorldRot = Shoulder.bone.rotation;
            shoulderAngle.text = "Shoulder";
        }

        // Subscribe to the Bluetooth receiver's data event
        if (SimpleBleReceiver.Instance != null)
            SimpleBleReceiver.Instance.OnPacketReceived += ProcessData;
    }

    public void CalibratePlayer() => calibrateNextFrame = true;

    void ProcessData(string rawData)
    {
        buffer += rawData;
        int newlineIdx = buffer.IndexOf('\n');

        // Process every complete line found in the buffer
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
            // Remove "ALL:" prefix and split the sensor groups (expected format: ALL:quat1|quat2|quat3)
            string content = line.Substring(4);
            string[] sensors = content.Split('|');

            for (int i = 0; i < sensors.Length; i++)
            {

                Bone b = null;

                // Identify which sensor index corresponds to which bone
                if (i == 1) 
                    b = Elbow;
                else if (i == 2) 
                    b = Shoulder;

                if (b == null || b.bone == null) 
                    continue;

                string[] q = sensors[i].Split(',');

                if (q.Length != 4) 
                    continue;

                // Split individual Quaternion components (x,y,z,w)
                float x = float.Parse(q[0], CultureInfo.InvariantCulture);
                float y = float.Parse(q[1], CultureInfo.InvariantCulture);
                float z = float.Parse(q[2], CultureInfo.InvariantCulture);
                float w = float.Parse(q[3], CultureInfo.InvariantCulture);

                // Reconstruct Quaternion and adjust coordinate system (Sensor space to Unity space)
                Quaternion rawQ = new Quaternion(y, -z, -x, w);
                Vector3 euler = rawQ.eulerAngles;

                if (calibrateNextFrame) 
                    b.calibrationOffset = rawQ;

                // Calculate the difference between current sensor rotation and calibrated rotation
                float dx = Mathf.DeltaAngle(b.calibrationOffset.eulerAngles.x, euler.x);
                float dy = Mathf.DeltaAngle(b.calibrationOffset.eulerAngles.y, euler.y);
                float dz = Mathf.DeltaAngle(b.calibrationOffset.eulerAngles.z, euler.z);

                // Remap the sensor axes to the intended Unity axes based on user configuration
                float worldX = GetVal(dx, dy, dz, b.Source_For_Pitch) * b.Pitch_Multiplier;
                float worldY = GetVal(dx, dy, dz, b.Source_For_Yaw) * b.Yaw_Multiplier;
                float worldZ = GetVal(dx, dy, dz, b.Source_For_Roll) * b.Roll_Multiplier;

                showEulerAngles(i, worldX, worldY, worldZ);

                // Apply the delta rotation to the original world rotation of the bone
                Quaternion deltaRotation = Quaternion.Euler(worldX, worldY, worldZ);
                Quaternion targetRotation = deltaRotation * b.initialWorldRot;

                //showEulerAngles(i, worldX, worldY, worldZ);

                // Smoothly interpolate to the new rotation to avoid jitter
                b.bone.rotation = Quaternion.Slerp(b.bone.rotation, targetRotation, Time.deltaTime * b.SmoothSpeed);
            }
            if (calibrateNextFrame) 
                calibrateNextFrame = false;
        }
        catch { }
    }

    public void showEulerAngles(int sensorId, float x, float y, float z)
    {
        string label = "NULL";
            
        if (sensorId == 1)
            label = "ELBOW";
        else if (sensorId == 2)
            label = "SHOULDER";

        string data = $"{label}\n" +
                      $"X: {x:F2}°\n" +
                      $"Y: {y:F2}°\n" +
                      $"Z: {z:F2}°";

        if (sensorId == 1)
        {
            elbowAngle.text = data;
        }
        else if (sensorId == 2)
        {
            shoulderAngle.text = data;
        }
    }

    // Helper to pick the correct float value based on the Enum selection
    float GetVal(float x, float y, float z, SourceAxis axis)
    {
        if (axis == SourceAxis.X) 
            return x;
        if (axis == SourceAxis.Y) 
            return y;
        return z;
    }
}