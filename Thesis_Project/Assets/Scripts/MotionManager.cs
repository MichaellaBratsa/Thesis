using UnityEngine;

public class MotionManager : MonoBehaviour
{
    [Header("Targets per sensor id")]
    public Transform sensor0Target; // id=0
    public Transform sensor1Target; // id=1
    public Transform sensor2Target; // id=2

    [Header("Optional offsets (deg) applied after the incoming rotation")]
    public Vector3 sensor0OffsetEuler;
    public Vector3 sensor1OffsetEuler;
    public Vector3 sensor2OffsetEuler;

    [Header("Optional smoothing")]
    [Range(0f, 30f)] public float rotationLerpSpeed = 12f; // 0 => instant

    // Δημόσια μέθοδος που καλεί το JavaBridge:
    public void UpdateSensorRotation(int id, Quaternion incoming)
    {
        Transform t = GetTarget(id);
        if (t == null) return;

        // Εφάρμοσε offset αν χρειάζεται (μετά τον incoming προσανατολισμό)
        var offset = GetOffset(id);
        Quaternion final = incoming * Quaternion.Euler(offset);

        if (rotationLerpSpeed > 0f)
            t.rotation = Quaternion.Slerp(t.rotation, final, Time.deltaTime * rotationLerpSpeed);
        else
            t.rotation = final;
    }

    // (Προαιρετικό) Αν κάπου θέλεις να περάσεις Euler αντί για Quaternion:
    public void UpdateSensorEuler(int id, float rollDeg, float pitchDeg, float yawDeg)
    {
        UpdateSensorRotation(id, Quaternion.Euler(rollDeg, pitchDeg, yawDeg));
    }

    Transform GetTarget(int id)
    {
        switch (id)
        {
            case 0: return sensor0Target;
            case 1: return sensor1Target;
            case 2: return sensor2Target;
            default: return null;
        }
    }

    Vector3 GetOffset(int id)
    {
        switch (id)
        {
            case 0: return sensor0OffsetEuler;
            case 1: return sensor1OffsetEuler;
            case 2: return sensor2OffsetEuler;
            default: return Vector3.zero;
        }
    }
}
