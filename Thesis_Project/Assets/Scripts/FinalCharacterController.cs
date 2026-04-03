using UnityEngine;
using System.Globalization;
using TMPro;

public class FinalCharacterController : MonoBehaviour
{
    public enum SourceAxis { X, Y, Z }
    public enum ArmSide { Right, Left }

    [Header("UI & State")]
    public TextMeshProUGUI elbowAngle, shoulderAngle;
    public ArmSide currentArm = ArmSide.Right; 

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

        [Header("Left Arm Mirroring")]
        [Tooltip("Επίλεξε ποιοι άξονες θα αντιστρέφονται όταν φορεθεί στο αριστερό χέρι.")]
        public bool InvertPitchOnLeft = false;
        public bool InvertYawOnLeft = true;
        public bool InvertRollOnLeft = true;

        [Header("Smoothing")]
        [Range(1f, 50f)] public float SmoothSpeed = 20f;

        [HideInInspector] public Quaternion calibrationOffset = Quaternion.identity;
        [HideInInspector] public Quaternion initialWorldRot;
    }

    public Bone Elbow; //Sensor id = 0
    public Bone Shoulder; //Sensor id = 1

    private string buffer = "";
    private bool calibrateNextFrame = false;

    void Start()
    {
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

        if (SimpleBleReceiver.Instance != null)
            SimpleBleReceiver.Instance.OnPacketReceived += ProcessData;
    }

    public void CalibratePlayer() => calibrateNextFrame = true;

    public void SetRightArm()
    {
        currentArm = ArmSide.Right;
        CalibratePlayer(); 
    }

    public void SetLeftArm()
    {
        currentArm = ArmSide.Left;
        CalibratePlayer(); 
    }

    public void ToggleArmSide()
    {
        currentArm = (currentArm == ArmSide.Right) ? ArmSide.Left : ArmSide.Right;
        CalibratePlayer();
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
            string content = line.Substring(4);
            string[] sensors = content.Split('|');

            for (int i = 0; i < sensors.Length; i++)
            {
                Bone b = null;

                if (i == 0) b = Elbow;
                else if (i == 1) b = Shoulder;

                if (b == null || b.bone == null) continue;

                string[] q = sensors[i].Split(',');

                if (q.Length != 4) continue;

                float x = float.Parse(q[0], CultureInfo.InvariantCulture);
                float y = float.Parse(q[1], CultureInfo.InvariantCulture);
                float z = float.Parse(q[2], CultureInfo.InvariantCulture);
                float w = float.Parse(q[3], CultureInfo.InvariantCulture);

                Quaternion rawQ = new Quaternion(y, -z, -x, w);
                Vector3 euler = rawQ.eulerAngles;

                if (calibrateNextFrame)
                    b.calibrationOffset = rawQ;

                float dx = Mathf.DeltaAngle(b.calibrationOffset.eulerAngles.x, euler.x);
                float dy = Mathf.DeltaAngle(b.calibrationOffset.eulerAngles.y, euler.y);
                float dz = Mathf.DeltaAngle(b.calibrationOffset.eulerAngles.z, euler.z);

                float pMult = b.Pitch_Multiplier * ((currentArm == ArmSide.Left && b.InvertPitchOnLeft) ? -1f : 1f);
                float yMult = b.Yaw_Multiplier * ((currentArm == ArmSide.Left && b.InvertYawOnLeft) ? -1f : 1f);
                float rMult = b.Roll_Multiplier * ((currentArm == ArmSide.Left && b.InvertRollOnLeft) ? -1f : 1f);

                float worldX = GetVal(dx, dy, dz, b.Source_For_Pitch) * pMult;
                float worldY = GetVal(dx, dy, dz, b.Source_For_Yaw) * yMult;
                float worldZ = GetVal(dx, dy, dz, b.Source_For_Roll) * rMult;

                showEulerAngles(i, worldX, worldY, worldZ);

                Quaternion deltaRotation = Quaternion.Euler(worldX, worldY, worldZ);
                Quaternion targetRotation = deltaRotation * b.initialWorldRot;

                b.bone.rotation = Quaternion.Slerp(b.bone.rotation, targetRotation, Time.deltaTime * b.SmoothSpeed);
            }
            if (calibrateNextFrame)
                calibrateNextFrame = false;
        }
        catch { }
    }

    public void showEulerAngles(int sensorId, float x, float y, float z)
    {
        string label = (sensorId == 1) ? "ELBOW" : (sensorId == 0) ? "SHOULDER" : "NULL";

        string sidePrefix = currentArm == ArmSide.Right ? "[R]" : "[L]";

        string data = $"{sidePrefix} {label}\n" +
                      $"X: {x:F2}°\n" +
                      $"Y: {y:F2}°\n" +
                      $"Z: {z:F2}°";

        if (sensorId == 0) elbowAngle.text = data;
        else if (sensorId == 1) shoulderAngle.text = data;
    }

    float GetVal(float x, float y, float z, SourceAxis axis)
    {
        if (axis == SourceAxis.X) return x;
        if (axis == SourceAxis.Y) return y;
        return z;
    }
}