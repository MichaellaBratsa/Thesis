using System.Diagnostics;
using UnityEngine;

public class MotionManager : MonoBehaviour
{
    [Header("Rig mapping (sensor → bones)")]
    [Tooltip("Optional: character root, used if you want to rotate avatar with shoulder yaw.")]
    public Transform characterRoot;

    [Tooltip("Bone driven by sensor id 0 (shoulder / upper arm).")]
    public Transform shoulderBone;

    [Tooltip("Bone driven by sensor id 1 (forearm).")]
    public Transform forearmBone;

    [Tooltip("Bone driven by sensor id 2 (hand).")]
    public Transform handBone;

    [Header("Smoothing")]
    [Range(0f, 30f)]
    public float boneLerpSpeed = 10f;

    [Range(0f, 30f)]
    public float rootYawLerpSpeed = 6f;

    [Tooltip("Αν θες να γυρίζει και το avatar (root) με τον ώμο.")]
    public bool driveRootYawFromShoulder = false;

    // Raw IMU quaternions όπως έρχονται από BleBridge (world space, x,y,z,w)
    private readonly Quaternion[] _sensorWorld =
    {
        Quaternion.identity,
        Quaternion.identity,
        Quaternion.identity
    };

    private readonly bool[] _hasData = { false, false, false };

    // Calibration / mapping
    // shoulderBoneWorld = _shoulderMapWorld * sensor0World
    private Quaternion _shoulderMapWorld = Quaternion.identity;

    // Relative mapping for segments:
    // forearmRelBone = _forearmDeltaRel * forearmRelSensor
    // handRelBone    = _handDeltaRel    * handRelSensor
    private Quaternion _forearmDeltaRel = Quaternion.identity;
    private Quaternion _handDeltaRel = Quaternion.identity;

    // For optional root yaw mapping
    private Quaternion _shoulderBoneCalibWorld = Quaternion.identity;
    private Quaternion _rootCalibWorld = Quaternion.identity;
    private Quaternion _rootYawOffset = Quaternion.identity;

    public bool IsCalibrated { get; private set; } = false;

    void Awake()
    {
        ResetCalibration();
    }

    /// <summary>
    /// Καλείται από BleBridge κάθε φορά που έρχεται νέο quaternion (world).
    /// id: 0 = shoulder, 1 = forearm, 2 = hand.
    /// </summary>
    public void UpdateSensorRotation(int id, Quaternion incomingWorld)
    {
        if (id < 0 || id > 2) return;

        _sensorWorld[id] = incomingWorld.normalized;
        _hasData[id] = true;
    }

    /// <summary>
    /// Manual calibration: βάλε avatar και αληθινό χέρι στην ίδια πόζα
    /// (π.χ. T-pose ή idle) και πάτα το κουμπί "Calibrate" (μέσω BleBridge).
    /// </summary>
    public void CalibrateToCurrentPose()
    {
        if (!_hasData[0] || shoulderBone == null)
        {
            UnityEngine.Debug.LogWarning("[MotionManager] Cannot calibrate: no sensor 0 data or shoulderBone is null.");
            return;
        }

        // Shoulder sensor as-is
        Quaternion s0 = _sensorWorld[0];           // sensor0 world
        Quaternion b0 = shoulderBone.rotation;     // shoulder bone world

        // --- Shoulder world mapping (absolute) ---
        // b0 = M * s0 => M = b0 * inverse(s0)
        _shoulderMapWorld = b0 * Quaternion.Inverse(s0);

        // --- Optional root yaw mapping ---
        if (driveRootYawFromShoulder && characterRoot != null)
        {
            _shoulderBoneCalibWorld = b0;
            _rootCalibWorld = characterRoot.rotation;

            Quaternion yawShoulderCalib = ExtractYaw(_shoulderBoneCalibWorld);
            Quaternion yawRootCalib = ExtractYaw(_rootCalibWorld);

            // RootYaw = YawShoulder * offset
            _rootYawOffset = yawRootCalib * Quaternion.Inverse(yawShoulderCalib);
        }
        else
        {
            _rootYawOffset = Quaternion.identity;
        }

        // We will yaw-lock children to the parent even at calibration time
        // so we are consistent later.

        // --- Forearm relative mapping (forearm vs shoulder) ---
        if (_hasData[1] && forearmBone != null)
        {
            // Align forearm yaw to shoulder yaw at calibration
            Quaternion s1Aligned = AlignYaw(_sensorWorld[1], s0);
            Quaternion b1 = forearmBone.rotation;

            // Sensor relative at calibration
            Quaternion forearmRelSensorCalib = Quaternion.Inverse(s0) * s1Aligned;
            // Bone relative at calibration
            Quaternion forearmRelBoneCalib = Quaternion.Inverse(b0) * b1;

            // forearmRelBone = Δ * forearmRelSensor  =>  Δ = forearmRelBoneCalib * inverse(forearmRelSensorCalib)
            _forearmDeltaRel = forearmRelBoneCalib * Quaternion.Inverse(forearmRelSensorCalib);
        }
        else
        {
            _forearmDeltaRel = Quaternion.identity;
        }

        // --- Hand relative mapping (hand vs forearm) ---
        if (_hasData[2] && handBone != null && _hasData[1])
        {
            // Yaw-lock hand to forearm at calibration
            Quaternion s0Cal = s0;
            Quaternion s1Aligned = AlignYaw(_sensorWorld[1], s0Cal);
            Quaternion s2Aligned = AlignYaw(_sensorWorld[2], s1Aligned);

            Quaternion b1 = forearmBone != null ? forearmBone.rotation : b0;
            Quaternion b2 = handBone.rotation;

            Quaternion handRelSensorCalib = Quaternion.Inverse(s1Aligned) * s2Aligned;
            Quaternion handRelBoneCalib = Quaternion.Inverse(b1) * b2;

            _handDeltaRel = handRelBoneCalib * Quaternion.Inverse(handRelSensorCalib);
        }
        else
        {
            _handDeltaRel = Quaternion.identity;
        }

        IsCalibrated = true;
        UnityEngine.Debug.Log("[MotionManager] Calibration complete (shoulder+relative forearm/hand with yaw lock).");
    }

