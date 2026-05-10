using UnityEngine;
using System.Globalization;
using TMPro;

public class FinalCharacterController : MonoBehaviour
{
    public enum SourceAxis { X, Y, Z }
    public enum ArmSide { Right, Left }

    [Header("UI & State")]
    public ArmSide currentArm = ArmSide.Right;
    [SerializeField] private GameObject rightHand;
    [SerializeField] private GameObject leftHand;

    public TMP_Text statusText;

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

    [System.Serializable] // Χρειάζεται για να το δείχνει ο Inspector
    public class Left_Bone : Bone
    {
        [Header("Left Arm Mirroring")]
        public bool InvertPitchOnLeft = true;
        public bool InvertYawOnLeft = false;
        public bool InvertRollOnLeft = true;
    }

    [Header("Right Arm Bones")]
    public Bone RightElbow;
    public Bone RightShoulder;

    [Header("Left Arm Bones")]
    public Left_Bone LeftElbow;
    public Left_Bone LeftShoulder;

    private string buffer = "";
    private bool calibrateNextFrame = false;

    void Start()
    {
        //SetWhichArm();
        CheckHand();

        if (RightElbow.bone) RightElbow.initialWorldRot = RightElbow.bone.rotation;
        if (RightShoulder.bone) RightShoulder.initialWorldRot = RightShoulder.bone.rotation;

        if (LeftElbow.bone) LeftElbow.initialWorldRot = LeftElbow.bone.rotation;
        if (LeftShoulder.bone) LeftShoulder.initialWorldRot = LeftShoulder.bone.rotation;

        if (SimpleBleReceiver.Instance != null)
            SimpleBleReceiver.Instance.OnPacketReceived += ProcessData;
    }

    //public void SetWhichArm()
    //{
    //    if (GameManager.Instance.isRightHanded == true)
    //        currentArm = ArmSide.Right;
    //    else
    //        currentArm = ArmSide.Left;
    //}

    public void UpdateArmVisibility()
    {
        if (currentArm == ArmSide.Right)
        {
            if (leftHand != null) leftHand.SetActive(false);
            if (rightHand != null) rightHand.SetActive(true);
        }
        else
        {
            if (rightHand != null) rightHand.SetActive(false);
            if (leftHand != null) leftHand.SetActive(true);
        }
    }

    public void CheckHand()
    {
        UpdateArmVisibility();

        if (currentArm == ArmSide.Right)
            SetRightArm();
        else
            SetLeftArm();
    }

    public void CalibratePlayer() => calibrateNextFrame = true;

    public void SetRightArm()
    {
        currentArm = ArmSide.Right;
        UpdateArmVisibility();
        ResetInactiveArm();
        CalibratePlayer();
    }

    public void SetLeftArm()
    {
        currentArm = ArmSide.Left;
        UpdateArmVisibility();
        ResetInactiveArm();
        CalibratePlayer();
    }

    public void ToggleArmSide()
    {
        currentArm = (currentArm == ArmSide.Right) ? ArmSide.Left : ArmSide.Right;
        UpdateArmVisibility();
        ResetInactiveArm();
        CalibratePlayer();
    }

    private void ResetInactiveArm()
    {
        if (currentArm == ArmSide.Right)
        {
            if (LeftElbow.bone) LeftElbow.bone.rotation = LeftElbow.initialWorldRot;
            if (LeftShoulder.bone) LeftShoulder.bone.rotation = LeftShoulder.initialWorldRot;
        }
        else
        {
            if (RightElbow.bone) RightElbow.bone.rotation = RightElbow.initialWorldRot;
            if (RightShoulder.bone) RightShoulder.bone.rotation = RightShoulder.initialWorldRot;
        }
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

                if (currentArm == ArmSide.Right)
                {
                    if (i == 0) b = RightElbow;
                    else if (i == 1) b = RightShoulder;
                }
                else
                {
                    if (i == 0) b = LeftElbow;
                    else if (i == 1) b = LeftShoulder;
                }

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

                // ΕΔΩ ΕΙΝΑΙ Η ΑΛΛΑΓΗ ΓΙΑ ΝΑ ΔΟΥΛΕΨΕΙ ΤΟ LEFT BONE
                float pMult = b.Pitch_Multiplier;
                float yMult = b.Yaw_Multiplier;
                float rMult = b.Roll_Multiplier;

                // Ελέγχουμε αν πρόκειται για το αριστερό χέρι ΚΑΙ αν το "b" είναι τύπου Left_Bone
                if (currentArm == ArmSide.Left && b is Left_Bone leftB)
                {
                    if (leftB.InvertPitchOnLeft) pMult *= -1f;
                    if (leftB.InvertYawOnLeft) yMult *= -1f;
                    if (leftB.InvertRollOnLeft) rMult *= -1f;
                }

                float worldX = GetVal(dx, dy, dz, b.Source_For_Pitch) * pMult;
                float worldY = GetVal(dx, dy, dz, b.Source_For_Yaw) * yMult;
                float worldZ = GetVal(dx, dy, dz, b.Source_For_Roll) * rMult;

                Quaternion deltaRotation = Quaternion.Euler(worldX, worldY, worldZ);
                Quaternion targetRotation = deltaRotation * b.initialWorldRot;

                b.bone.rotation = Quaternion.Slerp(b.bone.rotation, targetRotation, Time.deltaTime * b.SmoothSpeed);
            }

            if (calibrateNextFrame)
            {
                calibrateNextFrame = false;
                Invoke(nameof(ShowCalibrationMessage), 2f);
            }
        }
        catch { }
    }

    void ShowCalibrationMessage()
    {
        if (statusText != null)
        {
            CancelInvoke(nameof(HideStatusText));

            statusText.gameObject.SetActive(true);
            statusText.text = "Calibration Completed!";
            statusText.color = Color.cyan;

            Invoke(nameof(HideStatusText), 3f);
        }
    }

    void HideStatusText()
    {
        if (statusText != null)
            statusText.gameObject.SetActive(false);
    }

    float GetVal(float x, float y, float z, SourceAxis axis)
    {
        if (axis == SourceAxis.X) return x;
        if (axis == SourceAxis.Y) return y;
        return z;
    }
}