    public void ResetCalibration()
    {
        _shoulderMapWorld = Quaternion.identity;
        _forearmDeltaRel = Quaternion.identity;
        _handDeltaRel = Quaternion.identity;
        _rootYawOffset = Quaternion.identity;

        IsCalibrated = false;
        UnityEngine.Debug.Log("[MotionManager] Calibration reset.");
    }

    void LateUpdate()
    {
        if (!IsCalibrated) return;
        if (!_hasData[0] || shoulderBone == null) return;

        // --- Shoulder ---
        Quaternion s0 = _sensorWorld[0];
        Quaternion shoulderTargetWorld = _shoulderMapWorld * s0;

        // Optional: rotate the avatar root by shoulder yaw
        if (driveRootYawFromShoulder && characterRoot != null)
        {
            Quaternion currentShoulderYaw = ExtractYaw(shoulderTargetWorld);
            Quaternion rootTargetYaw = currentShoulderYaw * _rootYawOffset;

            if (rootYawLerpSpeed > 0f)
            {
                characterRoot.rotation = Quaternion.Slerp(
                    characterRoot.rotation,
                    rootTargetYaw,
                    Time.deltaTime * rootYawLerpSpeed
                );
            }
            else
            {
                characterRoot.rotation = rootTargetYaw;
            }
        }

        // Apply shoulder bone
        ApplyBoneRotation(shoulderBone, shoulderTargetWorld);

        // Prepare yaw-locked sensor orientations
        Quaternion s1Aligned = _hasData[1] ? AlignYaw(_sensorWorld[1], s0) : Quaternion.identity;
        Quaternion s2Aligned = _hasData[2] ? AlignYaw(_sensorWorld[2], (_hasData[1] ? s1Aligned : s0)) : Quaternion.identity;

        // --- Forearm (relative to shoulder) ---
        Quaternion forearmTargetWorld = shoulderTargetWorld;
        if (forearmBone != null && _hasData[1])
        {
            // Current sensor relative: forearm vs shoulder, AFTER yaw lock
            Quaternion forearmRelSensor = Quaternion.Inverse(s0) * s1Aligned;
            // Map to bone space
            Quaternion forearmRelBone = _forearmDeltaRel * forearmRelSensor;
            forearmTargetWorld = shoulderTargetWorld * forearmRelBone;

            ApplyBoneRotation(forearmBone, forearmTargetWorld);
        }

        // --- Hand (relative to forearm) ---
        if (handBone != null && _hasData[2] && _hasData[1])
        {
            // Sensor hand relative to forearm, AFTER yaw lock
            Quaternion handRelSensor = Quaternion.Inverse(s1Aligned) * s2Aligned;
            Quaternion handRelBone = _handDeltaRel * handRelSensor;

            Quaternion handTargetWorld = forearmTargetWorld * handRelBone;

            ApplyBoneRotation(handBone, handTargetWorld);
        }
    }

    private void ApplyBoneRotation(Transform bone, Quaternion targetWorld)
    {
        if (bone == null) return;

        if (boneLerpSpeed > 0f)
        {
            bone.rotation = Quaternion.Slerp(
                bone.rotation,
                targetWorld,
                Time.deltaTime * boneLerpSpeed
            );
        }
        else
        {
            bone.rotation = targetWorld;
        }
    }

    private static Quaternion ExtractYaw(Quaternion q)
    {
        Vector3 euler = q.eulerAngles;
        return Quaternion.Euler(0f, euler.y, 0f);
    }

    // NEW: force one quaternion to have the same yaw as another
    private static Quaternion AlignYaw(Quaternion source, Quaternion reference)
    {
        Vector3 srcE = source.eulerAngles;
        Vector3 refE = reference.eulerAngles;
        srcE.y = refE.y; // copy yaw
        return Quaternion.Euler(srcE);
    }

    void OnGUI()
    {
        if (!IsCalibrated)
        {
            GUI.Label(new Rect(10, 10, 700, 40),
                "NOT CALIBRATED: βάλε avatar & αληθινό χέρι στην ίδια πόζα και πάτα 'Calibrate'.");
        }
    }

    // --- Optional helpers if needed από άλλα scripts ---

    public Quaternion GetSensorWorld(int id)
    {
        if (id < 0 || id > 2) return Quaternion.identity;
        return _sensorWorld[id];
    }

    public bool HasData(int id)
    {
        if (id < 0 || id > 2) return false;
        return _hasData[id];
    }
}
